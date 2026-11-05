# Bouwt NexusPen.exe en de tests met de C#-compiler die in Windows zit (.NET Framework 4.8).
# Gebruik: powershell -ExecutionPolicy Bypass -File build\build.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
$out = Join-Path $root "out"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$src = Get-ChildItem (Join-Path $root "src") -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
$res = Get-ChildItem (Join-Path $root "assets\fonts") -Filter *.ttf | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
$refs = "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll"

Write-Host "NexusPen.exe bouwen..."
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /warn:4 `
    "/out:$out\NexusPen.exe" "/win32icon:$root\assets\NexusPen.ico" "/win32manifest:$root\assets\NexusPen.manifest" `
    @refs @res @src
if ($LASTEXITCODE -ne 0) { throw "Bouwen van NexusPen.exe mislukt" }

Write-Host "Tests bouwen..."
$tests = Get-ChildItem (Join-Path $root "tests") -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:exe /platform:anycpu /main:NexusPen.Tests "/out:$out\NexusPen.Tests.exe" @refs @res @src @tests
if ($LASTEXITCODE -ne 0) { throw "Bouwen van de tests mislukt" }

Write-Host "Tests draaien..."
& "$out\NexusPen.Tests.exe"
if ($LASTEXITCODE -ne 0) { throw "Er zijn tests mislukt" }
Write-Host "Klaar: $out\NexusPen.exe"
