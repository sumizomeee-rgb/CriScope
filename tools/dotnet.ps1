$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Set-LocalEnvironment.ps1')

$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Project-local .NET SDK is missing. Run tools/setup.ps1 first."
}

& $dotnet @args
exit $LASTEXITCODE
