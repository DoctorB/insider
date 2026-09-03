#include "fixture_protocol.h"

#include <windows.h>

#include <algorithm>
#include <cctype>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace
{
    template <typename TFunction>
    bool ResolveExport(HMODULE module, const char* name, TFunction& function)
    {
        const auto address = GetProcAddress(module, name);
        if (address == nullptr)
        {
            return false;
        }

        static_assert(sizeof(function) == sizeof(address));
        std::memcpy(&function, &address, sizeof(function));
        return true;
    }

    std::wstring GetProcessPath()
    {
        std::vector<wchar_t> buffer(MAX_PATH);
        for (;;)
        {
            const auto length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (length == 0)
            {
                return {};
            }

            if (static_cast<size_t>(length) < buffer.size() - 1)
            {
                return std::wstring(buffer.data(), length);
            }

            buffer.resize(buffer.size() * 2);
        }
    }

    bool EndsWithIgnoringCase(std::string value, std::string suffix)
    {
        if (value.size() < suffix.size())
        {
            return false;
        }

        std::transform(value.begin(), value.end(), value.begin(), [](unsigned char character)
        {
            return static_cast<char>(std::tolower(character));
        });
        std::transform(suffix.begin(), suffix.end(), suffix.begin(), [](unsigned char character)
        {
            return static_cast<char>(std::tolower(character));
        });
        return value.compare(value.size() - suffix.size(), suffix.size(), suffix) == 0;
    }

    bool WaitForLogMessage(const std::filesystem::path& path, const std::string& expected_message)
    {
        constexpr DWORD retry_delay_ms = 10;
        constexpr DWORD timeout_ms = 2'000;
        for (DWORD elapsed = 0; elapsed < timeout_ms; elapsed += retry_delay_ms)
        {
            std::ifstream stream(path, std::ios::binary);
            if (stream)
            {
                const std::string contents(
                    (std::istreambuf_iterator<char>(stream)),
                    std::istreambuf_iterator<char>());
                if (contents.find(expected_message) != std::string::npos)
                {
                    return true;
                }
            }

            Sleep(retry_delay_ms);
        }

        return false;
    }
}

int wmain(int argument_count, wchar_t** arguments)
{
    if (argument_count != 4)
    {
        std::cerr << "Expected a scenario, fake Mono DLL, and native proxy path." << std::endl;
        return 2;
    }

    const std::wstring scenario = arguments[1];
    const auto root_domain_delay = scenario == L"delayed-domain" ? 3U : 0U;
    const auto invoke_exception = scenario == L"managed-exception" ? TRUE : FALSE;
    if (scenario != L"success" && scenario != L"delayed-domain" && scenario != L"managed-exception")
    {
        std::cerr << "Unknown fixture scenario." << std::endl;
        return 2;
    }

    const auto fake_mono = LoadLibraryW(arguments[2]);
    if (fake_mono == nullptr)
    {
        std::cerr << "Could not load the fake Mono runtime." << std::endl;
        return 1;
    }

    FixtureState fixture_state = nullptr;
    FixtureFailure fixture_failure = nullptr;
    FixtureAssemblyPath fixture_assembly_path = nullptr;
    FixtureConfigure fixture_configure = nullptr;
    FixtureRootRequests fixture_root_requests = nullptr;
    if (!ResolveExport(fake_mono, "insider_fixture_state", fixture_state) ||
        !ResolveExport(fake_mono, "insider_fixture_failure", fixture_failure) ||
        !ResolveExport(fake_mono, "insider_fixture_assembly_path", fixture_assembly_path) ||
        !ResolveExport(fake_mono, "insider_fixture_configure", fixture_configure) ||
        !ResolveExport(fake_mono, "insider_fixture_root_requests", fixture_root_requests))
    {
        std::cerr << "The fake Mono runtime is missing fixture exports." << std::endl;
        return 1;
    }

    fixture_configure(root_domain_delay, invoke_exception);

    const auto proxy_path = std::filesystem::path(arguments[3]);
    const auto log_path = proxy_path.parent_path() / L"Insider" / L"logs" / L"native.log";
    const auto previous_log_path = proxy_path.parent_path() / L"Insider" / L"logs" / L"native.previous.log";
    std::error_code delete_error;
    std::filesystem::create_directories(log_path.parent_path(), delete_error);
    {
        std::ofstream previous_log(previous_log_path, std::ios::binary | std::ios::trunc);
        previous_log << "stale previous native log";
        std::ofstream current_log(log_path, std::ios::binary | std::ios::trunc);
        current_log << "previous native session marker";
    }

    const auto proxy = LoadLibraryW(arguments[3]);
    if (proxy == nullptr)
    {
        std::cerr << "Could not load the Insider native proxy." << std::endl;
        return 1;
    }

    constexpr DWORD retry_delay_ms = 10;
    constexpr DWORD timeout_ms = 5'000;
    DWORD state = 0;
    for (DWORD elapsed = 0; elapsed < timeout_ms; elapsed += retry_delay_ms)
    {
        if (fixture_failure() != 0)
        {
            std::cerr << "The fake Mono runtime rejected bootstrap call " << fixture_failure() << "." << std::endl;
            return 1;
        }

        state = fixture_state();
        if ((state & fixture_complete) == fixture_complete)
        {
            break;
        }

        Sleep(retry_delay_ms);
    }

    if ((state & fixture_complete) != fixture_complete)
    {
        std::cerr << "Bootstrap sequence timed out at state " << state << "." << std::endl;
        return 1;
    }

    if (scenario == L"delayed-domain" && fixture_root_requests() < root_domain_delay + 1)
    {
        std::cerr << "The bootstrap did not retry while the root domain was unavailable." << std::endl;
        return 1;
    }

    const auto* assembly_path = fixture_assembly_path();
    if (assembly_path == nullptr ||
        !EndsWithIgnoringCase(assembly_path, "Insider\\core\\Insider.Bootstrap.dll"))
    {
        std::cerr << "Unexpected managed assembly path: "
                  << (assembly_path == nullptr ? "<null>" : assembly_path)
                  << std::endl;
        return 1;
    }

    std::vector<wchar_t> environment_path(32'768);
    const auto environment_length = GetEnvironmentVariableW(
        L"INSIDER_PROCESS_PATH",
        environment_path.data(),
        static_cast<DWORD>(environment_path.size()));
    const auto process_path = GetProcessPath();
    if (environment_length == 0 ||
        environment_length >= environment_path.size() ||
        process_path != std::wstring(environment_path.data(), environment_length))
    {
        std::cerr << "INSIDER_PROCESS_PATH was not initialized correctly." << std::endl;
        return 1;
    }

    const auto expected_log = scenario == L"managed-exception"
        ? "The managed bootstrap returned an unhandled exception."
        : "Managed bootstrap started successfully.";
    if (!WaitForLogMessage(log_path, expected_log))
    {
        std::cerr << "Expected native log message was not written." << std::endl;
        return 1;
    }

    if (!WaitForLogMessage(previous_log_path, "previous native session marker"))
    {
        std::cerr << "The previous native log was not rotated." << std::endl;
        return 1;
    }

    {
        std::ifstream current_log(log_path, std::ios::binary);
        const std::string contents(
            (std::istreambuf_iterator<char>(current_log)),
            std::istreambuf_iterator<char>());
        if (contents.find("previous native session marker") != std::string::npos)
        {
            std::cerr << "The current native log still contains the previous session." << std::endl;
            return 1;
        }
    }

    std::cout << "Native bootstrap scenario completed: ";
    std::wcout << scenario << std::endl;
    return 0;
}
