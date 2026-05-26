param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$KataGoSourceDir = 'F:\WorkSpace\KataGo\cpp',
    [string]$EigenDir = 'F:\WorkSpace\KataGoDeps\eigen-5.0.1',
    [string]$ZlibSourceDir = 'F:\WorkSpace\KataGoDeps\zlib-1.3.2',
    [string]$Abi = 'arm64-v8a',
    [string]$Backend = 'EIGEN',
    [int]$ApiLevel = 23,
    [string]$NdkRoot = $env:ANDROID_NDK_HOME,
    [string]$CMakeExe = $env:CMAKE_EXE,
    [switch]$SkipUnityCopy
)

$ErrorActionPreference = 'Stop'

function Resolve-FirstExistingPath {
    param([string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Resolve-CMakeExe {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and (Test-Path -LiteralPath $ExplicitPath)) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $cmd = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmd -ne $null) {
        return $cmd.Source
    }

    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    )

    $resolved = Resolve-FirstExistingPath $candidates
    if ($resolved -eq $null) {
        throw 'cmake.exe not found. Set CMAKE_EXE or pass -CMakeExe.'
    }

    return $resolved
}

function Resolve-NdkRoot {
    param([string]$ExplicitPath)

    $candidates = @($ExplicitPath, $env:ANDROID_NDK_ROOT, $env:ANDROID_NDK_HOME)
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) {
        $candidates += Join-Path $env:ANDROID_SDK_ROOT 'ndk'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
        $candidates += Join-Path $env:ANDROID_HOME 'ndk'
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        $toolchain = Join-Path $candidate 'build\cmake\android.toolchain.cmake'
        if (Test-Path -LiteralPath $toolchain) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }

        $versioned = Get-ChildItem -LiteralPath $candidate -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'build\cmake\android.toolchain.cmake') } |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($versioned -ne $null) {
            return $versioned.FullName
        }
    }

    $unityNdk = Get-ChildItem -LiteralPath 'C:\Program Files\Unity\Hub\Editor' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'Editor\Data\PlaybackEngines\AndroidPlayer\NDK' } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'build\cmake\android.toolchain.cmake') } |
        Sort-Object -Descending |
        Select-Object -First 1
    if ($unityNdk -ne $null) {
        return (Resolve-Path -LiteralPath $unityNdk).Path
    }

    $runningUnityNdk = Get-Process Unity -ErrorAction SilentlyContinue |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.Path) } |
        ForEach-Object {
            $editorRoot = Split-Path -Parent $_.Path
            Join-Path $editorRoot 'Data\PlaybackEngines\AndroidPlayer\NDK'
        } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'build\cmake\android.toolchain.cmake') } |
        Sort-Object -Unique |
        Select-Object -First 1
    if ($runningUnityNdk -ne $null) {
        return (Resolve-Path -LiteralPath $runningUnityNdk).Path
    }

    throw 'Android NDK not found. Install Unity Android Build Support with NDK, set ANDROID_NDK_HOME, or pass -NdkRoot.'
}

function Resolve-NinjaExe {
    $cmd = Get-Command ninja -ErrorAction SilentlyContinue
    if ($cmd -ne $null) {
        return $cmd.Source
    }

    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    )

    $resolved = Resolve-FirstExistingPath $candidates
    if ($resolved -eq $null) {
        throw 'ninja.exe not found. Install Visual Studio CMake tools or add Ninja to PATH.'
    }

    return $resolved
}

$bridgeRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$repoRootPath = Resolve-Path -LiteralPath $RepoRoot
$buildDir = Join-Path $bridgeRoot "build-android-$Abi"
$toolchainPath = Join-Path (Resolve-NdkRoot $NdkRoot) 'build\cmake\android.toolchain.cmake'
$cmakePath = Resolve-CMakeExe $CMakeExe
$ninjaPath = Resolve-NinjaExe

$configureArgs = @(
    '-S', $bridgeRoot,
    '-B', $buildDir,
    '-G', 'Ninja',
    "-DCMAKE_MAKE_PROGRAM=$ninjaPath",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchainPath",
    "-DANDROID_ABI=$Abi",
    "-DANDROID_PLATFORM=android-$ApiLevel",
    '-DCMAKE_BUILD_TYPE=Release',
    "-DKATAGO_SOURCE_DIR=$KataGoSourceDir",
    "-DEIGEN3_INCLUDE_DIRS=$EigenDir",
    "-DZLIB_SOURCE_DIR=$ZlibSourceDir",
    "-DKATAGO_BRIDGE_BACKEND=$Backend"
)

& $cmakePath @configureArgs

& $cmakePath --build $buildDir --config Release --target katago_bridge

$sourceSo = Join-Path $buildDir 'out\libkatago_bridge.so'
if (-not (Test-Path -LiteralPath $sourceSo)) {
    throw "Android bridge output not found: $sourceSo"
}

if (-not $SkipUnityCopy) {
    $targetDir = Join-Path $repoRootPath "UnityProject\Assets\Plugins\Android\libs\$Abi"
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Copy-Item -LiteralPath $sourceSo -Destination (Join-Path $targetDir 'libkatago_bridge.so') -Force
    Write-Host "Copied Android bridge to Unity plugin path: $targetDir"
}

Write-Host "Android bridge build complete: $sourceSo"
