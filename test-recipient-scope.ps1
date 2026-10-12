$ErrorActionPreference='Stop'
$recipientCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$recipientOutput=Join-Path $PSScriptRoot 'bin\recipient-scope-tests'
New-Item -ItemType Directory -Force -Path $recipientOutput | Out-Null
$recipientExe=Join-Path $recipientOutput 'RecipientRequirementTests.exe'
& $recipientCompiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/out:'+$recipientExe) (Join-Path $PSScriptRoot 'src\RecipientRequirement.cs') (Join-Path $PSScriptRoot 'tests\RecipientRequirementTests.cs')
if($LASTEXITCODE -ne 0){throw 'Recipient scope compile failed.'}
& $recipientExe
if($LASTEXITCODE -ne 0){throw 'Recipient scope tests failed.'}
