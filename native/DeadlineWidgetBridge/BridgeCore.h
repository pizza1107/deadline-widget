#pragma once

#include <atomic>
#include <cstdint>
#include <functional>
#include <string>

namespace bridge {
struct LaunchOutcome {
    bool started;
    unsigned long error;
    std::uintptr_t process;
};

struct Dependencies {
    std::function<std::wstring()> configuredRoot;
    std::function<std::wstring()> knownFolderRoot;
    std::function<std::wstring()> environmentRoot;
    std::function<bool(const std::wstring&)> isFile;
    std::function<LaunchOutcome(const std::wstring&, const std::wstring&, int)> start;
    std::function<void(std::uintptr_t)> close;
    std::function<void(const std::wstring&, const std::wstring&, int, unsigned long)> record;
};

std::wstring readKnownFolderRoot();

class Controller {
public:
    explicit Controller(Dependencies dependencies);
    int latest() const noexcept;
    void dispatch(const wchar_t* command) noexcept;

private:
    Dependencies operations_;
    std::atomic<int> status_{0};
};
}
