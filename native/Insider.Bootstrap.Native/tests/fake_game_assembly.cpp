#include <windows.h>

#include <cstddef>
#include <cstring>

namespace
{
    int g_domain = 0;
    int g_assembly = 0;
    int g_image = 0;
    int g_class = 0;
    void* g_assemblies[] = { &g_assembly };
    volatile int g_score = 7;

    int __cdecl GetScore(void*, void*)
    {
        return g_score;
    }

    struct FakeMethodInfo
    {
        void* method_pointer;
    };

    FakeMethodInfo g_method = { reinterpret_cast<void*>(&GetScore) };
}

extern "C" __declspec(dllexport) void* __cdecl il2cpp_domain_get()
{
    return &g_domain;
}

extern "C" __declspec(dllexport) void** __cdecl il2cpp_domain_get_assemblies(
    void* domain,
    std::size_t* size)
{
    if (domain != &g_domain || size == nullptr)
    {
        return nullptr;
    }

    *size = 1;
    return g_assemblies;
}

extern "C" __declspec(dllexport) void* __cdecl il2cpp_assembly_get_image(void* assembly)
{
    return assembly == &g_assembly ? &g_image : nullptr;
}

extern "C" __declspec(dllexport) const char* __cdecl il2cpp_image_get_name(void* image)
{
    return image == &g_image ? "Assembly-CSharp.dll" : nullptr;
}

extern "C" __declspec(dllexport) void* __cdecl il2cpp_class_from_name(
    void* image,
    const char* name_space,
    const char* name)
{
    return image == &g_image &&
        name_space != nullptr &&
        std::strcmp(name_space, "Insider.Fixture") == 0 &&
        name != nullptr &&
        std::strcmp(name, "Score") == 0
        ? &g_class
        : nullptr;
}

extern "C" __declspec(dllexport) void* __cdecl il2cpp_class_get_method_from_name(
    void* klass,
    const char* name,
    int parameter_count)
{
    return klass == &g_class &&
        name != nullptr &&
        std::strcmp(name, "GetValue") == 0 &&
        parameter_count == 0
        ? &g_method
        : nullptr;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}
