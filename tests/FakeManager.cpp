#include <windows.h>
#include <string>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    wchar_t path[32768]{};
    const DWORD length = GetEnvironmentVariableW(L"FAKE_MANAGER_LOG", path, 32768);
    if (length == 0 || length >= 32768) return 20;

    const std::wstring command = GetCommandLineW();
    const int byteCount = WideCharToMultiByte(CP_UTF8, 0, command.data(), static_cast<int>(command.size()), nullptr, 0, nullptr, nullptr);
    if (byteCount <= 0) return 21;
    std::string bytes(byteCount, '\0');
    if (WideCharToMultiByte(CP_UTF8, 0, command.data(), static_cast<int>(command.size()), bytes.data(), byteCount, nullptr, nullptr) != byteCount) return 22;

    HANDLE file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return 23;
    DWORD written = 0;
    const bool saved = WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr) != FALSE;
    CloseHandle(file);
    if (!saved || written != bytes.size()) return 24;

    wchar_t requestedWait[16]{};
    const DWORD waitLength = GetEnvironmentVariableW(L"FAKE_MANAGER_WAIT_MS", requestedWait, 16);
    if (waitLength > 0 && waitLength < 16) Sleep(_wtoi(requestedWait));

    wchar_t requestedExit[16]{};
    const DWORD exitLength = GetEnvironmentVariableW(L"FAKE_MANAGER_EXIT_CODE", requestedExit, 16);
    if (exitLength > 0 && exitLength < 16) return _wtoi(requestedExit);
    return 0;
}
