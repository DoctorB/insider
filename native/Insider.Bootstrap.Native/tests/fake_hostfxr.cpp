#include <windows.h>

#include <cwchar>

namespace
{
    int g_context = 0;

    bool EndsWith(const wchar_t* value, const wchar_t* suffix)
    {
        if (value == nullptr || suffix == nullptr)
        {
            return false;
        }

        const auto value_length = std::wcslen(value);
        const auto suffix_length = std::wcslen(suffix);
        return value_length >= suffix_length &&
            _wcsicmp(value + value_length - suffix_length, suffix) == 0;
    }
}

extern "C" __declspec(dllexport) int __cdecl hostfxr_initialize_for_dotnet_command_line(
    int argument_count,
    const wchar_t** arguments,
    void*,
    void** context)
{
    if (argument_count != 1 ||
        arguments == nullptr ||
        !EndsWith(arguments[0], L"Insider\\runtime\\win-x64\\Insider.Il2CppHost.dll") ||
        context == nullptr)
    {
        return 23;
    }

    *context = &g_context;
    return 0;
}

extern "C" __declspec(dllexport) int __cdecl hostfxr_run_app(void* context)
{
    return context == &g_context ? 0 : 24;
}

extern "C" __declspec(dllexport) int __cdecl hostfxr_close(void* context)
{
    return context == &g_context ? 0 : 25;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}
