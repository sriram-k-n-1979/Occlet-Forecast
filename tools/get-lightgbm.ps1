<#
  Downloads LightGBM's official native library (lib_lightgbm.dll) for Windows x64 from the
  LightGBM wheel published on PyPI, verifies its SHA-256, and copies it to native/win-x64/.
  The Approach 2 project copies it next to the .exe at build time. No Python needed.

  Usage (from the repo root, in PowerShell):   ./tools/get-lightgbm.ps1
#>
param([string]$Version = "4.5.0")
$ErrorActionPreference = "Stop"
$root  = Split-Path -Parent $PSScriptRoot
$meta  = Invoke-RestMethod "https://pypi.org/pypi/lightgbm/$Version/json"
$wheel = $meta.urls | Where-Object { $_.filename -like "*win_amd64.whl" } | Select-Object -First 1
if (-not $wheel) { throw "No win_amd64 wheel found for LightGBM $Version" }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("lgbm-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Force $tmp | Out-Null
$zip = Join-Path $tmp "lightgbm.zip"
Write-Host "Downloading $($wheel.filename) ..."
Invoke-WebRequest $wheel.url -OutFile $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
if ($hash -ne $wheel.digests.sha256) { throw "SHA-256 mismatch: got $hash, PyPI says $($wheel.digests.sha256)" }

Expand-Archive $zip -DestinationPath (Join-Path $tmp "x")
$dll  = Get-ChildItem (Join-Path $tmp "x") -Recurse -Filter lib_lightgbm.dll | Select-Object -First 1
$dest = Join-Path $root "native\win-x64"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $dll.FullName $dest -Force
Remove-Item $tmp -Recurse -Force
Write-Host "Installed lib_lightgbm.dll (LightGBM $Version) -> $dest"
Write-Host "Now run:  dotnet build ; dotnet run --project src/Occlet.Approach2"
