cmake_minimum_required(VERSION 3.20)

# Find Google Test
find_package(GTest CONFIG REQUIRED)

# Test executable
add_executable(rocket-welder-tests
    test_connection_string.cpp
    test_gst_caps.cpp
    main.cpp
)

target_link_libraries(rocket-welder-tests
    rocket-welder::rocket-welder
    GTest::gtest
    GTest::gtest_main
)

# Add tests to CTest
include(GoogleTest)
gtest_discover_tests(rocket-welder-tests)