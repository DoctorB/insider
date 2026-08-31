cmake_minimum_required(VERSION 3.24)

project(InsiderNative LANGUAGES CXX)

include(CTest)

if(NOT WIN32)
  message(FATAL_ERROR "The first Insider native bootstrap target is Windows x64.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "The first Insider native bootstrap target is x64.")
endif()

add_subdirectory(Insider.Bootstrap.Native)
