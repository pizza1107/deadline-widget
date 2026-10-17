#include "BridgeCore.h"
#include <windows.h>
#include <cstring>
#include <cwchar>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
unsigned assertions = 0;
void expect(bool condition, const char* description) {
    if (!condition) throw std::runtime_error(description);
    ++assertions;
}

// Fakes isolate OS launch side effects; the controller and DLL are real.
struct Scenario {
    std::wstring configuredRoot = L"C:\\测试 用户\\Configured Local Data";
    std::wstring knownRoot = L"C:\\测试 用户\\Known Local Data";
    std::wstring environmentRoot = L"C:\\测试 用户\\Environment Local Data";
    bool filePresent = true;
    bridge::LaunchOutcome outcome{true, 0, 123};
    std::vector<std::string> calls;
    std::wstring checkedPath, launchedPath, parameters, recordedRoot, recordedCommand;
    int showState = 0, recordedStatus = -1;
    unsigned long recordedError = 999;
    std::uintptr_t closedHandle = 0;

    bridge::Dependencies dependencies() {
        return {
            [this] { calls.push_back("configured"); return configuredRoot; },
            [this] { calls.push_back("known"); return knownRoot; },
            [this] { calls.push_back("environment"); return environmentRoot; },
            [this](const std::wstring& path) { calls.push_back("exists"); checkedPath = path; return filePresent; },
            [this](const std::wstring& path, const std::wstring& args, int show) {
                calls.push_back("start"); launchedPath = path; parameters = args; showState = show; return outcome;
            },
            [this](std::uintptr_t handle) { calls.push_back("close"); closedHandle = handle; },
            [this](const std::wstring& base, const std::wstring& command, int status, unsigned long error) {
                calls.push_back("record"); recordedRoot = base; recordedCommand = command;
                recordedStatus = status; recordedError = error;
            }
        };
    }
};

void controllerContract() {
    Scenario accepted;
    accepted.configuredRoot.clear();
    accepted.environmentRoot.clear();
    bridge::Controller controller(accepted.dependencies());
    expect(controller.latest() == 0, "new instance reports idle");
    controller.dispatch(L"OpenManager");
    expect(controller.latest() == 1, "accepted launch updates status to 1");
    expect(accepted.checkedPath == L"C:\\测试 用户\\Known Local Data\\DeadlineWidgetV2\\Manager\\DeadlineWidget.Manager.exe", "checks exact manager path resolved from Known Folder");
    expect(accepted.launchedPath == L"C:\\测试 用户\\Known Local Data\\DeadlineWidgetV2\\Manager\\DeadlineWidget.Manager.exe", "launches exact manager path resolved from Known Folder");
    expect(accepted.parameters == L"--activate", "passes only --activate");
    expect(accepted.showState == SW_SHOWNORMAL, "launches normal window");
    expect(accepted.closedHandle == 123, "closes returned process handle");
    expect(accepted.calls == std::vector<std::string>{"configured", "known", "exists", "start", "close", "record"}, "Known Folder success follows an absent configured root and does not consult the environment");
    expect(accepted.recordedRoot == accepted.knownRoot && accepted.recordedCommand == L"OpenManager" && accepted.recordedStatus == 1 && accepted.recordedError == 0, "records the Known Folder root in launch diagnostics");

    Scenario fallback;
    fallback.configuredRoot.clear();
    fallback.knownRoot.clear();
    bridge::Controller fallbackController(fallback.dependencies());
    fallbackController.dispatch(L"OpenManager");
    expect(fallbackController.latest() == 1, "environment root is accepted after Known Folder failure");
    expect(fallback.calls == std::vector<std::string>{"configured", "known", "environment", "exists", "start", "close", "record"}, "consults the environment only after configured and Known Folder failure");
    expect(fallback.checkedPath == L"C:\\测试 用户\\Environment Local Data\\DeadlineWidgetV2\\Manager\\DeadlineWidget.Manager.exe", "checks exact manager path resolved from the environment fallback");
    expect(fallback.launchedPath == fallback.checkedPath, "launches the environment fallback manager path");
    expect(fallback.recordedRoot == fallback.environmentRoot, "records the environment fallback root in diagnostics");

    Scenario restartSafe;
    restartSafe.knownRoot.clear();
    restartSafe.environmentRoot.clear();
    bridge::Controller restartSafeController(restartSafe.dependencies());
    restartSafeController.dispatch(L"OpenManager");
    expect(restartSafeController.latest() == 1, "configured root launches after restart without Known Folder or environment access");
    expect(restartSafe.checkedPath == L"C:\\测试 用户\\Configured Local Data\\DeadlineWidgetV2\\Manager\\DeadlineWidget.Manager.exe", "configured root resolves the exact manager path");
    expect(restartSafe.calls == std::vector<std::string>{"configured", "exists", "start", "close", "record"}, "configured root avoids every process-environment dependency");

    for (const wchar_t* command : {L"", L"openmanager", L"OpenManager --activate", L" OpenManager", L"Other\r\nInjected", static_cast<const wchar_t*>(nullptr)}) {
        Scenario rejected;
        rejected.configuredRoot.clear();
        bridge::Controller instance(rejected.dependencies());
        instance.dispatch(command);
        expect(instance.latest() == 2, "rejects every command except exact OpenManager");
        expect(rejected.calls == std::vector<std::string>{"configured", "known", "record"}, "unknown command resolves roots but never checks file or starts a process");
        expect(rejected.recordedStatus == 2 && rejected.recordedError == ERROR_INVALID_PARAMETER, "records invalid-command diagnostic");
        expect(rejected.recordedCommand == (command ? command : L""), "records command safely including null");
    }
    controller.dispatch(L"invalid");
    expect(controller.latest() == 2, "new command replaces prior successful status");

    Scenario missing;
    missing.filePresent = false;
    bridge::Controller missingController(missing.dependencies());
    missingController.dispatch(L"OpenManager");
    expect(missingController.latest() == 3 && missing.recordedError == ERROR_FILE_NOT_FOUND, "missing executable reports 3 and file-not-found");
    expect(missing.calls == std::vector<std::string>{"configured", "exists", "record"}, "missing executable cannot start");

    Scenario noRoot;
    noRoot.configuredRoot.clear();
    noRoot.knownRoot.clear();
    noRoot.environmentRoot.clear();
    bridge::Controller noRootController(noRoot.dependencies());
    noRootController.dispatch(L"OpenManager");
    expect(noRootController.latest() == 3 && noRoot.recordedError == ERROR_PATH_NOT_FOUND, "both root sources failing reports missing path");
    expect(noRoot.calls == std::vector<std::string>{"configured", "known", "environment", "record"}, "all root sources failing cannot construct a relative launch path");

    Scenario denied;
    denied.outcome = {false, ERROR_ACCESS_DENIED, 123};
    bridge::Controller deniedController(denied.dependencies());
    deniedController.dispatch(L"OpenManager");
    expect(deniedController.latest() == 4 && denied.recordedError == ERROR_ACCESS_DENIED, "preserves ShellExecute error with status 4");
    expect(denied.closedHandle == 123 && denied.calls == std::vector<std::string>{"configured", "exists", "start", "close", "record"}, "closes handle immediately even after failed launch");

    Scenario handleless;
    handleless.outcome.process = 0;
    bridge::Controller handlelessController(handleless.dependencies());
    handlelessController.dispatch(L"OpenManager");
    expect(handlelessController.latest() == 1 && handleless.calls == std::vector<std::string>{"configured", "exists", "start", "record"}, "accepted handleless launch needs no close");

    for (int failurePoint = 0; failurePoint < 4; ++failurePoint) {
        Scenario throwing;
        auto operations = throwing.dependencies();
        if (failurePoint == 0) operations.configuredRoot = []() -> std::wstring { throw std::bad_alloc(); };
        if (failurePoint == 1) {
            throwing.configuredRoot.clear();
            throwing.knownRoot.clear();
            operations.environmentRoot = []() -> std::wstring { throw std::bad_alloc(); };
        }
        if (failurePoint == 2) operations.isFile = [](const std::wstring&) -> bool { throw std::runtime_error("filesystem"); };
        if (failurePoint == 3) operations.start = [](const std::wstring&, const std::wstring&, int) -> bridge::LaunchOutcome { throw std::runtime_error("launcher"); };
        bridge::Controller guarded(std::move(operations));
        guarded.dispatch(L"OpenManager");
        expect(guarded.latest() == 4, "contains dependency exception and reports failure");
    }
    Scenario brokenLog;
    auto operations = brokenLog.dependencies();
    operations.record = [](const std::wstring&, const std::wstring&, int, unsigned long) { throw std::runtime_error("logger"); };
    bridge::Controller loggingGuard(std::move(operations));
    loggingGuard.dispatch(L"OpenManager");
    expect(loggingGuard.latest() == 1 && brokenLog.closedHandle == 123, "logger failure preserves launch result and closed handle");
}

void knownFolderProviderContract() {
    expect(!bridge::readKnownFolderRoot().empty(), "production Known Folder provider resolves Local AppData without environment fallback");
}

template<class Signature> Signature loadExport(HMODULE module, const char* name) {
    FARPROC address = GetProcAddress(module, name);
    Signature callable = nullptr;
    static_assert(sizeof(callable) == sizeof(address));
    std::memcpy(&callable, &address, sizeof(callable));
    return callable;
}

void exportContract(const wchar_t* path) {
    HMODULE module = LoadLibraryW(path);
    expect(module != nullptr, "loads DLL without Rainmeter runtime");
    auto initialize = loadExport<void (*)(void**, void*)>(module, "Initialize");
    auto reload = loadExport<void (*)(void*, void*, double*)>(module, "Reload");
    auto update = loadExport<double (*)(void*)>(module, "Update");
    auto execute = loadExport<void (*)(void*, const wchar_t*)>(module, "ExecuteBang");
    auto finalize = loadExport<void (*)(void*)>(module, "Finalize");
    expect(initialize && reload && update && execute && finalize, "loads all five exact callback names");
    void* state = nullptr;
    initialize(&state, nullptr);
    expect(state != nullptr && update(state) == 0, "Initialize creates idle state");
    double maximum = -1;
    reload(state, nullptr, &maximum);
    expect(maximum == 4, "Reload exposes status range");
    execute(state, L"Other\r\nInjected");
    expect(update(state) == 2, "ExecuteBang rejects unknown command through DLL");
    execute(state, L"OpenManager");
    expect(update(state) == 3, "DLL refuses absent manager or directory at manager path");
    finalize(state);
    initialize(nullptr, nullptr);
    reload(nullptr, nullptr, nullptr);
    execute(nullptr, nullptr);
    finalize(nullptr);
    expect(update(nullptr) == 4, "null callbacks are safe and Update reports failure");
    expect(FreeLibrary(module) != FALSE, "unloads DLL");
}

void launchContract(const wchar_t* path) {
    HMODULE module = LoadLibraryW(path);
    expect(module != nullptr, "loads bridge DLL for Unicode-path launch");
    auto initialize = loadExport<void (*)(void**, void*)>(module, "Initialize");
    auto update = loadExport<double (*)(void*)>(module, "Update");
    auto execute = loadExport<void (*)(void*, const wchar_t*)>(module, "ExecuteBang");
    auto finalize = loadExport<void (*)(void*)>(module, "Finalize");
    expect(initialize && update && execute && finalize, "loads launch callbacks");
    void* state = nullptr;
    initialize(&state, nullptr);
    expect(state != nullptr, "initializes bridge launch state");
    execute(state, L"OpenManager");
    expect(update(state) == 1, "real bridge starts the Unicode-path manager");
    finalize(state);
    expect(FreeLibrary(module) != FALSE, "unloads bridge after launch");
}
}

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc == 3 && std::wcscmp(argv[2], L"--launch") == 0) {
            launchContract(argv[1]);
            std::cout << "PASS: " << assertions << " real-DLL launch assertions\n";
            return 0;
        }
        expect(argc == 2, "requires DLL path for integration checks");
        knownFolderProviderContract();
        controllerContract();
        exportContract(argv[1]);
        std::cout << "PASS: " << assertions << " native assertions\n";
        return 0;
    } catch (const std::exception& failure) {
        std::cerr << "FAIL: " << failure.what() << '\n';
        return 1;
    }
}
