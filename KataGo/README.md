# KataGo Runtime Setup

This directory is the shared KataGo runtime source layout for Windows Unity Editor and Windows PC builds. It intentionally lives outside `UnityProject/Assets` so Unity does not import KataGo `.dll` files as editor plugins.

The editor resolves KataGo from the repository root `KataGo/` directory. The Windows build script copies the selected runtime files to the built player's root directory as `<BuildRoot>/KataGo/` after `BuildPipeline.BuildPlayer` succeeds.

KataGo backend selection is controlled by the repository-root `game-config.json`. `katago.backend.windowsEditor` and `katago.backend.windowsPlayer` accept `exe`, `native`, or `disabled`. The current Windows default is `native`; switch a Windows value to `exe` to use the legacy `katago.exe` process path.

Expected layout:

```text
KataGo/
  engines/
    win-x64/
      opencl/
        katago.exe
        *.dll
        analysis_example.cfg
      eigenavx2/
        katago.exe
        *.dll
        analysis_example.cfg
        analysis_nowrite.cfg
      native-eigen/
        katago_bridge.dll
        analysis_nowrite.cfg
  models/
    kata1-b18c384nbt-s9996604416-d4316597426.bin.gz
  configs/
    analysis.cfg
```

When the game root is writable, the Unity adapter tries the OpenCL engine first and falls back to `eigenavx2` CPU if OpenCL is unavailable. When the game root is not writable, OpenCL is skipped because it needs to write tuning cache files, and the CPU engine uses `analysis_nowrite.cfg`.

When the selected backend is `native`, Unity loads `native-eigen/katago_bridge.dll` through P/Invoke. The bridge uses the same analysis JSON request and response contract as `katago.exe analysis`, but it is currently a single local engine instance and should not be used for multiple simultaneous KataGo sessions.

For Windows player builds, the `native` backend copies only `native-eigen/`, the configured model, and `game-config.json`; it does not copy the OpenCL/Eigen `katago.exe` engine folders. The `exe` backend keeps the full KataGo runtime copy behavior.

For the first smoke test, start KataGo with:

```text
katago.exe analysis -config analysis_example.cfg -model ../../../models/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz
```

KataGo may create `analysis_logs` under the selected engine directory at runtime. These logs are generated diagnostics and should not be committed.
The OpenCL engine may create `KataGoData/opencltuning`; these tuning cache files are also generated local state and should not be committed.
