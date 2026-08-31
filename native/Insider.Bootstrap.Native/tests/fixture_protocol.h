#pragma once

#include <windows.h>

constexpr DWORD fixture_root_domain_requested = 1U << 0;
constexpr DWORD fixture_thread_attached = 1U << 1;
constexpr DWORD fixture_assembly_opened = 1U << 2;
constexpr DWORD fixture_class_resolved = 1U << 3;
constexpr DWORD fixture_method_resolved = 1U << 4;
constexpr DWORD fixture_method_invoked = 1U << 5;
constexpr DWORD fixture_complete =
    fixture_root_domain_requested |
    fixture_thread_attached |
    fixture_assembly_opened |
    fixture_class_resolved |
    fixture_method_resolved |
    fixture_method_invoked;

using FixtureState = DWORD (__cdecl*)();
using FixtureFailure = DWORD (__cdecl*)();
using FixtureAssemblyPath = const char* (__cdecl*)();
