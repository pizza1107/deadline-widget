// Original Win32 implementation of the documented Rainmeter callback ABI.
#include "BridgeCore.h"
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <cwchar>
#include <vector>

namespace {
HMODULE bridgeModule = nullptr;

std::wstring readConfiguredRoot() {
    if (!bridgeModule) return {};
    std::vector<wchar_t> modulePath(32768);
    const DWORD length = GetModuleFileNameW(bridgeModule, modulePath.data(), static_cast<DWORD>(modulePath.size()));
    if (length == 0 || length >= modulePath.size()) return {};
    std::wstring configPath(modulePath.data(), length);
    const auto separator = configPath.find_last_of(L"\\/");
    if (separator == std::wstring::npos) return {};
    configPath.resize(separator + 1);
    configPath += L"DeadlineWidgetBridge.root";

    HANDLE file = CreateFileW(configPath.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart <= 0 || size.QuadPart > 32768) {
        CloseHandle(file);
        return {};
    }
    std::string bytes(static_cast<size_t>(size.QuadPart), '\0');
    DWORD read = 0;
    const bool readOk = ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr) != FALSE;
    CloseHandle(file);
    if (!readOk || read != bytes.size()) return {};

    const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
    if (count <= 0) return {};
    std::wstring root(count, L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), root.data(), count) != count) return {};
    while (!root.empty() && (root.back() == L'\r' || root.back() == L'\n' || root.back() == L' ' || root.back() == L'\t')) root.pop_back();
    if (root.size() < 3 || root[1] != L':' || (root[2] != L'\\' && root[2] != L'/')) return {};
    return root;
}

std::wstring readLocalRoot() {
    const DWORD capacity = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
    if (!capacity) return {};
    std::wstring value(capacity, L'\0');
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", value.data(), capacity);
    if (length == 0 || length >= capacity) return {};
    value.resize(length);
    return value;
}
}

namespace bridge {
std::wstring readKnownFolderRoot() {
#ifdef DEADLINE_WIDGET_BRIDGE_TEST_KNOWN_FOLDER_FROM_ENVIRONMENT
    return readLocalRoot();
#else
    struct KnownFolderPath {
        PWSTR value = nullptr;
        ~KnownFolderPath() { CoTaskMemFree(value); }
    } path;
    const HRESULT result = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &path.value);
    if (FAILED(result) || !path.value || !*path.value) return {};
    return path.value;
#endif
}
}

namespace {
bool isExecutableFile(const std::wstring& path) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

bridge::LaunchOutcome startManager(const std::wstring& executable, const std::wstring& arguments, int showState) {
    SHELLEXECUTEINFOW request{};
    request.cbSize = sizeof(request);
    request.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
    request.lpVerb = L"open";
    request.lpFile = executable.c_str();
    request.lpParameters = arguments.c_str();
    request.nShow = showState;
    SetLastError(ERROR_SUCCESS);
    const bool started = ShellExecuteExW(&request) != FALSE;
    DWORD error = started ? ERROR_SUCCESS : GetLastError();
    if (!started && error == ERROR_SUCCESS) {
        const auto shellError = reinterpret_cast<std::uintptr_t>(request.hInstApp);
        error = shellError > 0 && shellError <= 32 ? static_cast<DWORD>(shellError) : ERROR_GEN_FAILURE;
    }
    return {started, error, reinterpret_cast<std::uintptr_t>(request.hProcess)};
}

std::wstring quoteCommand(const std::wstring& command) {
    std::wstring output;
    for (wchar_t character : command) {
        switch (character) {
        case L'\\': output += L"\\\\"; break;
        case L'"': output += L"\\\""; break;
        case L'\r': output += L"\\r"; break;
        case L'\n': output += L"\\n"; break;
        case L'\t': output += L"\\t"; break;
        default: output += character < 32 ? L'?' : character; break;
        }
    }
    return output;
}

void appendDiagnostic(const std::wstring& root, const std::wstring& command, int status, unsigned long error) {
    if (root.empty()) return;
    const std::wstring appPath = root + L"\\DeadlineWidgetV2";
    const std::wstring logPath = appPath + L"\\logs";
    CreateDirectoryW(appPath.c_str(), nullptr);
    CreateDirectoryW(logPath.c_str(), nullptr);
    SYSTEMTIME now{};
    GetSystemTime(&now);
    wchar_t timestamp[32]{};
    std::swprintf(timestamp, 32, L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ", now.wYear, now.wMonth,
                  now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
    const std::wstring line = std::wstring(timestamp) + L" command=\"" + quoteCommand(command) +
        L"\" status=" + std::to_wstring(status) + L" error=" + std::to_wstring(error) + L"\r\n";
    const int count = WideCharToMultiByte(CP_UTF8, 0, line.data(), static_cast<int>(line.size()), nullptr, 0, nullptr, nullptr);
    if (count <= 0) return;
    std::string bytes(count, '\0');
    if (WideCharToMultiByte(CP_UTF8, 0, line.data(), static_cast<int>(line.size()), bytes.data(), count, nullptr, nullptr) != count) return;
    const std::wstring filePath = logPath + L"\\bridge.log";
    HANDLE file = CreateFileW(filePath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                             nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr);
    CloseHandle(file);
}

bridge::Dependencies windowsDependencies() {
    return {readConfiguredRoot, bridge::readKnownFolderRoot, readLocalRoot, isExecutableFile, startManager,
            [](std::uintptr_t process) { CloseHandle(reinterpret_cast<HANDLE>(process)); }, appendDiagnostic};
}
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) bridgeModule = module;
    return TRUE;
}

#define BRIDGE_CALLBACK extern "C" __declspec(dllexport)

BRIDGE_CALLBACK void Initialize(void** data, void*) noexcept {
    if (!data) return;
    *data = nullptr;
    try { *data = new bridge::Controller(windowsDependencies()); } catch (...) {}
}

BRIDGE_CALLBACK void Reload(void*, void*, double* maximum) noexcept {
    if (maximum) *maximum = 4.0;
}

BRIDGE_CALLBACK double Update(void* data) noexcept {
    try { return data ? static_cast<bridge::Controller*>(data)->latest() : 4.0; } catch (...) { return 4.0; }
}

BRIDGE_CALLBACK void ExecuteBang(void* data, const wchar_t* command) noexcept {
    try { if (data) static_cast<bridge::Controller*>(data)->dispatch(command); } catch (...) {}
}

BRIDGE_CALLBACK void Finalize(void* data) noexcept {
    try { delete static_cast<bridge::Controller*>(data); } catch (...) {}
}
