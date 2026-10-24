$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath "$PSScriptRoot\build\Ryadom.exe")) { & "$PSScriptRoot\build.ps1" }
& "$framework\csc.exe" /nologo /target:exe /codepage:65001 "/out:$PSScriptRoot\build\ComposerTests.exe" "/reference:$PSScriptRoot\build\Ryadom.exe" "/reference:$framework\WPF\PresentationFramework.dll" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Net.Http.dll "$PSScriptRoot\ComposerTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Composer regression test build failed' }
& "$PSScriptRoot\build\ComposerTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Composer regression tests failed' }
