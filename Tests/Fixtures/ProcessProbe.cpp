#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <string>
#include <thread>
#include <vector>
#ifdef _WIN32
#include <windows.h>
#else
#include <sched.h>
#include <unistd.h>
#endif

int main(int argc, char **argv) {
    if (argc < 2)
        return 2;
    const std::string mode = argv[1];
    if (mode == "--version") {
        std::cout << "FailureFixture 1.0";
        return 0;
    }
    if (mode.rfind("-std=", 0) == 0 && std::getenv("BENCHMARK_TEST_SLOW_BUILD")) {
        std::cout << "BuildStarted ";
#ifdef _WIN32
        std::cout << GetCurrentProcessId() << std::endl;
#else
        std::cout << getpid() << std::endl;
#endif
        std::this_thread::sleep_for(std::chrono::seconds(30));
        return 2;
    }
    if (mode == "fail")
        return 7;
    if (mode == "echo") {
        if (argc != 3)
            return 2;
        std::cout << argv[2];
        return 0;
    }
    if (mode == "sleep") {
        std::this_thread::sleep_for(std::chrono::seconds(8));
        return 0;
    }
    if (mode == "memory") {
        std::vector<std::uint8_t> bytes(64 * 1024 * 1024);
        volatile std::uint8_t *touched = bytes.data();
        for (std::size_t i = 0; i < bytes.size(); i += 4096)
            touched[i] = 1;
        std::cout << int(touched[bytes.size() - 4096]);
        return 0;
    }
    if (mode == "affinity") {
#ifdef _WIN32
        DWORD_PTR mask = 0, system = 0;
        if (!GetProcessAffinityMask(GetCurrentProcess(), &mask, &system))
            return 2;
        int count = 0;
        while (mask) {
            count += int(mask & 1);
            mask >>= 1;
        }
        std::cout << count;
#else
        cpu_set_t set;
        CPU_ZERO(&set);
        if (sched_getaffinity(0, sizeof(set), &set) != 0)
            return 2;
        std::cout << CPU_COUNT(&set);
#endif
        return 0;
    }
    if (mode == "spawn") {
#ifdef _WIN32
        std::string command = "\"" + std::string(argv[0]) + "\" sleep";
        STARTUPINFOA startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION child{};
        if (!CreateProcessA(nullptr, command.data(), nullptr, nullptr, false, CREATE_NO_WINDOW, nullptr, nullptr,
                            &startup, &child))
            return 2;
        std::cout << child.dwProcessId << std::endl;
        CloseHandle(child.hThread);
        CloseHandle(child.hProcess);
#else
        const pid_t child = fork();
        if (child < 0)
            return 2;
        if (child == 0) {
            execl(argv[0], argv[0], "sleep", nullptr);
            return 2;
        }
        std::cout << child << std::endl;
#endif
        std::this_thread::sleep_for(std::chrono::seconds(8));
        return 0;
    }
    return 2;
}
