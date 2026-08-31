#include <windows.h>

#include <array>
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
}

int wmain(int argument_count, wchar_t** arguments)
{
    if (argument_count != 2)
    {
        std::cerr << "Expected the proxy DLL path." << std::endl;
        return 2;
    }

    const auto proxy = LoadLibraryW(arguments[1]);
    if (proxy == nullptr)
    {
        std::cerr << "Could not load the proxy DLL." << std::endl;
        return 1;
    }

    constexpr std::array<const char*, 17> expected_exports =
    {
        "GetFileVersionInfoA",
        "GetFileVersionInfoByHandle",
        "GetFileVersionInfoExA",
        "GetFileVersionInfoExW",
        "GetFileVersionInfoSizeA",
        "GetFileVersionInfoSizeExA",
        "GetFileVersionInfoSizeExW",
        "GetFileVersionInfoSizeW",
        "GetFileVersionInfoW",
        "VerFindFileA",
        "VerFindFileW",
        "VerInstallFileA",
        "VerInstallFileW",
        "VerLanguageNameA",
        "VerLanguageNameW",
        "VerQueryValueA",
        "VerQueryValueW",
    };

    for (const auto* name : expected_exports)
    {
        if (GetProcAddress(proxy, name) == nullptr)
        {
            std::cerr << "Missing proxy export: " << name << std::endl;
            return 1;
        }
    }

    using GetFileVersionInfoSize = DWORD (WINAPI*)(LPCWSTR, LPDWORD);
    GetFileVersionInfoSize get_version_size = nullptr;
    if (!ResolveExport(proxy, "GetFileVersionInfoSizeW", get_version_size))
    {
        return 1;
    }

    std::vector<wchar_t> system_directory(MAX_PATH);
    const auto system_length = GetSystemDirectoryW(
        system_directory.data(),
        static_cast<UINT>(system_directory.size()));
    if (system_length == 0 || static_cast<size_t>(system_length) >= system_directory.size())
    {
        std::cerr << "Could not resolve the Windows system directory." << std::endl;
        return 1;
    }

    const auto versioned_file = std::wstring(system_directory.data(), system_length) + L"\\kernel32.dll";
    DWORD ignored_handle = 0;
    if (get_version_size(versioned_file.c_str(), &ignored_handle) == 0)
    {
        std::cerr << "The forwarded version API call failed." << std::endl;
        return 1;
    }

    std::cout << "All version.dll exports resolved and forwarding succeeded." << std::endl;
    return 0;
}
