# KataGo Bridge

This directory contains the local native bridge used by Unity when `game-config.json` selects the Windows `native` KataGo backend.

Current Windows build inputs:

- KataGo source: `F:/WorkSpace/KataGo/cpp`
- Eigen headers: `F:/WorkSpace/KataGoDeps/eigen-5.0.1`
- zlib source: `F:/WorkSpace/KataGoDeps/zlib-1.3.2`

Configure the CPU bridge with the Visual Studio generator:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' `
  -S 'F:\WorkSpace\WeiqiXN-2\Native\KataGoBridge' `
  -B 'F:\WorkSpace\WeiqiXN-2\Native\KataGoBridge\build-win-x64' `
  -G 'Visual Studio 17 2022' -A x64 `
  -DKATAGO_SOURCE_DIR='F:/WorkSpace/KataGo/cpp' `
  -DEIGEN3_INCLUDE_DIRS='F:/WorkSpace/KataGoDeps/eigen-5.0.1' `
  -DZLIB_SOURCE_DIR='F:/WorkSpace/KataGoDeps/zlib-1.3.2' `
  -DKATAGO_BRIDGE_BACKEND=EIGEN
```

Configure the OpenCL bridge with a separate build directory:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' `
  -S 'F:\WorkSpace\WeiqiXN-2\Native\KataGoBridge' `
  -B 'F:\WorkSpace\WeiqiXN-2\Native\KataGoBridge\build-win-x64-opencl' `
  -G 'Visual Studio 17 2022' -A x64 `
  -DKATAGO_SOURCE_DIR='F:/WorkSpace/KataGo/cpp' `
  -DEIGEN3_INCLUDE_DIRS='F:/WorkSpace/KataGoDeps/eigen-5.0.1' `
  -DZLIB_SOURCE_DIR='F:/WorkSpace/KataGoDeps/zlib-1.3.2' `
  -DKATAGO_BRIDGE_BACKEND=OPENCL
```

Build the DLL:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' `
  --build 'F:\WorkSpace\WeiqiXN-2\Native\KataGoBridge\build-win-x64' `
  --config Release --target katago_bridge
```

Output:

```text
Native/KataGoBridge/build-win-x64/out/Release/katago_bridge.dll
```

Runtime copy target:

```text
KataGo/engines/win-x64/native-eigen/katago_bridge.dll
KataGo/engines/win-x64/native-opencl/katago_bridge.dll
```

The current bridge wraps KataGo's analysis command in-process and redirects standard input/output internally. Treat it as one engine instance per process. The Windows Unity native backend tries `native-opencl` first when configured, then falls back to `native-eigen` when CPU fallback is enabled. After the Windows DLL path is validated in Unity, the same CMake entry should be extended for Android `arm64-v8a` `.so` output.

Smoke test the runtime copy:

```powershell
python Native/KataGoBridge/tools/smoke_windows_bridge.py
python Native/KataGoBridge/tools/smoke_windows_bridge.py native-opencl 300000
```
