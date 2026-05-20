# KataGo Runtime Setup

This directory is the shared KataGo runtime layout for Windows Unity Editor and Windows PC builds. Unity copies `Assets/StreamingAssets` into the built player's `<GameName>_Data/StreamingAssets` directory, so editor and packaged player code both resolve KataGo through `Application.streamingAssetsPath/KataGo`.

Expected layout:

```text
Assets/StreamingAssets/KataGo/
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
  models/
    kata1-b18c384nbt-s9996604416-d4316597426.bin.gz
  configs/
    analysis.cfg
```

The current Unity adapter starts the `eigenavx2` engine. The `opencl` engine can stay in this layout for later GPU startup or fallback work, but it is not selected by the current runtime code.

For the first smoke test, start KataGo with:

```text
katago.exe analysis -config analysis_example.cfg -model ../../../models/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz
```

KataGo may create `analysis_logs` under the selected engine directory at runtime. These logs are generated diagnostics and should not be committed.
