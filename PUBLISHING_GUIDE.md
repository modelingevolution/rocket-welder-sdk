# Publishing Guide for Rocket Welder SDK

This guide explains how to publish the Rocket Welder SDK libraries to their respective package registries.

## Prerequisites

### Required Secrets

The following secrets need to be configured in the GitHub repository settings:

1. **VCPKG_REGISTRY_PAT**: Personal Access Token with `repo` scope for pushing to the vcpkg registry repository
2. **NUGET_API_KEY**: API key for publishing to NuGet.org
3. **PYPI_API_TOKEN**: API token for publishing to PyPI
4. **TEST_PYPI_API_TOKEN**: API token for publishing to Test PyPI (optional)

## Dependency Versions

All SDKs depend on ZeroBuffer v1.1.0:
- **C++ SDK**: Uses zerobuffer v1.1.0 from vcpkg custom registry
- **C# SDK**: Uses ZeroBuffer v1.1.0 from NuGet
- **Python SDK**: Uses zerobuffer-ipc v1.1.0 from PyPI

## Publishing Process

### Automatic Publishing (Recommended)

Push a version tag to trigger automatic publishing of all SDKs:

```bash
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0
```

This will:
1. Create a GitHub release
2. Publish C++ SDK to vcpkg registry
3. Publish C# SDK to NuGet
4. Publish Python SDK to PyPI

### Individual SDK Publishing

You can also publish SDKs individually:

#### C++ SDK
```bash
git tag -a cpp-v1.0.0 -m "C++ SDK v1.0.0"
git push origin cpp-v1.0.0
```

#### C# SDK
```bash
git tag -a csharp-v1.0.0 -m "C# SDK v1.0.0"
git push origin csharp-v1.0.0
```

#### Python SDK
```bash
git tag -a python-v1.0.0 -m "Python SDK v1.0.0"
git push origin python-v1.0.0
```

### Manual Publishing via GitHub Actions

You can manually trigger publishing workflows from the Actions tab:

1. Go to the Actions tab in the repository
2. Select the workflow you want to run:
   - "Publish C++ SDK to vcpkg Registry"
   - "Publish C# SDK to NuGet"
   - "Publish Python SDK to PyPI"
   - "Release All SDKs"
3. Click "Run workflow"
4. Enter the version number
5. Click "Run workflow"

## Package Locations

After publishing, the packages will be available at:

### C++ SDK (vcpkg)
- **Registry**: https://github.com/modelingevolution/rocket-welder-sdk-vcpkg-registry
- **Installation**: Configure vcpkg-configuration.json and run `vcpkg install rocket-welder-sdk`

### C# SDK (NuGet)
- **NuGet.org**: https://www.nuget.org/packages/RocketWelder.SDK
- **GitHub Packages**: https://github.com/modelingevolution/rocket-welder-sdk/packages
- **Installation**: `dotnet add package RocketWelder.SDK`

### Python SDK (PyPI)
- **PyPI**: https://pypi.org/project/rocket-welder-sdk/
- **Test PyPI**: https://test.pypi.org/project/rocket-welder-sdk/
- **Installation**: `pip install rocket-welder-sdk`

## Version Management

All SDKs should maintain the same version number for consistency. When releasing:

1. Update version in:
   - `cpp/CMakeLists.txt`
   - `csharp/RocketWelder.SDK.csproj`
   - `python/setup.py`

2. Commit the version changes:
   ```bash
   git add .
   git commit -m "Bump version to 1.0.0"
   ```

3. Create and push the tag:
   ```bash
   git tag -a v1.0.0 -m "Release v1.0.0"
   git push origin main
   git push origin v1.0.0
   ```

## Troubleshooting

### C++ vcpkg Publishing Issues
- Ensure VCPKG_REGISTRY_PAT secret is set with proper permissions
- Verify the tag exists on GitHub before publishing
- Check that the SHA512 calculation completes successfully

### C# NuGet Publishing Issues
- Ensure NUGET_API_KEY is valid and not expired
- Verify the package ID is not already taken on NuGet.org
- Check that all NuGet package dependencies are available

### Python PyPI Publishing Issues
- Ensure PYPI_API_TOKEN is configured correctly
- Verify the package name is available on PyPI
- Check that all pip dependencies are available

## Local Testing

Before publishing, test the packages locally:

### C++ SDK
```bash
cd cpp
./build.sh
```

### C# SDK
```bash
cd csharp
./build.sh
dotnet pack
```

### Python SDK
```bash
cd python
python -m build
twine check dist/*
```