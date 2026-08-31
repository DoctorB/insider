#include "fixture_protocol.h"

#include <atomic>
#include <cstring>

namespace
{
    std::atomic<DWORD> g_state = 0;
    std::atomic<DWORD> g_failure = 0;
    std::atomic<DWORD> g_root_domain_delay = 0;
    std::atomic<DWORD> g_root_domain_requests = 0;
    std::atomic<BOOL> g_invoke_exception = FALSE;
    char g_assembly_path[4096] = {};

    int g_domain_token = 0;
    int g_thread_token = 0;
    int g_assembly_token = 0;
    int g_image_token = 0;
    int g_class_token = 0;
    int g_method_token = 0;
    int g_exception_token = 0;

    void Fail(DWORD code)
    {
        DWORD expected = 0;
        g_failure.compare_exchange_strong(expected, code);
    }

    void CompleteStep(DWORD step)
    {
        g_state.fetch_or(step, std::memory_order_release);
    }
}

extern "C" __declspec(dllexport) void* __cdecl mono_get_root_domain()
{
    const auto request = g_root_domain_requests.fetch_add(1, std::memory_order_acq_rel) + 1;
    if (request <= g_root_domain_delay.load(std::memory_order_acquire))
    {
        return nullptr;
    }

    CompleteStep(fixture_root_domain_requested);
    return &g_domain_token;
}

extern "C" __declspec(dllexport) void* __cdecl mono_thread_attach(void* domain)
{
    if (domain != &g_domain_token)
    {
        Fail(1);
        return nullptr;
    }

    CompleteStep(fixture_thread_attached);
    return &g_thread_token;
}

extern "C" __declspec(dllexport) void* __cdecl mono_domain_assembly_open(void* domain, const char* name)
{
    if (domain != &g_domain_token || name == nullptr || name[0] == '\0')
    {
        Fail(2);
        return nullptr;
    }

    if (strncpy_s(g_assembly_path, name, _TRUNCATE) != 0)
    {
        Fail(3);
        return nullptr;
    }

    CompleteStep(fixture_assembly_opened);
    return &g_assembly_token;
}

extern "C" __declspec(dllexport) void* __cdecl mono_assembly_get_image(void* assembly)
{
    if (assembly != &g_assembly_token)
    {
        Fail(4);
        return nullptr;
    }

    return &g_image_token;
}

extern "C" __declspec(dllexport) void* __cdecl mono_class_from_name(
    void* image,
    const char* name_space,
    const char* name)
{
    if (image != &g_image_token ||
        name_space == nullptr ||
        name == nullptr ||
        std::strcmp(name_space, "Insider.Native") != 0 ||
        std::strcmp(name, "Entrypoint") != 0)
    {
        Fail(5);
        return nullptr;
    }

    CompleteStep(fixture_class_resolved);
    return &g_class_token;
}

extern "C" __declspec(dllexport) void* __cdecl mono_class_get_method_from_name(
    void* klass,
    const char* name,
    int parameter_count)
{
    if (klass != &g_class_token ||
        name == nullptr ||
        std::strcmp(name, "Start") != 0 ||
        parameter_count != 0)
    {
        Fail(6);
        return nullptr;
    }

    CompleteStep(fixture_method_resolved);
    return &g_method_token;
}

extern "C" __declspec(dllexport) void* __cdecl mono_runtime_invoke(
    void* method,
    void* instance,
    void** parameters,
    void** exception)
{
    if (method != &g_method_token || instance != nullptr || parameters != nullptr || exception == nullptr)
    {
        Fail(7);
        return nullptr;
    }

    *exception = g_invoke_exception.load(std::memory_order_acquire) == TRUE
        ? &g_exception_token
        : nullptr;
    CompleteStep(fixture_method_invoked);
    return nullptr;
}

extern "C" __declspec(dllexport) DWORD __cdecl insider_fixture_state()
{
    return g_state.load(std::memory_order_acquire);
}

extern "C" __declspec(dllexport) DWORD __cdecl insider_fixture_failure()
{
    return g_failure.load(std::memory_order_acquire);
}

extern "C" __declspec(dllexport) const char* __cdecl insider_fixture_assembly_path()
{
    return g_assembly_path;
}

extern "C" __declspec(dllexport) void __cdecl insider_fixture_configure(
    DWORD root_domain_delay,
    BOOL invoke_exception)
{
    g_state.store(0, std::memory_order_release);
    g_failure.store(0, std::memory_order_release);
    g_root_domain_requests.store(0, std::memory_order_release);
    g_root_domain_delay.store(root_domain_delay, std::memory_order_release);
    g_invoke_exception.store(invoke_exception, std::memory_order_release);
    g_assembly_path[0] = '\0';
}

extern "C" __declspec(dllexport) DWORD __cdecl insider_fixture_root_requests()
{
    return g_root_domain_requests.load(std::memory_order_acquire);
}
