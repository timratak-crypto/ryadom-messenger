$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path "$PSScriptRoot\build" -Force | Out-Null
& $csc /nologo /target:exe /optimize+ /codepage:65001 "/out:$PSScriptRoot\build\NoiseTests.exe" "$PSScriptRoot\NoiseFilter.cs" "$PSScriptRoot\VoiceAudio.cs" "$PSScriptRoot\NoiseTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Noise tests build failed' }
& "$PSScriptRoot\build\NoiseTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Noise tests failed' }
