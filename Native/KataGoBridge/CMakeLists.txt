cmake_minimum_required(VERSION 3.22)

project(WeiqiXNKataGoBridge LANGUAGES C CXX)

set(KATAGO_SOURCE_DIR "F:/WorkSpace/KataGo/cpp" CACHE PATH "Path to the KataGo cpp source directory")
if(NOT EXISTS "${KATAGO_SOURCE_DIR}/CMakeLists.txt")
  message(FATAL_ERROR "KATAGO_SOURCE_DIR must point to KataGo/cpp. Current value: ${KATAGO_SOURCE_DIR}")
endif()

set(EIGEN3_INCLUDE_DIRS "F:/WorkSpace/KataGoDeps/eigen-5.0.1" CACHE PATH "Directory containing Eigen and unsupported subdirectories")
set(ZLIB_SOURCE_DIR "F:/WorkSpace/KataGoDeps/zlib-1.3.2" CACHE PATH "Path to the zlib source directory")
if(NOT EXISTS "${EIGEN3_INCLUDE_DIRS}/Eigen")
  message(FATAL_ERROR "EIGEN3_INCLUDE_DIRS must contain the Eigen directory. Current value: ${EIGEN3_INCLUDE_DIRS}")
endif()
if(NOT EXISTS "${ZLIB_SOURCE_DIR}/zlib.h")
  message(FATAL_ERROR "ZLIB_SOURCE_DIR must contain zlib.h. Current value: ${ZLIB_SOURCE_DIR}")
endif()

add_library(katago_zlib STATIC
  "${ZLIB_SOURCE_DIR}/adler32.c"
  "${ZLIB_SOURCE_DIR}/compress.c"
  "${ZLIB_SOURCE_DIR}/crc32.c"
  "${ZLIB_SOURCE_DIR}/deflate.c"
  "${ZLIB_SOURCE_DIR}/gzclose.c"
  "${ZLIB_SOURCE_DIR}/gzlib.c"
  "${ZLIB_SOURCE_DIR}/gzread.c"
  "${ZLIB_SOURCE_DIR}/gzwrite.c"
  "${ZLIB_SOURCE_DIR}/infback.c"
  "${ZLIB_SOURCE_DIR}/inffast.c"
  "${ZLIB_SOURCE_DIR}/inflate.c"
  "${ZLIB_SOURCE_DIR}/inftrees.c"
  "${ZLIB_SOURCE_DIR}/trees.c"
  "${ZLIB_SOURCE_DIR}/uncompr.c"
  "${ZLIB_SOURCE_DIR}/zutil.c"
)
target_include_directories(katago_zlib PUBLIC "${ZLIB_SOURCE_DIR}")
set_target_properties(katago_zlib PROPERTIES POSITION_INDEPENDENT_CODE ON)

list(PREPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")

set(KATAGO_BRIDGE_BACKEND "EIGEN" CACHE STRING "KataGo bridge neural net backend")
string(TOUPPER "${KATAGO_BRIDGE_BACKEND}" KATAGO_BRIDGE_BACKEND)
set_property(CACHE KATAGO_BRIDGE_BACKEND PROPERTY STRINGS EIGEN OPENCL)
if(NOT KATAGO_BRIDGE_BACKEND STREQUAL "EIGEN" AND NOT KATAGO_BRIDGE_BACKEND STREQUAL "OPENCL")
  message(FATAL_ERROR "KATAGO_BRIDGE_BACKEND must be EIGEN or OPENCL. Current value: ${KATAGO_BRIDGE_BACKEND}")
endif()
set(USE_BACKEND "${KATAGO_BRIDGE_BACKEND}" CACHE STRING "KataGo neural net backend" FORCE)
set(NO_GIT_REVISION 1 CACHE BOOL "Do not embed KataGo git revision" FORCE)
set(BUILD_DISTRIBUTED 0 CACHE BOOL "Do not build distributed training support" FORCE)

find_package(Threads REQUIRED)
add_subdirectory("${KATAGO_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/katago-upstream")
set_target_properties(katago PROPERTIES EXCLUDE_FROM_ALL TRUE)

get_target_property(KATAGO_TARGET_SOURCES katago SOURCES)
if(NOT KATAGO_TARGET_SOURCES)
  message(FATAL_ERROR "Could not read KataGo source list from upstream target.")
endif()

set(KATAGO_BRIDGE_SOURCES)
foreach(source_path IN LISTS KATAGO_TARGET_SOURCES)
  if(source_path MATCHES "(^|[\\/])main\\.cpp$")
    continue()
  endif()

  if(IS_ABSOLUTE "${source_path}")
    list(APPEND KATAGO_BRIDGE_SOURCES "${source_path}")
  else()
    list(APPEND KATAGO_BRIDGE_SOURCES "${KATAGO_SOURCE_DIR}/${source_path}")
  endif()
endforeach()

add_library(katago_bridge SHARED
  src/katago_bridge.cpp
  src/katago_bridge_version.cpp
  ${KATAGO_BRIDGE_SOURCES}
)

target_compile_features(katago_bridge PRIVATE cxx_std_17)
target_compile_definitions(katago_bridge PRIVATE KATAGO_BRIDGE_EXPORTS)
target_include_directories(katago_bridge PRIVATE
  "${KATAGO_SOURCE_DIR}"
  "${KATAGO_SOURCE_DIR}/external"
  "${CMAKE_BINARY_DIR}/katago-upstream"
)

foreach(prop INCLUDE_DIRECTORIES COMPILE_DEFINITIONS COMPILE_OPTIONS LINK_LIBRARIES)
  get_target_property(prop_values katago ${prop})
  if(prop_values)
    if(prop STREQUAL "INCLUDE_DIRECTORIES")
      target_include_directories(katago_bridge PRIVATE ${prop_values})
    elseif(prop STREQUAL "COMPILE_DEFINITIONS")
      target_compile_definitions(katago_bridge PRIVATE ${prop_values})
    elseif(prop STREQUAL "COMPILE_OPTIONS")
      target_compile_options(katago_bridge PRIVATE ${prop_values})
    elseif(prop STREQUAL "LINK_LIBRARIES")
      target_link_libraries(katago_bridge PRIVATE ${prop_values})
    endif()
  endif()
endforeach()

get_directory_property(katago_dir_includes DIRECTORY "${KATAGO_SOURCE_DIR}" INCLUDE_DIRECTORIES)
if(katago_dir_includes)
  target_include_directories(katago_bridge PRIVATE ${katago_dir_includes})
endif()

if(MSVC)
  target_compile_definitions(katago_bridge PRIVATE _CRT_SECURE_NO_WARNINGS NOMINMAX)
endif()

if(ANDROID)
  target_compile_definitions(katago_bridge PRIVATE BYTE_ORDER=1234 LITTLE_ENDIAN=1234 BIG_ENDIAN=4321)
  target_link_libraries(katago_bridge PRIVATE log)
endif()

set_target_properties(katago_bridge PROPERTIES
  OUTPUT_NAME "katago_bridge"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/out"
  LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/out"
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/out"
)
