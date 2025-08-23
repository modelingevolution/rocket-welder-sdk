from setuptools import setup, find_packages
import os
import shutil

# Always copy README.md from parent directory if it exists
if os.path.exists("../README.md"):
    shutil.copy2("../README.md", "README.md")

# Read README.md
readme_path = "README.md"
if os.path.exists(readme_path):
    with open(readme_path, "r", encoding="utf-8") as fh:
        long_description = fh.read()
else:
    long_description = "Client library for RocketWelder video streaming services"

setup(
    name="rocket-welder-sdk",
    version="1.0.0",
    author="ModelingEvolution",
    description="High-performance video streaming client library for RocketWelder services",
    long_description=long_description,
    long_description_content_type="text/markdown",
    url="https://github.com/modelingevolution/rocket-welder-sdk",
    packages=find_packages(),
    classifiers=[
        "Development Status :: 3 - Alpha",
        "Intended Audience :: Developers",
        "Topic :: Multimedia :: Video",
        "License :: OSI Approved :: MIT License",
        "Programming Language :: Python :: 3",
        "Programming Language :: Python :: 3.8",
        "Programming Language :: Python :: 3.9",
        "Programming Language :: Python :: 3.10",
        "Programming Language :: Python :: 3.11",
        "Programming Language :: Python :: 3.12",
    ],
    python_requires=">=3.8",
    install_requires=[
        "numpy>=1.20.0",
        "opencv-python>=4.5.0",
        "zerobuffer-ipc>=1.1.10",
    ],
    extras_require={
        "dev": [
            "pytest>=7.0",
            "black>=22.0",
            "mypy>=1.0",
        ],
    },
)