$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Invoke-Dotnet @('build', (Join-Path $root 'FreeAppLock.csproj'), '-c', 'Release', '--nologo')
Invoke-Dotnet @('run', '--project', (Join-Path $root 'Tests\FreeAppLock.CoreTests.csproj'), '-c', 'Release')
Invoke-Dotnet @('build', (Join-Path $root 'Tests\WindowTarget\WindowTarget.csproj'), '-c', 'Release', '--nologo')
$settings = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FreeAppLock\settings.json'
$before = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { 'absent' }
try {
    Invoke-Dotnet @('run', '--project', (Join-Path $root 'Tests\Ui\UiTests.csproj'), '-c', 'Release', '--', (Join-Path $root 'Tests\WindowTarget\bin\Release\net8.0-windows\AppLockTestTarget.exe'), (Join-Path $root 'App.xaml'))
}
finally {
    $after = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { 'absent' }
    if ($before -ne $after) { throw 'Real settings changed during integration tests.' }
    Write-Output 'Verified: real settings unchanged.'
}
