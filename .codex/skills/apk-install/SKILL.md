---
name: apk-install
description: Install the current WeiqiXN Android APK to all connected Android devices. Use when the user asks to install, deploy, push, or test the project APK on connected Android phones, tablets, or emulators; if the APK output is missing, ask whether to build the Android APK first.
---

# APK Install

## Path Rules

- Preferred repo root: the current project that contains this skill, resolved from `.codex/skills/apk-install/scripts/` back to the repository root.
- Preferred APK output: `<current repo root>\Build\Android\WeiqiXN.apk`.
- Legacy fallback repo root: `F:\WorkSpace\WeiqiXN`.
- Legacy fallback APK output: `F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk`.
- ADB executable: `C:\Users\78447\AppData\Local\Android\Sdk\platform-tools\adb.exe`.
- AAPT executable for inspecting the current APK when needed: `C:\Users\78447\AppData\Local\Android\Sdk\build-tools\34.0.0\aapt.exe`.
- Android package name: `com.DefaultCompany.WeiqiXN`.
- Launchable activity: `com.unity3d.player.UnityPlayerActivity`.
- Unity Android build menu: `自定义功能/打包/Build Android APK`.
- Unity non-development Android build menu: `自定义功能/打包/Build Android APK (Non-Development)`.
- Build config source: `UnityProject/Assets/Scripts/Editor/Build/BuildConfig.cs`, `BuildConfig.BUILD_PATH_ANDROID`.

Use the current repo relative APK output first. Use the legacy fixed APK output only when the current repo output is missing. Do not rediscover the ADB path unless the fixed ADB path fails.

## Workflow

1. Check whether `<current repo root>\Build\Android\WeiqiXN.apk` exists.
2. If it is missing, check whether `F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk` exists as a legacy fallback.
3. If both APK outputs are missing, ask the user whether to build it. Do not start an Android build without confirmation.
4. If the user confirms building, use Unity MCP to execute `自定义功能/打包/Build Android APK`.
5. After building, confirm the APK exists at the current repo relative output first.
6. Install to all currently connected Android devices with the bundled script:

```powershell
powershell -ExecutionPolicy Bypass -File .codex\skills\apk-install\scripts\install_apk_all_devices.ps1
```

7. Report which device serials succeeded and which failed.
8. The script launches the app on each device after a successful install. Treat launch failures as task failures unless the user asked to install only.

## Install Behavior

The bundled script:

- Resolves the APK path in this order:
  - explicit `-ApkPath` argument, if provided;
  - `<current repo root>\Build\Android\WeiqiXN.apk`;
  - `F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk`.
- Uses the fixed ADB path above.
- Reads devices from `adb devices`.
- Installs only to devices in `device` state.
- Reports `offline`, `unauthorized`, or other non-ready devices without installing to them.
- Runs `adb -s <serial> install -r -d <apk>` for each ready device.
- Starts `com.DefaultCompany.WeiqiXN/com.unity3d.player.UnityPlayerActivity` after each successful install.
- Exits nonzero if no APK exists, no ready devices exist, any install fails, or any post-install launch fails.

## Build Safety

When a build is needed, follow the Unity project's editor-state rules before triggering Unity build/import operations:

- Write and read `UnityProject/Temp/WeiqiXN/editor_state_probe.json` through `自定义功能/Editor/Write Editor State`.
- Continue only when `isPlaying=false`, `isPlayingOrWillChangePlaymode=false`, and `isCompiling=false`.
- If Unity is in Play mode or about to enter Play mode, stop and ask the user to exit Play mode.

## Fallback Commands

If the script cannot run, use the same fixed tools directly and prefer the current repo relative APK:

```powershell
$adb = 'C:\Users\78447\AppData\Local\Android\Sdk\platform-tools\adb.exe'
$apk = Join-Path (Get-Location) 'Build\Android\WeiqiXN.apk'
if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) {
    $apk = 'F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk'
}
& $adb devices
& $adb -s <serial> install -r -d $apk
& $adb -s <serial> shell am start -n 'com.DefaultCompany.WeiqiXN/com.unity3d.player.UnityPlayerActivity'
```
