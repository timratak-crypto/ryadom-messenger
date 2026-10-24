$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path "$PSScriptRoot\build" -Force | Out-Null
& $csc /nologo /target:exe /codepage:65001 "/out:$PSScriptRoot\build\CallSessionTests.exe" "$PSScriptRoot\CallSession.cs" "$PSScriptRoot\CallSessionTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Call state test build failed' }
& "$PSScriptRoot\build\CallSessionTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Call state tests failed' }
if (!(Test-Path -LiteralPath "$PSScriptRoot\build\Ryadom.exe")) { & "$PSScriptRoot\build.ps1" }
& $csc /nologo /target:exe /codepage:65001 "/reference:$PSScriptRoot\build\Ryadom.exe" "/out:$PSScriptRoot\build\CallTests.exe" "$PSScriptRoot\CallTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Call transport test build failed' }
$node = Get-Command node -ErrorAction Stop
& $node.Source "$PSScriptRoot\..\server-relay\windows-call-fixture.mjs" "$PSScriptRoot\build\CallTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Call transport tests failed' }
