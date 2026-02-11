$ErrorActionPreference = "Stop"

$csc = "C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe"
if (-not (Test-Path $csc)) {
  $csc = "C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe"
}
if (-not (Test-Path $csc)) {
  throw "csc.exe not found: $csc"
}

$outDir = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$outExe = Join-Path $outDir "ClashGuardian.exe"
$icon = Join-Path $PSScriptRoot "assets\\ClashGuardian.ico"
if (-not (Test-Path $icon)) {
  throw "icon not found: $icon"
}

& $csc /nologo /target:winexe /win32icon:$icon /out:$outExe `
  @(Get-ChildItem -Path $PSScriptRoot -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })

if ($LASTEXITCODE -ne 0) {
  throw "Build failed (csc exit code: $LASTEXITCODE)"
}

Write-Host "Built: $outExe"
