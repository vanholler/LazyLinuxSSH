[CmdletBinding()]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $Root "src\LazyLinuxSSH.Desktop\LazyLinuxSSH.Desktop.csproj"
$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
$InstallerScript = Join-Path $PSScriptRoot "LazyLinuxSSH.iss"
$InstallerOutput = Join-Path $Root "artifacts\installer"

if (-not $IsWindows -and $env:OS -ne "Windows_NT") {
    throw "Build the Windows installer on Windows with .NET 8 SDK and Inno Setup 6 or 7."
}

if (-not (Test-Path $Project)) {
    throw "Project not found: $Project"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}

$Compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$CompilerPath = if ($Compiler) {
    $Compiler.Source
}
else {
    @(
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $CompilerPath) {
    throw "Inno Setup 6 or 7 was not found. Install it from https://jrsoftware.org/isinfo.php"
}

Write-Host "[1/4] Cleaning the publish directory..." -ForegroundColor Cyan
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PublishDir, $InstallerOutput | Out-Null

Write-Host "[2/4] Restoring NuGet packages..." -ForegroundColor Cyan
& dotnet restore $Project --runtime win-x64 --nologo
if ($LASTEXITCODE -ne 0) {
    throw @"
NuGet restore failed. This is usually a network, proxy, firewall, or DNS problem.
Check that https://api.nuget.org/v3/index.json opens on this PC, then run this script again.
Do not compile LazyLinuxSSH.iss directly: it needs the files produced by dotnet publish.
"@
}

Write-Host "[3/4] Publishing LazyLinuxSSH $Version for Windows x64..." -ForegroundColor Cyan
& dotnet publish $Project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --nologo `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$Executable = Join-Path $PublishDir "LazyLinuxSSH.Desktop.exe"
if (-not (Test-Path $Executable)) {
    throw "Published executable not found: $Executable"
}

Write-Host "[4/4] Building the installer and uninstaller..." -ForegroundColor Cyan
& $CompilerPath "/DAppVersion=$Version" $InstallerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$Setup = Get-ChildItem $InstallerOutput -Filter "LazyLinuxSSH-Setup-$Version-win-x64.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $Setup) {
    throw "Installer not found in $InstallerOutput"
}

Write-Host ""
Write-Host "Success: $($Setup.FullName)" -ForegroundColor Green
Write-Host "Uninstall: Settings > Apps > Installed apps > LazyLinuxSSH."
Write-Host "User data in %APPDATA%\LazyLinuxSSH is preserved during uninstall."
