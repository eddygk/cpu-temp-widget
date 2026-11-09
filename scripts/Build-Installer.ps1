<#
.SYNOPSIS
  Build the end-user installer: dist\CpuTempWidget-Setup-<version>.exe

.DESCRIPTION
  1. Builds native\RmTemp.dll (needs the AMD Ryzen Master Monitoring SDK installed, for its headers, and
     Visual Studio 2022 Build Tools with the C++ workload). Use -SkipNative to reuse an existing native\bin build.
  2. Runs the unit tests (skip with -SkipTests).
  3. Publishes the app self-contained for win-x64, so end users need no .NET runtime.
  4. Compiles installer\CpuTempWidget.iss with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
  5. Prints the SHA-256 of the installer (put it in the release notes).

  AMD's SDK files are never copied into the installer.

.PARAMETER Version
  Version number, e.g. 1.0.0 (stamped into the exe and the installer).
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [switch]$SkipNative,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$Root    = Split-Path -Parent $PSScriptRoot
$Publish = Join-Path $Root 'publish'
$Dist    = Join-Path $Root 'dist'

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                    -find 'MSBuild\Current\Bin\amd64\MSBuild.exe' | Select-Object -First 1
        if ($path) { return $path }
    }
    throw 'MSBuild with the C++ build tools was not found (Visual Studio 2022 Build Tools, "Desktop development with C++").'
}

function Find-Iscc {
    $candidates = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    if (-not $candidates) { throw 'Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup' }
    $candidates | Select-Object -First 1
}

# 1. native wrapper ----------------------------------------------------------------------------------------------
$nativeDll = Join-Path $Root 'native\bin\x64\Release\RmTemp.dll'
if (-not $SkipNative) {
    Write-Host '== Building native wrapper'
    & (Find-MSBuild) (Join-Path $Root 'native\RmTemp.vcxproj') /p:Configuration=Release /p:Platform=x64 /nologo /v:m /clp:'ErrorsOnly;Summary'
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
}
if (-not (Test-Path $nativeDll)) { throw "Missing $nativeDll (build it, or drop -SkipNative)." }

# 2. tests -------------------------------------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Host '== Running tests'
    dotnet test (Join-Path $Root 'tests\CpuTempWidget.Tests\CpuTempWidget.Tests.csproj') -p:Platform=x64 --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

# 3. publish (self-contained: no .NET needed on the user's PC) ---------------------------------------------------
Write-Host '== Publishing app (self-contained)'
if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }
dotnet publish (Join-Path $Root 'app\CpuTempWidget.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:Version=$Version -o $Publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
foreach ($f in 'CpuTempWidget.exe', 'RmTemp.dll') {
    if (-not (Test-Path (Join-Path $Publish $f))) { throw "Publish output is missing $f" }
}
if (Get-ChildItem $Publish -Recurse -Include 'Platform.dll', 'Device.dll', 'AMDRyzenMaster*') {
    throw 'AMD SDK files ended up in the publish folder; they must not be redistributed.'
}

# 4. installer ---------------------------------------------------------------------------------------------------
Write-Host '== Compiling installer'
& (Find-Iscc) "/DAppVersion=$Version" "/DSourceDir=$Publish" (Join-Path $Root 'installer\CpuTempWidget.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed.' }

# 5. result ------------------------------------------------------------------------------------------------------
$setup = Join-Path $Dist "CpuTempWidget-Setup-$Version.exe"
$hash  = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ''
Write-Host "Installer: $setup"
Write-Host ("Size:      {0:N1} MB" -f ((Get-Item $setup).Length / 1MB))
Write-Host "SHA-256:   $hash"
