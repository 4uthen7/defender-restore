# DefenderRestore build script (.NET Framework 4.x / csc.exe)
# Usage: powershell -ExecutionPolicy Bypass -File .\build.ps1

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$src  = Join-Path $PSScriptRoot 'src'
$dist = Join-Path $PSScriptRoot 'dist'

if (-not (Test-Path -LiteralPath $src)) {
    throw "Source directory not found: $src"
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$csc = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw '.NET Framework 4.x csc.exe was not found. Install .NET Framework 4.8.'
}

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Management.dll',
    'System.Security.dll'
) | ForEach-Object { '/r:' + $_ }

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/win32manifest:$src\app.manifest",
    "/win32icon:$src\app.ico",
    "/out:$dist\DefenderRestore.exe"
) + $refs + @(
    "$src\Core.cs",
    "$src\MainForm.cs",
    "$src\AssemblyInfo.cs"
)

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\one-click-repair.bat') `
          -Destination (Join-Path $dist 'one-click-repair.bat') -Force

Write-Host "Build OK: $dist\DefenderRestore.exe"
