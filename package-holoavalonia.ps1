$ErrorActionPreference = 'Stop'

$AppName = 'HoloAvalonia'
$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir 'HoloAvalonia.csproj'
$PublishDir = Join-Path $ProjectDir 'bin/Release/net10.0/publish'
$AppBundle = Join-Path $PublishDir "$AppName.app"
$ContentsDir = Join-Path $AppBundle 'Contents'
$MacOSDir = Join-Path $ContentsDir 'MacOS'
$ResourcesDir = Join-Path $ContentsDir 'Resources'
$IconSource = Join-Path $ProjectDir 'Resources/fire.ico'
$IconsetDir = Join-Path $PublishDir "$AppName.iconset"
$IcnsFile = Join-Path $ResourcesDir "$AppName.icns"
$BundleId = 'com.monkeysoft.holoavalonia'
$InfoPlist = Join-Path $ContentsDir 'Info.plist'

function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing required command: $Name"
    }
}

try {
    Require-Command 'dotnet'
    Require-Command 'rsync'
    Require-Command 'sips'
    Require-Command 'iconutil'
    Require-Command 'plutil'

    if (-not (Test-Path $ProjectFile)) {
        throw "Project file not found: $ProjectFile"
    }

    if (-not (Test-Path $IconSource)) {
        throw "Icon source not found: $IconSource"
    }

    Write-Host '==> Publishing HoloAvalonia'
    & dotnet publish $ProjectFile -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    Write-Host '==> Rebuilding app bundle'
    if (Test-Path $AppBundle) {
        Remove-Item $AppBundle -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $MacOSDir | Out-Null
    New-Item -ItemType Directory -Force -Path $ResourcesDir | Out-Null
    New-Item -ItemType Directory -Force -Path $IconsetDir | Out-Null

    Write-Host '==> Generating iconset'
    & sips -z 16 16 $IconSource --out (Join-Path $IconsetDir 'icon_16x16.png') | Out-Null
    & sips -z 32 32 $IconSource --out (Join-Path $IconsetDir 'icon_16x16@2x.png') | Out-Null
    & sips -z 32 32 $IconSource --out (Join-Path $IconsetDir 'icon_32x32.png') | Out-Null
    & sips -z 64 64 $IconSource --out (Join-Path $IconsetDir 'icon_32x32@2x.png') | Out-Null
    & sips -z 128 128 $IconSource --out (Join-Path $IconsetDir 'icon_128x128.png') | Out-Null
    & sips -z 256 256 $IconSource --out (Join-Path $IconsetDir 'icon_128x128@2x.png') | Out-Null
    & sips -z 256 256 $IconSource --out (Join-Path $IconsetDir 'icon_256x256.png') | Out-Null
    & sips -z 512 512 $IconSource --out (Join-Path $IconsetDir 'icon_256x256@2x.png') | Out-Null
    & sips -z 512 512 $IconSource --out (Join-Path $IconsetDir 'icon_512x512.png') | Out-Null
    & sips -z 1024 1024 $IconSource --out (Join-Path $IconsetDir 'icon_512x512@2x.png') | Out-Null

    & iconutil -c icns $IconsetDir -o $IcnsFile
    if ($LASTEXITCODE -ne 0) { throw "iconutil failed with exit code $LASTEXITCODE" }

    Write-Host '==> Writing Info.plist'
    @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>$AppName</string>
    <key>CFBundleIconFile</key>
    <string>$AppName.icns</string>
    <key>CFBundleIdentifier</key>
    <string>$BundleId</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>$AppName</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@ | Set-Content -Path $InfoPlist -Encoding utf8

    & plutil -lint $InfoPlist | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "plutil failed with exit code $LASTEXITCODE" }

    Write-Host '==> Copying published files into app bundle'
    & rsync -a --delete --exclude "$AppName.app" --exclude "$AppName.iconset" "$PublishDir/" "$MacOSDir/"
    if ($LASTEXITCODE -ne 0) { throw "rsync failed with exit code $LASTEXITCODE" }

    & chmod +x (Join-Path $MacOSDir $AppName)
    if ($LASTEXITCODE -ne 0) { throw "chmod failed with exit code $LASTEXITCODE" }

    $FinalApp = Join-Path $PWD "$AppName.app"

    if (Test-Path $FinalApp) {
        Remove-Item $FinalApp -Recurse -Force
    }

    Copy-Item $AppBundle $FinalApp -Recurse -Force

    Write-Host ''
    Write-Host 'Done. App bundle created at:'
    Write-Host "  $AppBundle"
    Write-Host 'Copied to:'
    Write-Host "  $FinalApp"
}
finally {
    if (Test-Path $IconsetDir) {
        Remove-Item $IconsetDir -Recurse -Force
    }
}
