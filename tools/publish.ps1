$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\CriScope.App\CriScope.App.csproj'
$output = Join-Path $projectRoot 'artifacts\CriScope-win-x64'
$archive = Join-Path $projectRoot 'artifacts\CriScope-win-x64.zip'

& (Join-Path $PSScriptRoot 'dotnet.ps1') publish $project -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=false' -o $output
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码：$LASTEXITCODE" }
if (-not (Test-Path -LiteralPath (Join-Path $output 'CriScope.exe'))) { throw '发布结果缺少 CriScope.exe。' }

# 重复发布时不能把 EXE 产生的本地录制和日志打进公共发行包。
Add-Type -AssemblyName System.IO.Compression
$zip = [IO.Compression.ZipArchive]::new([IO.File]::Open($archive, [IO.FileMode]::Create), [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $output -File -Recurse) {
        $relative = $file.FullName.Substring($output.Length + 1)
        if ($relative -match '(^|[\\/])\.local([\\/]|$)' -or $file.Extension -in @('.criscope','.log')) { continue }
        $entry = $zip.CreateEntry(('CriScope-win-x64/' + $relative.Replace('\', '/')), [IO.Compression.CompressionLevel]::Optimal)
        $source = [IO.File]::OpenRead($file.FullName)
        $destination = $entry.Open()
        try { $source.CopyTo($destination) }
        finally { $destination.Dispose(); $source.Dispose() }
    }
}
finally { $zip.Dispose() }

Write-Output "发布成功：$(Join-Path $output 'CriScope.exe')"
Write-Output "自包含 Windows x64 压缩包：$archive"
Write-Output '请分发完整 ZIP；运行无需安装 .NET。压缩包已排除 .local 与 .criscope 录制文件。'
