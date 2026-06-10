param(
    [string]$ApkPath = "",
    [string]$AdbPath = "C:\Users\78447\AppData\Local\Android\Sdk\platform-tools\adb.exe",
    [string]$PackageName = "com.DefaultCompany.WeiqiXN",
    [string]$LaunchActivity = "com.unity3d.player.UnityPlayerActivity"
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "..\..\..\.."))
$repoApkPath = Join-Path $repoRoot "Build\Android\WeiqiXN.apk"
$legacyApkPath = "F:\WorkSpace\WeiqiXN\Build\Android\WeiqiXN.apk"

if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    if (Test-Path -LiteralPath $repoApkPath -PathType Leaf) {
        $ApkPath = $repoApkPath
    } elseif (Test-Path -LiteralPath $legacyApkPath -PathType Leaf) {
        $ApkPath = $legacyApkPath
    } else {
        $ApkPath = $repoApkPath
    }
}

if (-not (Test-Path -LiteralPath $ApkPath -PathType Leaf)) {
    [Console]::Error.WriteLine("APK not found. Checked current repo output '$repoApkPath' and legacy output '$legacyApkPath'. Ask the user whether to build it with Unity menu '自定义功能/打包/Build Android APK'.")
    exit 2
}

Write-Host "Using APK: $ApkPath"

if (-not (Test-Path -LiteralPath $AdbPath -PathType Leaf)) {
    [Console]::Error.WriteLine("ADB not found: $AdbPath")
    exit 2
}

$deviceOutput = & $AdbPath devices
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("adb devices failed with exit code $LASTEXITCODE.")
    exit $LASTEXITCODE
}

$readyDevices = New-Object System.Collections.Generic.List[string]
$notReadyDevices = New-Object System.Collections.Generic.List[string]

foreach ($line in $deviceOutput) {
    $trimmed = $line.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -eq "List of devices attached") {
        continue
    }

    $parts = $trimmed -split "\s+"
    if ($parts.Length -lt 2) {
        continue
    }

    $serial = $parts[0]
    $state = $parts[1]
    if ($state -eq "device") {
        $readyDevices.Add($serial)
    } else {
        $notReadyDevices.Add("$serial`t$state")
    }
}

if ($notReadyDevices.Count -gt 0) {
    Write-Host "Non-ready Android devices:"
    foreach ($device in $notReadyDevices) {
        Write-Host "  $device"
    }
}

if ($readyDevices.Count -eq 0) {
    [Console]::Error.WriteLine("No connected Android devices in 'device' state.")
    exit 3
}

$failed = New-Object System.Collections.Generic.List[string]
$launchFailed = New-Object System.Collections.Generic.List[string]
foreach ($serial in $readyDevices) {
    Write-Host "Installing APK to $serial ..."
    & $AdbPath -s $serial install -r -d $ApkPath
    if ($LASTEXITCODE -ne 0) {
        $failed.Add($serial)
        Write-Host "Install failed on $serial with exit code $LASTEXITCODE."
        continue
    } else {
        Write-Host "Install succeeded on $serial."
    }

    Write-Host "Launching $PackageName on $serial ..."
    & $AdbPath -s $serial shell am start -n "$PackageName/$LaunchActivity"
    if ($LASTEXITCODE -ne 0) {
        $launchFailed.Add($serial)
        Write-Host "Launch failed on $serial with exit code $LASTEXITCODE."
    } else {
        Write-Host "Launch succeeded on $serial."
    }
}

if ($failed.Count -gt 0) {
    [Console]::Error.WriteLine("APK install failed on: " + [string]::Join(", ", $failed))
    exit 4
}

if ($launchFailed.Count -gt 0) {
    [Console]::Error.WriteLine("APK launch failed on: " + [string]::Join(", ", $launchFailed))
    exit 5
}

Write-Host "APK installed and launched successfully on $($readyDevices.Count) device(s): $([string]::Join(', ', $readyDevices))"
