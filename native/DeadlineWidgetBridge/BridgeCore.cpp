#include "BridgeCore.h"
#include <windows.h>
#include <utility>

namespace bridge {
Controller::Controller(Dependencies dependencies) : operations_(std::move(dependencies)) {}

int Controller::latest() const noexcept { return status_.load(); }

void Controller::dispatch(const wchar_t* command) noexcept {
    std::wstring root;
    std::wstring text;
    int result = 4;
    unsigned long error = ERROR_GEN_FAILURE;
    try {
        text = command ? command : L"";
        root = operations_.configuredRoot();
        if (root.empty()) root = operations_.knownFolderRoot();
        if (root.empty()) root = operations_.environmentRoot();
        if (text != L"OpenManager") {
            result = 2;
            error = ERROR_INVALID_PARAMETER;
        } else if (root.empty()) {
            result = 3;
            error = ERROR_PATH_NOT_FOUND;
        } else {
            const std::wstring executable = root + L"\\DeadlineWidgetV2\\Manager\\DeadlineWidget.Manager.exe";
            if (!operations_.isFile(executable)) {
                result = 3;
                error = ERROR_FILE_NOT_FOUND;
            } else {
                const auto outcome = operations_.start(executable, L"--activate", SW_SHOWNORMAL);
                // Closing is the first operation after launch. There is no wait boundary.
                if (outcome.process) operations_.close(outcome.process);
                result = outcome.started ? 1 : 4;
                error = outcome.started ? ERROR_SUCCESS : outcome.error;
            }
        }
    } catch (...) {
        result = 4;
        error = ERROR_GEN_FAILURE;
    }
    status_.store(result);
    try { operations_.record(root, text, result, error); } catch (...) {
        // Diagnostics cannot turn an accepted launch into a reported failure.
    }
}
}
