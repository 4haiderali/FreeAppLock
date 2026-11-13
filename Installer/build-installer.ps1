param([string]$Compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $root 'build-and-test.ps1')
& dotnet publish (Join-Path $root 'FreeAppLock.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root 'publish') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($name in @('README.md','SECURITY.md','RELEASE-NOTES-v1.0.1.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $root 'publish') }
[xml]$project = Get-Content -LiteralPath (Join-Path $root "FreeAppLock.csproj")
$version = [string]$project.Project.PropertyGroup.Version
& $Compiler "/DAppVersion=$version" (Join-Path $PSScriptRoot 'FreeAppLock.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
