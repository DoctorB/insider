#include "fixture_protocol.h"

#include <windows.h>

#include <algorithm>
#include <cctype>
#include <cstring>
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
}

int wmain(int argument_count, wchar_t** arguments)
{
    if (argument_count != 3)
    {
        std::cerr << "Expected the fake Mono DLL and native proxy paths." << std::endl;
        return 2;
    }

    const auto fake_mono = LoadLibraryW(arguments[1]);
    if (fake_mono == nullptr)
    {
        std::cerr << "Could not load the fake Mono runtime." << std::endl;
        return 1;
    }

    FixtureState fixture_state = nullptr;
    FixtureFailure fixture_failure = nullptr;
    FixtureAssemblyPath fixture_assembly_path = nullptr;
    if (!ResolveExport(fake_mono, "insider_fixture_state", fixture_state) ||
        !ResolveExport(fake_mono, "insider_fixture_failure", fixture_failure) ||
        !ResolveExport(fake_mono, "insider_fixture_assembly_path", fixture_assembly_path))
    {
        std::cerr << "The fake Mono runtime is missing fixture exports." << std::endl;
        return 1;
    }

    const auto proxy = LoadLibraryW(arguments[2]);
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

    std::cout << "Native bootstrap completed the expected Mono embedding sequence." << std::endl;
    return 0;
}
