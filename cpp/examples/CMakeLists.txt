cmake_minimum_required(VERSION 3.20)

add_executable(simple_client simple_client.cpp)
target_link_libraries(simple_client rocket-welder::rocket-welder)