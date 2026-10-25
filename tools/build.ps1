# VoltPulse release build: verifies, publishes self-contained binaries for x64 and ARM64,
# zips the portable build, optionally compiles the Inno Setup installer, and writes SHA-256 hashes.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 [-SkipArm64] [-SkipInstaller]
param(
    [switch]$SkipArm64,
    [switch]$SkipInstaller,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = (Select-Xml -Path "src\VoltPulse.App\VoltPulse.App.csproj" -XPath "//Version" -ErrorAction SilentlyContinue)
$version = if ($version) { $version.Node.InnerText } else { "1.0.0" }
Write-Host "=== VoltPulse build $version ($Configuration) ===" -ForegroundColor Cyan

Write-Host "`n[1/6] Building solution (warnings are failures)..." -ForegroundColor Yellow
dotnet build VoltPulse.sln -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "`n[2/6] Running tests..." -ForegroundColor Yellow
dotnet test VoltPulse.sln -c $Configuration --no-build --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

$publish = @(
    @{ Rid = "win-x64"; Out = "dist\win-x64" }
)
if (-not $SkipArm64) {
    $publish += @{ Rid = "win-arm64"; Out = "dist\win-arm64" }
}

$step = 3
foreach ($target in $publish) {
    Write-Host "`n[$step/6] Publishing self-contained single-file ($($target.Rid))..." -ForegroundColor Yellow
    dotnet publish src\VoltPulse.App\VoltPulse.App.csproj -c $Configuration -r $target.Rid `
        --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -p:DebugTypeEmbedded=false -o $target.Out --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "publish $($target.Rid) failed" }
    $step++
}

Write-Host "`n[5/6] Packaging portable zip..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path dist | Out-Null
$zip = "dist\VoltPulse-$version-portable-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "dist\win-x64\*" -DestinationPath $zip

Write-Host "`n[6/6] Hashes..." -ForegroundColor Yellow
$hashes = Get-ChildItem dist -File | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    "$hash  $($_.Name)"
}
$hashes | Set-Content "dist\SHA256SUMS.txt"
$hashes | Write-Host

if (-not $SkipInstaller) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($iscc) {
        Write-Host "`nBuilding installer with Inno Setup..." -ForegroundColor Yellow
        & $iscc "/DVersion=$version" "installer\VoltPulse.iss"
        if ($LASTEXITCODE -ne 0) { throw "installer build failed" }
        $installer = Get-ChildItem "dist\VoltPulse-*-installer.exe" | Select-Object -First 1
        if ($installer) {
            $hash = (Get-FileHash $installer.FullName -Algorithm SHA256).Hash
            "$hash  $($installer.Name)" | Add-Content "dist\SHA256SUMS.txt"
        }
    }
    else {
        Write-Warning "Inno Setup 6 not found - skipping the installer. Install it from https://jrsoftware.org/isinfo.php and re-run."
    }
}

Write-Host "`nDone. Artifacts in dist\ :" -ForegroundColor Green
Get-ChildItem dist -File | Format-Table Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
