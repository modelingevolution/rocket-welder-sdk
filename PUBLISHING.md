# Publishing

## Required Tokens

Set these environment variables before publishing:

```bash
# C++ (vcpkg) - Path to local vcpkg registry
export VCPKG_REPO=/path/to/vcpkg

# C# (NuGet) - Get from https://www.nuget.org/account/apikeys
export NUGET_API_KEY=your_nuget_api_key

# Python (PyPI) - Get from https://pypi.org/manage/account/token/
export PYPI_API_TOKEN=pypi-your_token
```

## Publish All

```bash
./publish.sh
```

## Publish Individual Libraries

```bash
# C++
cd cpp && ./publish.sh

# C#
cd csharp && ./publish.sh

# Python
cd python && ./publish.sh
```

Libraries will be published to:
- **C++**: Local vcpkg registry
- **C#**: NuGet.org
- **Python**: PyPI.org