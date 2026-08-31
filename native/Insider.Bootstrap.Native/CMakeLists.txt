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
endif()
