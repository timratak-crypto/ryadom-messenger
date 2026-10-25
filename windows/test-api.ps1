$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath "$PSScriptRoot\build\Ryadom.exe")) { & "$PSScriptRoot\build.ps1" }
& "$framework\csc.exe" /nologo /target:exe /codepage:65001 "/out:$PSScriptRoot\build\ApiTests.exe" "/reference:$PSScriptRoot\build\Ryadom.exe" "/reference:$framework\WPF\PresentationFramework.dll" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Net.Http.dll "$PSScriptRoot\ApiTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'API regression test build failed' }
& "$PSScriptRoot\build\ApiTests.exe" "$PSScriptRoot\tests\messages-response.json"
if ($LASTEXITCODE -ne 0) { throw 'API regression tests failed' }
