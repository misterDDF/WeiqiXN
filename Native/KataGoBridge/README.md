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

The current bridge wraps KataGo's analysis command in-process and redirects standard input/output internally. Treat it as one engine instance per process. That single engine instance supports multiple simultaneous `kg_analyze` calls when the bridge exports `kg_supports_concurrent_analyze`; the bridge writes all requests into one analysis input stream and dispatches final JSON output by request id. Do not create multiple bridge engine instances inside the same Unity process because standard stream redirection is process-wide. The Windows Unity native backend tries `native-opencl` first when configured, then falls back to `native-eigen` when CPU fallback is enabled. After the Windows DLL path is validated in Unity, the same CMake entry should be extended for Android `arm64-v8a` `.so` output.

Build the Android `arm64-v8a` eigen `.so` and copy it into Unity's Android plugin path:

```powershell
powershell -ExecutionPolicy Bypass -File Native/KataGoBridge/tools/build_android_bridge.ps1 -Backend EIGEN
```

Build the Android OpenCL ICD loader used for linking the OpenCL bridge:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' `
  -S 'F:\WorkSpace\KataGoDeps\OpenCL-ICD-Loader' `
  -B 'F:\WorkSpace\WeiqiXN\Native\KataGoBridge\build-android-opencl-icd' `
  -G Ninja `
  -DCMAKE_TOOLCHAIN_FILE='F:\Unity 2022.3.45f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK\build\cmake\android.toolchain.cmake' `
  -DANDROID_ABI=arm64-v8a `
  -DANDROID_PLATFORM=android-23 `
  -DCMAKE_BUILD_TYPE=Release `
  -DOPENCL_ICD_LOADER_HEADERS_DIR='F:\WorkSpace\KataGoDeps\OpenCL-Headers' `
  -DENABLE_OPENCL_LAYERS=OFF `
  -DBUILD_TESTING=OFF
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' `
  --build 'F:\WorkSpace\WeiqiXN\Native\KataGoBridge\build-android-opencl-icd' `
  --config Release
```

Build the Android OpenCL `.so` and copy the bridge into Unity's Android plugin path:

```powershell
powershell -ExecutionPolicy Bypass -File Native/KataGoBridge/tools/build_android_bridge.ps1 `
  -Backend OPENCL `
  -OpenClIncludeDir 'F:\WorkSpace\KataGoDeps\OpenCL-Headers' `
  -OpenClLibrary 'F:\WorkSpace\WeiqiXN\Native\KataGoBridge\build-android-opencl-icd\libOpenCL.so'
```

Default output:

```text
Native/KataGoBridge/build-android-arm64-v8a-eigen/out/libkatago_bridge.so
Native/KataGoBridge/build-android-arm64-v8a-opencl/out/libkatago_bridge.so
UnityProject/Assets/Plugins/Android/libs/arm64-v8a/libkatago_bridge_eigen.so
UnityProject/Assets/Plugins/Android/libs/arm64-v8a/libkatago_bridge_opencl.so
```

The script expects Unity Android Build Support or an Android NDK available through `ANDROID_NDK_HOME`, `ANDROID_NDK_ROOT`, `ANDROID_SDK_ROOT`, or `ANDROID_HOME`. Pass `-NdkRoot` or `-CMakeExe` when those tools are installed outside the default locations.
Android Unity runtime loads fixed P/Invoke library names for each candidate: `katago_bridge_opencl` is tried first when configured, and `katago_bridge_eigen` remains the required fallback. The OpenCL bridge links against `libOpenCL.so`, but Android packages should not bundle a same-named OpenCL loader by default; runtime resolution should use the device system's public `libOpenCL.so` when available.

Smoke test the runtime copy:

```powershell
python Native/KataGoBridge/tools/smoke_windows_bridge.py
python Native/KataGoBridge/tools/smoke_windows_bridge.py native-opencl 300000
```
