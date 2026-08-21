<#
.SYNOPSIS
    Builds BandPilot into a single self-contained Windows executable.

.DESCRIPTION
    Runs the layout tests first, then publishes. The tests are fast and guard
    the native struct definitions, which fail silently rather than loudly when
    they are wrong, so a failure here stops the build.

.PARAMETER SkipTests
    Publish without running the verification pass.

.EXAMPLE
    .\build.ps1
#>

[CmdletBinding()]
param(
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ''
Write-Host 'BandPilot build' -ForegroundColor Cyan
Write-Host '===============' -ForegroundColor Cyan
Write-Host ''

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'The .NET SDK was not found on PATH.' -ForegroundColor Red
    Write-Host 'Install .NET 8 from https://dotnet.microsoft.com/download/dotnet/8.0'
    exit 1
}

if (-not $SkipTests) {
    Write-Host 'Running layout and math checks...' -ForegroundColor Yellow
    Push-Location (Join-Path $root 'tests/LayoutTests')
    try {
        dotnet run -c Release
        if ($LASTEXITCODE -ne 0) {
            Write-Host ''
            Write-Host 'Verification failed. Not publishing.' -ForegroundColor Red
            exit 1
        }
    }
    finally { Pop-Location }
}

Write-Host ''
Write-Host 'Publishing...' -ForegroundColor Yellow

$dist = Join-Path $root 'dist'

dotnet publish (Join-Path $root 'src/BandPilot.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $dist

if ($LASTEXITCODE -ne 0) {
    Write-Host 'Publish failed.' -ForegroundColor Red
    exit 1
}

$exe = Join-Path $dist 'BandPilot.exe'
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Built: $exe  ($size MB)" -ForegroundColor Green
    Write-Host 'Right-click the executable and choose "Run as administrator".' -ForegroundColor DarkGray
    Write-Host ''
}
