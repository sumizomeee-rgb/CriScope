$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Set-LocalEnvironment.ps1')

$sdkVersion = '10.0.401'
$avaloniaTemplateVersion = '12.1.3'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    $bootstrapDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) '.local\bootstrap'
    New-Item -ItemType Directory -Path $bootstrapDirectory -Force | Out-Null
    $installer = Join-Path $bootstrapDirectory 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version $sdkVersion -InstallDir $env:DOTNET_ROOT -NoPath
    if ($LASTEXITCODE -ne 0) { throw ".NET SDK installation failed: $LASTEXITCODE" }
}

$actualVersion = & $dotnet --version
if ($LASTEXITCODE -ne 0 -or $actualVersion -ne $sdkVersion) {
    throw "Expected .NET SDK $sdkVersion, found $actualVersion."
}

$installedTemplates = & $dotnet new uninstall | Out-String
if ($LASTEXITCODE -ne 0) { throw "Unable to inspect installed templates: $LASTEXITCODE" }
if (-not $installedTemplates.Contains('Avalonia.Templates') -or
    -not $installedTemplates.Contains("$avaloniaTemplateVersion")) {
    & $dotnet new install "Avalonia.Templates@$avaloniaTemplateVersion"
    if ($LASTEXITCODE -ne 0) { throw "Avalonia template installation failed: $LASTEXITCODE" }
}

Write-Output "Ready: .NET SDK $actualVersion; Avalonia templates $avaloniaTemplateVersion"
