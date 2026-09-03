#include <windows.h>

#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

namespace
{
    bool WaitForLogMessage(const std::filesystem::path& path, const std::string& expected_message)
    {
        constexpr DWORD retry_delay_ms = 10;
        constexpr DWORD timeout_ms = 5'000;
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
    std::filesystem::path game_assembly_path;
    std::filesystem::path proxy_path;
    if (argument_count == 1)
    {
        const auto fixture_path = std::filesystem::path(arguments[0]);
        game_assembly_path = fixture_path.parent_path() / L"GameAssembly.dll";
        proxy_path = fixture_path.parent_path() / L"version.dll";
    }
    else if (argument_count == 3)
    {
        game_assembly_path = arguments[1];
        proxy_path = arguments[2];
    }
    else
    {
        std::cerr << "Expected no arguments, or fake GameAssembly and native proxy paths." << std::endl;
        return 2;
    }

    if (LoadLibraryW(game_assembly_path.c_str()) == nullptr)
    {
        std::cerr << "Could not load the fake GameAssembly runtime." << std::endl;
        return 1;
    }

    const auto log_path = proxy_path.parent_path() / L"Insider" / L"logs" / L"native.log";
    std::error_code file_error;
    std::filesystem::create_directories(log_path.parent_path(), file_error);
    std::filesystem::remove(log_path, file_error);

    if (LoadLibraryW(proxy_path.c_str()) == nullptr)
    {
        std::cerr << "Could not load the Insider native proxy." << std::endl;
        return 1;
    }

    if (!WaitForLogMessage(
            log_path,
            "Managed IL2CPP bootstrap started successfully through the private CoreCLR runtime."))
    {
        std::cerr << "The IL2CPP bootstrap success message was not written." << std::endl;
        return 1;
    }

    std::cout << "Native IL2CPP bootstrap scenario completed." << std::endl;
    return 0;
}
