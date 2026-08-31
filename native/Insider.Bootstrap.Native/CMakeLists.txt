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

  if(MSVC)
    target_compile_options(InsiderFakeMonoRuntime PRIVATE /W4 /WX /permissive-)
    target_compile_options(InsiderNativeBootstrapFixture PRIVATE /W4 /WX /permissive-)
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

  set_tests_properties(
    InsiderNativeProxySmoke
    InsiderNativeBootstrapFixture.Success
    InsiderNativeBootstrapFixture.DelayedDomain
    InsiderNativeBootstrapFixture.ManagedException
    PROPERTIES
      RESOURCE_LOCK insider_native_log
      TIMEOUT 10)
endif()
