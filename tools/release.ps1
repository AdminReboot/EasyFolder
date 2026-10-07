# Build a single-file EasyFolder.exe and publish it as a GitHub Release.
# Usage: bump <Version> in EasyFolder.csproj, commit and push, then run  .\tools\release.ps1
# The asset must be named EasyFolder.exe and the tag v<Version>: the in-app updater relies on both.
param([string]$Notes = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content "$root\EasyFolder.csproj")).Project.PropertyGroup.Version
$tag = "v$version"
$out = "$root\publish"

dotnet publish "$root\EasyFolder.csproj" -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $out `
    --source https://api.nuget.org/v3/index.json -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = "$out\EasyFolder.exe"
$built = (Get-Item $exe).VersionInfo.ProductVersion
if (-not $built.StartsWith($version)) { throw "built version $built does not match $version" }

if ($Notes) { gh release create $tag $exe --title "Easy Folder $version" --notes $Notes }
else { gh release create $tag $exe --title "Easy Folder $version" --generate-notes }
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
"Released $tag"
