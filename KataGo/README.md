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
      native-opencl/
        katago_bridge.dll
        analysis_example.cfg
      native-eigen/
        katago_bridge.dll
        analysis_nowrite.cfg
  models/
    kata1-b18c384nbt-s9996604416-d4316597426.bin.gz
    b18c384nbt-humanv0.bin.gz
  configs/
    analysis.cfg
```

When the game root is writable, the Unity adapter tries OpenCL first and falls back to CPU if OpenCL is unavailable. The `exe` backend uses `opencl` then `eigenavx2`; the `native` backend uses `native-opencl` then `native-eigen`. When the game root is not writable, OpenCL is skipped because it needs to write tuning cache files, and the CPU engine uses `analysis_nowrite.cfg`.

When the selected backend is `native`, Unity loads `katago_bridge.dll` through P/Invoke from the selected native candidate directory. The bridge uses the same analysis JSON request and response contract as `katago.exe analysis`. A single bridge engine instance can accept multiple in-flight `kg_analyze` calls when the DLL exports `kg_supports_concurrent_analyze`; the bridge dispatches final output by request id. Do not create multiple native bridge engine instances in one Unity process because the bridge still redirects KataGo analysis through process-wide standard streams.

Human SL is configured as a companion model. `game-config.json` sets the normal analysis model with `katago.model.fileName` and the Human SL model with `katago.model.humanSlFileName`. The exe backend starts KataGo with both `-model` and `-human-model` when the Human SL file exists. The native backend uses `kg_create_engine_with_human_model`, so runtime DLLs/SOs must be rebuilt after bridge source changes.

For Windows player builds, the `native` backend copies the configured native candidate directories, both configured models, and `game-config.json`; it does not copy the OpenCL/Eigen `katago.exe` engine folders. If CPU fallback is enabled, `native-eigen` is required and `native-opencl` is optional at build time. The `exe` backend keeps the full KataGo runtime copy behavior.

For the first smoke test, start KataGo with:

```text
katago.exe analysis -config analysis_example.cfg -model ../../../models/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz -human-model ../../../models/b18c384nbt-humanv0.bin.gz
```

KataGo may create `analysis_logs` under the selected engine directory at runtime. These logs are generated diagnostics and should not be committed.
The OpenCL engine may create `KataGoData/opencltuning`; these tuning cache files are also generated local state and should not be committed.
Windows player packaging excludes runtime-generated or non-Windows directories and files from the copied KataGo runtime: `analysis_logs`, `KataGoData`, `android-opencl-tuning`, `Library`, `Temp`, `weiqixn_bridge_resolved_config.cfg`, and Unity `.meta` files.
