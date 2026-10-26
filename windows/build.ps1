$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
$winmd = Get-ChildItem -LiteralPath $sdkRoot -Filter Windows.winmd -Recurse | Where-Object { $_.Directory.Name -match '^10\.' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!$winmd) { throw 'Install the Windows 10 SDK to compile camera support.' }
$facades = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
New-Item -ItemType Directory -Path "$PSScriptRoot\build" -Force | Out-Null
& "$framework\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 "/win32icon:$PSScriptRoot\app.ico" "/out:$PSScriptRoot\build\Ryadom.exe" "/win32manifest:$PSScriptRoot\app.manifest" "/resource:$PSScriptRoot\MainWindow.xaml,Ryadom.MainWindow.xaml" "/reference:$framework\WPF\PresentationFramework.dll" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$($winmd.FullName)" "/reference:$framework\System.Runtime.WindowsRuntime.dll" "/reference:$facades\System.Runtime.dll" "/reference:$facades\System.Runtime.InteropServices.WindowsRuntime.dll" "$PSScriptRoot\App.cs" "$PSScriptRoot\Design.cs" "$PSScriptRoot\Settings.cs" "$PSScriptRoot\NoiseFilter.cs" "$PSScriptRoot\VoiceAudio.cs" "$PSScriptRoot\Calls.cs" "$PSScriptRoot\CallSession.cs" "$PSScriptRoot\Notifications.cs"
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
Write-Output 'Built windows/build/Ryadom.exe'

