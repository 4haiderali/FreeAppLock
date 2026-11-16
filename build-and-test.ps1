param([switch]$SkipNativeInput)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Invoke-Dotnet @('build', (Join-Path $root 'FreeAppLock.csproj'), '-c', 'Release', '--nologo')
Invoke-Dotnet @('run', '--project', (Join-Path $root 'Tests\FreeAppLock.CoreTests.csproj'), '-c', 'Release')
& node --test (Join-Path $root 'Tests\extension.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Extension regressions failed.' }
Invoke-Dotnet @('build', (Join-Path $root 'NativeHost\FreeAppLock.NativeHost.csproj'), '-c', 'Release', '--nologo')
Invoke-Dotnet @('build', (Join-Path $root 'Tests\WindowTarget\WindowTarget.csproj'), '-c', 'Release', '--nologo')
$settings = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FreeAppLock\settings.json'
$before = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { 'absent' }
try {
    $uiArgs=@('run', '--project', (Join-Path $root 'Tests\Ui\UiTests.csproj'), '-c', 'Release', '--', (Join-Path $root 'Tests\WindowTarget\bin\Release\net8.0-windows\AppLockTestTarget.exe'), (Join-Path $root 'App.xaml'))
    if($SkipNativeInput){$uiArgs+='--skip-native-input';Write-Output 'SKIP: native input injection assertions explicitly disabled for this run.'}
    Invoke-Dotnet $uiArgs
}
finally {
    $after = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { 'absent' }
    if ($before -ne $after) { throw 'Real settings changed during integration tests.' }
    Write-Output 'Verified: real settings unchanged.'
}
