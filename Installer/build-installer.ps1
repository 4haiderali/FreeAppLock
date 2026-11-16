param([string]$Compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $SkipTests) { & (Join-Path $root 'build-and-test.ps1') }
& dotnet publish (Join-Path $root 'FreeAppLock.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root 'publish') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($name in @('README.md','SECURITY.md','RELEASE-NOTES-v1.1.0.md','BROWSER-PROFILES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $root 'publish') }
& dotnet publish (Join-Path $root 'NativeHost\FreeAppLock.NativeHost.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root 'publish-host') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Native host publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'publish-host\FreeAppLock.NativeHost.exe') -Destination (Join-Path $root 'publish')
Copy-Item -LiteralPath (Join-Path $root 'NativeHost\com.freeapplock.profiles.json') -Destination (Join-Path $root 'publish')
$extensionDestination=Join-Path $root 'publish\BrowserExtension'
New-Item -ItemType Directory -Path $extensionDestination -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'BrowserExtension') -File | Copy-Item -Destination $extensionDestination
[xml]$project = Get-Content -LiteralPath (Join-Path $root "FreeAppLock.csproj")
$version = [string]$project.Project.PropertyGroup.Version
& $Compiler "/DAppVersion=$version" (Join-Path $PSScriptRoot 'FreeAppLock.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
