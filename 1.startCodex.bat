@echo off
setlocal

set "SCRIPT_DIR=%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "function Resolve-RepoRoot([string]$StartPath) {" ^
  "  $current=(Resolve-Path -LiteralPath $StartPath).Path;" ^
  "  while ($true) {" ^
  "    if (Test-Path -LiteralPath (Join-Path $current 'UnityProject')) { return $current }" ^
  "    $parent=Split-Path -Parent $current;" ^
  "    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { throw \"Could not find a UnityProject directory when resolving '$StartPath'.\" }" ^
  "    $current=$parent;" ^
  "  }" ^
  "}" ^
  "function Read-UnityPort([string]$SettingsPath) {" ^
  "  if (-not (Test-Path -LiteralPath $SettingsPath)) { return $null }" ^
  "  try {" ^
  "    $json=Get-Content -LiteralPath $SettingsPath -Raw;" ^
  "    if ([string]::IsNullOrWhiteSpace($json)) { return $null }" ^
  "    $settings=$json | ConvertFrom-Json;" ^
  "    if ($null -eq $settings.Port) { return $null }" ^
  "    return [int]$settings.Port;" ^
  "  } catch { return $null }" ^
  "}" ^
  "$repoRoot=Resolve-RepoRoot '%SCRIPT_DIR%';" ^
  "$localSettingsPath=Join-Path $repoRoot 'UnityProject\Packages\com.gamelovers.mcp-unity\McpUnitySettings.local.json';" ^
  "$defaultSettingsPath=Join-Path $repoRoot 'UnityProject\Packages\com.gamelovers.mcp-unity\McpUnitySettings.json';" ^
  "$port=Read-UnityPort $localSettingsPath;" ^
  "if ($null -eq $port) { $port=Read-UnityPort $defaultSettingsPath }" ^
  "if ($null -eq $port) { $port=8090 }" ^
  "Write-Host \"[MCP Unity] Using UNITY_PORT=$port for $repoRoot\";" ^
  "$portConfig='mcp_servers.mcp-unity.env.UNITY_PORT=' + [char]39 + [string]$port + [char]39;" ^
  "& codex --cd $repoRoot -c $portConfig"

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
  echo.
  echo Codex exited with code %EXIT_CODE%.
  pause
)

exit /b %EXIT_CODE%
