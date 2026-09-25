$projectRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $projectRoot '.local'
$dotnetRoot = Join-Path $localRoot 'dotnet'

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_CLI_HOME = Join-Path $localRoot 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $localRoot 'nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $localRoot 'nuget\http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $localRoot 'nuget\plugins-cache'
$env:TEMP = Join-Path $localRoot 'tmp'
$env:TMP = $env:TEMP
$env:PATH = $dotnetRoot + [IO.Path]::PathSeparator + $env:PATH

foreach ($directory in @(
    $localRoot,
    $dotnetRoot,
    $env:DOTNET_CLI_HOME,
    $env:NUGET_PACKAGES,
    $env:NUGET_HTTP_CACHE_PATH,
    $env:NUGET_PLUGINS_CACHE_PATH,
    $env:TEMP
)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
