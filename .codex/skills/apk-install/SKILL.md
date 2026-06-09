---
name: apk-install
description: Install the current WeiqiXN Android APK to all connected Android devices. Use when the user asks to install, deploy, push, or test the project APK on connected Android phones, tablets, or emulators; if the APK output is missing, ask whether to build the Android APK first.
---

# APK Install

## Fixed Paths

- Repo root: `F:\WorkSpace\WeiqiXN`
- APK output: `F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk`
- ADB executable: `C:\Users\78447\AppData\Local\Android\Sdk\platform-tools\adb.exe`
- AAPT executable for inspecting the current APK when needed: `C:\Users\78447\AppData\Local\Android\Sdk\build-tools\34.0.0\aapt.exe`
- Android package name: `com.DefaultCompany.WeiqiXN`
- Launchable activity: `com.unity3d.player.UnityPlayerActivity`
- Unity Android build menu: `自定义功能/打包/Build Android APK`
- Unity non-development Android build menu: `自定义功能/打包/Build Android APK (Non-Development)`
- Build config source: `UnityProject/Assets/Scripts/Editor/Build/BuildConfig.cs`, `BuildConfig.BUILD_PATH_ANDROID`

Use these fixed paths first. Do not rediscover the APK path or ADB path unless one of the fixed paths fails.

## Workflow

1. Check whether `F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk` exists.
2. If the APK is missing, ask the user whether to build it. Do not start an Android build without confirmation.
3. If the user confirms building, use Unity MCP to execute `自定义功能/打包/Build Android APK`.
4. After building, confirm the APK exists at the fixed output path.
5. Install to all currently connected Android devices with the bundled script:

```powershell
powershell -ExecutionPolicy Bypass -File .codex\skills\apk-install\scripts\install_apk_all_devices.ps1
```

6. Report which device serials succeeded and which failed.
7. The script launches the app on each device after a successful install. Treat launch failures as task failures unless the user asked to install only.

## Install Behavior

The bundled script:

- Uses the fixed ADB path above.
- Reads devices from `adb devices`.
- Installs only to devices in `device` state.
- Reports `offline`, `unauthorized`, or other non-ready devices without installing to them.
- Runs `adb -s <serial> install -r -d <apk>` for each ready device.
- Starts `com.DefaultCompany.WeiqiXN/com.unity3d.player.UnityPlayerActivity` after each successful install.
- Exits nonzero if no ready devices exist, any install fails, or any post-install launch fails.

## Build Safety

When a build is needed, follow the Unity project's editor-state rules before triggering Unity build/import operations:

- Write and read `UnityProject/Temp/WeiqiXN/editor_state_probe.json` through `自定义功能/Editor/Write Editor State`.
- Continue only when `isPlaying=false`, `isPlayingOrWillChangePlaymode=false`, and `isCompiling=false`.
- If Unity is in Play mode or about to enter Play mode, stop and ask the user to exit Play mode.

## Fallback Commands

If the script cannot run, use the same fixed tools directly:

```powershell
$adb = 'C:\Users\78447\AppData\Local\Android\Sdk\platform-tools\adb.exe'
$apk = 'F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk'
& $adb devices
& $adb -s <serial> install -r -d $apk
& $adb -s <serial> shell am start -n 'com.DefaultCompany.WeiqiXN/com.unity3d.player.UnityPlayerActivity'
```
