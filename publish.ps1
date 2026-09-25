<#
.SYNOPSIS
  Builds the mod in Release and uploads the workshop/ workspace to Steam Workshop with MegaCrit's ModUploader.

.DESCRIPTION
  1. dotnet build -c Release (no copy into the game's mods folder)
  2. Copies StS2-UnDoFloor.dll + StS2-UnDoFloor.json into workshop\content\
  3. Runs ModUploader.exe upload -w workshop
  The first upload creates the item and writes workshop\mod_id.txt; commit that file so later runs update the same item.
  Steam must be running and logged in to the account that owns the item.

.PARAMETER UploaderDir
  Folder containing ModUploader.exe (https://github.com/megacrit/sts2-mod-uploader/releases).
  Defaults to $env:STS2_MOD_UPLOADER, then C:\workspace\sts2-mod-uploader.

.PARAMETER ChangeNote
  Overrides "changeNote" in workshop\workshop.json for this upload only.

.PARAMETER NoUpload
  Build and stage the content, but do not run the uploader.
#>
param(
    [string]$UploaderDir = $(if ($env:STS2_MOD_UPLOADER) { $env:STS2_MOD_UPLOADER } else { 'C:\workspace\sts2-mod-uploader' }),
    [string]$ChangeNote,
    [switch]$NoUpload
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspace = Join-Path $root 'workshop'
$content = Join-Path $workspace 'content'

Write-Host '== Building Release' -ForegroundColor Cyan
dotnet build (Join-Path $root 'StS2-UnDoFloor.csproj') -c Release -p:CopyToModsFolder=false
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host '== Staging workshop\content' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $content | Out-Null
# Wipe everything (dotfiles included): Steam stores an all-zero hash for empty files, which
# breaks clients that SHA-1-verify downloads, so nothing but the mod itself may be uploaded.
Get-ChildItem $content -Force | Remove-Item -Force -Recurse
# Ask MSBuild where the DLL went: the output folder differs between Godot.NET.Sdk (.godot/mono/temp/bin) and the plain SDK (bin/).
$dll = (dotnet msbuild (Join-Path $root 'StS2-UnDoFloor.csproj') -nologo -getProperty:TargetPath -p:Configuration=Release).Trim()
if (-not (Test-Path $dll)) { throw "Built DLL not found at '$dll'" }
Copy-Item $dll $content
Copy-Item (Join-Path $root 'StS2-UnDoFloor.json') $content
Get-ChildItem $content | Format-Table Name, Length -AutoSize

$manifest = Get-Content (Join-Path $root 'StS2-UnDoFloor.json') -Raw | ConvertFrom-Json
Write-Host "Mod version in manifest: $($manifest.version)"

if ($ChangeNote) {
    $wsPath = Join-Path $workspace 'workshop.json'
    $ws = Get-Content $wsPath -Raw | ConvertFrom-Json
    $ws.changeNote = $ChangeNote
    $ws | ConvertTo-Json -Depth 5 | Set-Content $wsPath -Encoding utf8
    Write-Host "changeNote set to: $ChangeNote"
}

if ($NoUpload) { Write-Host 'NoUpload set; skipping ModUploader.'; exit 0 }

$exe = Join-Path $UploaderDir 'ModUploader.exe'
if (-not (Test-Path $exe)) { throw "ModUploader.exe not found at $exe. Download it from https://github.com/megacrit/sts2-mod-uploader/releases or pass -UploaderDir." }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam is not running; start it and log in first.' }

Write-Host "== Uploading workspace $workspace" -ForegroundColor Cyan
Push-Location $UploaderDir
try {
    & $exe upload -w $workspace
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($code -ne 0) { throw "ModUploader exited with $code; see $UploaderDir\mod-uploader.log" }
$idFile = Join-Path $workspace 'mod_id.txt'
if (Test-Path $idFile) {
    $id = (Get-Content $idFile -Raw).Trim()
    Write-Host "Done. Workshop item: https://steamcommunity.com/sharedfiles/filedetails/?id=$id" -ForegroundColor Green
}
