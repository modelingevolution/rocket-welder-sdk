cmake_minimum_required(VERSION 3.20)
project(rocket-welder-sdk VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find required packages from vcpkg
find_package(zerobuffer CONFIG REQUIRED)
find_package(OpenCV REQUIRED COMPONENTS core imgproc videoio)
find_package(jsoncpp CONFIG REQUIRED)
find_package(Threads REQUIRED)

# Create header-only library
add_library(rocket-welder INTERFACE)
add_library(rocket-welder::rocket-welder ALIAS rocket-welder)

target_include_directories(rocket-welder INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(rocket-welder INTERFACE
    zerobuffer::zerobuffer
    ${OpenCV_LIBS}
    JsonCpp::JsonCpp
    Threads::Threads
)

target_compile_features(rocket-welder INTERFACE cxx_std_20)

# Build tests if requested
option(BUILD_TESTS "Build tests" ON)
if(BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()

# Build examples if requested
option(BUILD_EXAMPLES "Build examples" ON)
if(BUILD_EXAMPLES)
    add_subdirectory(examples)
endif()

# Installation
include(GNUInstallDirs)
include(CMakePackageConfigHelpers)

install(TARGETS rocket-welder
    EXPORT rocket-welder-targets
)

install(DIRECTORY include/
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}
)

install(EXPORT rocket-welder-targets
    FILE rocket-welder-targets.cmake
    NAMESPACE rocket-welder::
    DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/rocket-welder
)

configure_package_config_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/cmake/rocket-welder-config.cmake.in
    ${CMAKE_CURRENT_BINARY_DIR}/rocket-welder-config.cmake
    INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/rocket-welder
)

write_basic_package_version_file(
    ${CMAKE_CURRENT_BINARY_DIR}/rocket-welder-config-version.cmake
    VERSION ${PROJECT_VERSION}
    COMPATIBILITY SameMajorVersion
)

install(FILES
    ${CMAKE_CURRENT_BINARY_DIR}/rocket-welder-config.cmake
    ${CMAKE_CURRENT_BINARY_DIR}/rocket-welder-config-version.cmake
    DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/rocket-welder
)