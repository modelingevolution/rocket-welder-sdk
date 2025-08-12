cmake_minimum_required(VERSION 3.20)
project(rocket_welder_sdk VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find dependencies
find_package(OpenCV REQUIRED)
find_package(Threads REQUIRED)

# Create library
add_library(rocket_welder_sdk
    src/client.cpp
)

target_include_directories(rocket_welder_sdk
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_link_libraries(rocket_welder_sdk
    PUBLIC
        ${OpenCV_LIBS}
        Threads::Threads
)

# Set properties
set_target_properties(rocket_welder_sdk PROPERTIES
    VERSION ${PROJECT_VERSION}
    SOVERSION 1
    PUBLIC_HEADER include/rocket_welder/client.hpp
)

# Installation
install(TARGETS rocket_welder_sdk
    EXPORT rocket_welder_sdk_targets
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
    RUNTIME DESTINATION bin
    PUBLIC_HEADER DESTINATION include/rocket_welder
)

install(DIRECTORY include/rocket_welder
    DESTINATION include
)

# Build examples
option(BUILD_EXAMPLES "Build examples" ON)
if(BUILD_EXAMPLES)
    add_subdirectory(examples)
endif()