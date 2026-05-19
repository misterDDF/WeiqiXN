# KataGo Local Editor Setup

This directory is for local Unity Editor validation of KataGo. The engine binaries and model are intentionally kept in git so a fresh clone can run without downloading KataGo again. It is not the final client packaging layout.

Expected layout:

```text
ExternalTools/KataGo/
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

Use `opencl` first for GPU-capable machines and fall back to `eigenavx2` when OpenCL startup fails.

For the first smoke test, start KataGo with:

```text
katago.exe analysis -config analysis_example.cfg -model ../../../models/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz
```

The Unity adapter should eventually use absolute paths derived from the project root instead of depending on the current working directory.
