<#
.SYNOPSIS
    Publishes RibbonXmlEditor as a single self-contained win-x64 exe (no .NET install needed on the target).
.DESCRIPTION
    Output: .\publish\win-x64\RibbonXmlEditor.exe
    Optionally signs the exe with the ACCO code-signing certificate when Sign-File.bat is present.
.PARAMETER Sign
    Sign the published exe with C:\Visual Studio Files\Sectigo\Sign-File.bat (requires the SafeNet token).
#>
[CmdletBinding()]
param(
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'RibbonXmlEditor\RibbonXmlEditor.csproj'
$publishDir = Join-Path $root 'publish\win-x64'

Write-Host "Publishing $project ..." -ForegroundColor Cyan
dotnet publish $project -p:PublishProfile=SelfContained-win-x64 --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $publishDir 'RibbonXmlEditor.exe'
if (-not (Test-Path $exe)) { throw "Expected output not found: $exe" }

if ($Sign) {
    $signBat = 'C:\Visual Studio Files\Sectigo\Sign-File.bat'
    if (Test-Path $signBat) {
        Write-Host "Signing $exe ..." -ForegroundColor Cyan
        & cmd.exe /c "`"$signBat`" `"$exe`""
        if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }
    }
    else {
        Write-Warning "Sign-File.bat not found at $signBat; exe left unsigned."
    }
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
