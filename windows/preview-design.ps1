$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath "$PSScriptRoot\build\Ryadom.exe")) { & "$PSScriptRoot\build.ps1" }
& "$framework\csc.exe" /nologo /target:exe /codepage:65001 "/out:$PSScriptRoot\build\DesignPreview.exe" "/reference:$PSScriptRoot\build\Ryadom.exe" "/reference:$framework\WPF\PresentationFramework.dll" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\WindowsBase.dll" /reference:System.Xaml.dll "$PSScriptRoot\DesignPreview.cs"
if ($LASTEXITCODE -ne 0) { throw 'Design preview build failed' }
& "$PSScriptRoot\build\DesignPreview.exe" "$PSScriptRoot\build\design-preview"
if ($LASTEXITCODE -ne 0) { throw 'Offscreen design preview failed' }
