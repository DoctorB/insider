#include <windows.h>
#include <winver.h>

#include <array>
#include <cstring>
#include <string>
#include <vector>

namespace
{
    HMODULE g_module = nullptr;
    INIT_ONCE g_system_version_once = INIT_ONCE_STATIC_INIT;
    HMODULE g_system_version = nullptr;

    enum class BootstrapResult
    {
        NotReady,
        Started,
        Failed,
    };

    std::wstring GetModulePath(HMODULE module)
    {
        std::vector<wchar_t> buffer(MAX_PATH);
        for (;;)
        {
            const auto length = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
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

    std::wstring GetDirectoryName(const std::wstring& path)
    {
        const auto separator = path.find_last_of(L"\\/");
        return separator == std::wstring::npos ? std::wstring() : path.substr(0, separator);
    }

    std::wstring Combine(const std::wstring& directory, const wchar_t* relativePath)
    {
        if (directory.empty())
        {
            return relativePath;
        }

        return directory + L"\\" + relativePath;
    }

    std::string ToUtf8(const std::wstring& value)
    {
        if (value.empty())
        {
            return {};
        }

        const auto size = WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr);
        if (size <= 0)
        {
            return {};
        }

        std::string result(static_cast<size_t>(size), '\0');
        WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            size,
            nullptr,
            nullptr);
        return result;
    }

    void WriteLog(const std::wstring& message)
    {
        const auto game_directory = GetDirectoryName(GetModulePath(g_module));
        if (game_directory.empty())
        {
            return;
        }

        const auto insider_directory = Combine(game_directory, L"Insider");
        const auto log_directory = Combine(insider_directory, L"logs");
        CreateDirectoryW(insider_directory.c_str(), nullptr);
        CreateDirectoryW(log_directory.c_str(), nullptr);

        const auto log_path = Combine(log_directory, L"native.log");
        const auto handle = CreateFileW(
            log_path.c_str(),
            FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            return;
        }

        const auto line = ToUtf8(L"[Insider.Native] " + message + L"\r\n");
        DWORD written = 0;
        WriteFile(handle, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
        CloseHandle(handle);
    }

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

    HMODULE FindMonoModule()
    {
        constexpr std::array<const wchar_t*, 3> module_names =
        {
            L"mono-2.0-bdwgc.dll",
            L"mono-2.0-sgen.dll",
            L"mono.dll",
        };

        for (const auto* module_name : module_names)
        {
            if (const auto module = GetModuleHandleW(module_name); module != nullptr)
            {
                return module;
            }
        }

        return nullptr;
    }

    BootstrapResult TryStartManagedBootstrap()
    {
        const auto mono_module = FindMonoModule();
        if (mono_module == nullptr)
        {
            return BootstrapResult::NotReady;
        }

        using MonoGetRootDomain = void* (__cdecl*)();
        using MonoThreadAttach = void* (__cdecl*)(void* domain);
        using MonoDomainAssemblyOpen = void* (__cdecl*)(void* domain, const char* name);
        using MonoAssemblyGetImage = void* (__cdecl*)(void* assembly);
        using MonoClassFromName = void* (__cdecl*)(void* image, const char* name_space, const char* name);
        using MonoClassGetMethodFromName = void* (__cdecl*)(void* klass, const char* name, int parameter_count);
        using MonoRuntimeInvoke = void* (__cdecl*)(void* method, void* instance, void** parameters, void** exception);

        MonoGetRootDomain get_root_domain = nullptr;
        MonoThreadAttach thread_attach = nullptr;
        MonoDomainAssemblyOpen domain_assembly_open = nullptr;
        MonoAssemblyGetImage assembly_get_image = nullptr;
        MonoClassFromName class_from_name = nullptr;
        MonoClassGetMethodFromName class_get_method = nullptr;
        MonoRuntimeInvoke runtime_invoke = nullptr;

        if (!ResolveExport(mono_module, "mono_get_root_domain", get_root_domain) ||
            !ResolveExport(mono_module, "mono_thread_attach", thread_attach) ||
            !ResolveExport(mono_module, "mono_domain_assembly_open", domain_assembly_open) ||
            !ResolveExport(mono_module, "mono_assembly_get_image", assembly_get_image) ||
            !ResolveExport(mono_module, "mono_class_from_name", class_from_name) ||
            !ResolveExport(mono_module, "mono_class_get_method_from_name", class_get_method) ||
            !ResolveExport(mono_module, "mono_runtime_invoke", runtime_invoke))
        {
            WriteLog(L"The loaded Mono runtime does not expose the required embedding API.");
            return BootstrapResult::Failed;
        }

        auto* domain = get_root_domain();
        if (domain == nullptr)
        {
            return BootstrapResult::NotReady;
        }

        if (thread_attach(domain) == nullptr)
        {
            WriteLog(L"Could not attach the bootstrap thread to the Mono root domain.");
            return BootstrapResult::Failed;
        }

        const auto game_directory = GetDirectoryName(GetModulePath(g_module));
        const auto assembly_path = Combine(game_directory, L"Insider\\core\\Insider.Bootstrap.dll");
        const auto assembly_path_utf8 = ToUtf8(assembly_path);
        if (assembly_path_utf8.empty())
        {
            WriteLog(L"Could not encode the managed bootstrap path as UTF-8.");
            return BootstrapResult::Failed;
        }

        auto* assembly = domain_assembly_open(domain, assembly_path_utf8.c_str());
        if (assembly == nullptr)
        {
            WriteLog(L"Could not load managed bootstrap: " + assembly_path);
            return BootstrapResult::Failed;
        }

        auto* image = assembly_get_image(assembly);
        auto* klass = image == nullptr ? nullptr : class_from_name(image, "Insider.Native", "Entrypoint");
        auto* method = klass == nullptr ? nullptr : class_get_method(klass, "Start", 0);
        if (method == nullptr)
        {
            WriteLog(L"Could not resolve Insider.Native.Entrypoint.Start().");
            return BootstrapResult::Failed;
        }

        void* exception = nullptr;
        runtime_invoke(method, nullptr, nullptr, &exception);
        if (exception != nullptr)
        {
            WriteLog(L"The managed bootstrap returned an unhandled exception.");
            return BootstrapResult::Failed;
        }

        WriteLog(L"Managed bootstrap started successfully.");
        return BootstrapResult::Started;
    }

    DWORD WINAPI BootstrapThread(void*)
    {
        const auto process_path = GetModulePath(nullptr);
        if (!process_path.empty())
        {
            SetEnvironmentVariableW(L"INSIDER_PROCESS_PATH", process_path.c_str());
        }

        WriteLog(L"Waiting for the Unity Mono runtime.");
        constexpr DWORD retry_delay_ms = 50;
        constexpr DWORD timeout_ms = 60'000;
        for (DWORD elapsed = 0; elapsed < timeout_ms; elapsed += retry_delay_ms)
        {
            const auto result = TryStartManagedBootstrap();
            if (result != BootstrapResult::NotReady)
            {
                return result == BootstrapResult::Started ? 0 : 1;
            }

            Sleep(retry_delay_ms);
        }

        WriteLog(L"Timed out waiting for the Unity Mono runtime.");
        return 1;
    }

    BOOL CALLBACK LoadSystemVersionModule(PINIT_ONCE, PVOID, PVOID*)
    {
        std::vector<wchar_t> buffer(MAX_PATH);
        const auto length = GetSystemDirectoryW(buffer.data(), static_cast<UINT>(buffer.size()));
        if (length == 0 || static_cast<size_t>(length) >= buffer.size())
        {
            return FALSE;
        }

        const auto path = std::wstring(buffer.data(), length) + L"\\version.dll";
        g_system_version = LoadLibraryW(path.c_str());
        return g_system_version != nullptr;
    }

    HMODULE GetSystemVersionModule()
    {
        InitOnceExecuteOnce(&g_system_version_once, LoadSystemVersionModule, nullptr, nullptr);
        return g_system_version;
    }
}

extern "C" DWORD WINAPI InsiderVerInstallFileA(
    DWORD flags,
    LPCSTR source_file_name,
    LPCSTR destination_file_name,
    LPCSTR source_directory,
    LPCSTR destination_directory,
    LPCSTR current_directory,
    LPSTR temporary_file,
    PUINT temporary_file_length)
{
    using Function = decltype(&::VerInstallFileA);
    Function function = nullptr;
    const auto module = GetSystemVersionModule();
    if (module == nullptr || !ResolveExport(module, "VerInstallFileA", function))
    {
        SetLastError(module == nullptr ? ERROR_MOD_NOT_FOUND : ERROR_PROC_NOT_FOUND);
        return VIF_CANNOTREADSRC;
    }

    return function(
        flags,
        source_file_name,
        destination_file_name,
        source_directory,
        destination_directory,
        current_directory,
        temporary_file,
        temporary_file_length);
}

extern "C" DWORD WINAPI InsiderVerInstallFileW(
    DWORD flags,
    LPCWSTR source_file_name,
    LPCWSTR destination_file_name,
    LPCWSTR source_directory,
    LPCWSTR destination_directory,
    LPCWSTR current_directory,
    LPWSTR temporary_file,
    PUINT temporary_file_length)
{
    using Function = decltype(&::VerInstallFileW);
    Function function = nullptr;
    const auto module = GetSystemVersionModule();
    if (module == nullptr || !ResolveExport(module, "VerInstallFileW", function))
    {
        SetLastError(module == nullptr ? ERROR_MOD_NOT_FOUND : ERROR_PROC_NOT_FOUND);
        return VIF_CANNOTREADSRC;
    }

    return function(
        flags,
        source_file_name,
        destination_file_name,
        source_directory,
        destination_directory,
        current_directory,
        temporary_file,
        temporary_file_length);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH)
    {
        return TRUE;
    }

    g_module = instance;
    DisableThreadLibraryCalls(instance);
    const auto thread = CreateThread(nullptr, 0, BootstrapThread, nullptr, 0, nullptr);
    if (thread != nullptr)
    {
        CloseHandle(thread);
    }

    return TRUE;
}
