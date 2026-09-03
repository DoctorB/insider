add_library(InsiderBootstrapNative SHARED
  bootstrap.cpp
  version.def)

set_target_properties(InsiderBootstrapNative PROPERTIES
  OUTPUT_NAME "version"
  PREFIX "")

target_compile_features(InsiderBootstrapNative PRIVATE cxx_std_17)
target_compile_definitions(InsiderBootstrapNative PRIVATE
  NOMINMAX
  UNICODE
  _UNICODE
  WIN32_LEAN_AND_MEAN)

if(MSVC)
  target_compile_options(InsiderBootstrapNative PRIVATE /W4 /WX /permissive- /guard:cf)
  target_link_options(InsiderBootstrapNative PRIVATE /DYNAMICBASE /NXCOMPAT /guard:cf)
endif()

if(BUILD_TESTING)
  add_executable(InsiderNativeProxySmoke proxy_smoke.cpp)
  target_compile_features(InsiderNativeProxySmoke PRIVATE cxx_std_17)
  target_compile_definitions(InsiderNativeProxySmoke PRIVATE
    NOMINMAX
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN)

  if(MSVC)
    target_compile_options(InsiderNativeProxySmoke PRIVATE /W4 /WX /permissive-)
  endif()

  add_test(
    NAME InsiderNativeProxySmoke
    COMMAND InsiderNativeProxySmoke $<TARGET_FILE:InsiderBootstrapNative>)

  add_library(InsiderFakeMonoRuntime SHARED tests/fake_mono.cpp)
  set_target_properties(InsiderFakeMonoRuntime PROPERTIES
    OUTPUT_NAME "mono-2.0-bdwgc"
    PREFIX "")
  target_compile_features(InsiderFakeMonoRuntime PRIVATE cxx_std_17)
  target_include_directories(InsiderFakeMonoRuntime PRIVATE tests)

  add_executable(InsiderNativeBootstrapFixture tests/bootstrap_fixture.cpp)
  target_compile_features(InsiderNativeBootstrapFixture PRIVATE cxx_std_17)
  target_compile_definitions(InsiderNativeBootstrapFixture PRIVATE
    NOMINMAX
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN)
  target_include_directories(InsiderNativeBootstrapFixture PRIVATE tests)

  add_library(InsiderFakeGameAssembly SHARED tests/fake_game_assembly.cpp)
  set_target_properties(InsiderFakeGameAssembly PROPERTIES
    OUTPUT_NAME "GameAssembly"
    PREFIX "")
  target_compile_features(InsiderFakeGameAssembly PRIVATE cxx_std_17)

  add_library(InsiderFakeHostFxr SHARED tests/fake_hostfxr.cpp)
  set_target_properties(InsiderFakeHostFxr PROPERTIES
    OUTPUT_NAME "hostfxr_fixture"
    PREFIX "")
  target_compile_features(InsiderFakeHostFxr PRIVATE cxx_std_17)

  add_executable(InsiderNativeIl2CppBootstrapFixture tests/il2cpp_bootstrap_fixture.cpp)
  target_compile_features(InsiderNativeIl2CppBootstrapFixture PRIVATE cxx_std_17)
  target_compile_definitions(InsiderNativeIl2CppBootstrapFixture PRIVATE
    NOMINMAX
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN)

  add_custom_command(TARGET InsiderFakeHostFxr POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory
      "$<TARGET_FILE_DIR:InsiderBootstrapNative>/Insider/runtime/win-x64"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
      "$<TARGET_FILE:InsiderFakeHostFxr>"
      "$<TARGET_FILE_DIR:InsiderBootstrapNative>/Insider/runtime/win-x64/hostfxr.dll")

  if(MSVC)
    target_compile_options(InsiderFakeMonoRuntime PRIVATE /W4 /WX /permissive-)
    target_compile_options(InsiderNativeBootstrapFixture PRIVATE /W4 /WX /permissive-)
    target_compile_options(InsiderFakeGameAssembly PRIVATE /W4 /WX /permissive-)
    target_compile_options(InsiderFakeHostFxr PRIVATE /W4 /WX /permissive-)
    target_compile_options(InsiderNativeIl2CppBootstrapFixture PRIVATE /W4 /WX /permissive-)
  endif()

  add_test(
    NAME InsiderNativeBootstrapFixture.Success
    COMMAND InsiderNativeBootstrapFixture
      success
      $<TARGET_FILE:InsiderFakeMonoRuntime>
      $<TARGET_FILE:InsiderBootstrapNative>)

  add_test(
    NAME InsiderNativeBootstrapFixture.DelayedDomain
    COMMAND InsiderNativeBootstrapFixture
      delayed-domain
      $<TARGET_FILE:InsiderFakeMonoRuntime>
      $<TARGET_FILE:InsiderBootstrapNative>)

  add_test(
    NAME InsiderNativeBootstrapFixture.ManagedException
    COMMAND InsiderNativeBootstrapFixture
      managed-exception
      $<TARGET_FILE:InsiderFakeMonoRuntime>
      $<TARGET_FILE:InsiderBootstrapNative>)

  add_test(
    NAME InsiderNativeBootstrapFixture.Il2CppCoreClr
    COMMAND InsiderNativeIl2CppBootstrapFixture
      $<TARGET_FILE:InsiderFakeGameAssembly>
      $<TARGET_FILE:InsiderBootstrapNative>)

  set_tests_properties(
    InsiderNativeProxySmoke
    InsiderNativeBootstrapFixture.Success
    InsiderNativeBootstrapFixture.DelayedDomain
    InsiderNativeBootstrapFixture.ManagedException
    InsiderNativeBootstrapFixture.Il2CppCoreClr
    PROPERTIES
      RESOURCE_LOCK insider_native_log
      TIMEOUT 10)
endif()
