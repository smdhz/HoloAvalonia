$ErrorActionPreference = 'Stop'

$AppName = 'HoloAvalonia'
$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir 'HoloAvalonia.csproj'
$Architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$RuntimeIdentifier = switch ($Architecture) {
    'Arm64' { 'osx-arm64' }
    'X64' { 'osx-x64' }
    default { throw "Unsupported macOS architecture: $Architecture" }
}
$PublishDir = Join-Path $ProjectDir "bin/Release/net10.0/$RuntimeIdentifier/publish"
$AppBundle = Join-Path $PublishDir "$AppName.app"
$ContentsDir = Join-Path $AppBundle 'Contents'
$MacOSDir = Join-Path $ContentsDir 'MacOS'
$ResourcesDir = Join-Path $ContentsDir 'Resources'
$IconSource = Join-Path $ProjectDir 'Resources/fire.ico'
$IcnsFile = Join-Path $ResourcesDir "$AppName.icns"
$IconWorkDir = Join-Path ([System.IO.Path]::GetTempPath()) "$AppName-icon-$([Guid]::NewGuid().ToString('N'))"
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
    Require-Command 'tiffutil'
    Require-Command 'tiff2icns'
    Require-Command 'plutil'
    Require-Command 'codesign'

    if (-not (Test-Path $ProjectFile)) {
        throw "Project file not found: $ProjectFile"
    }

    if (-not (Test-Path $IconSource)) {
        throw "Icon source not found: $IconSource"
    }

    Write-Host "==> Publishing HoloAvalonia ($RuntimeIdentifier, self-contained)"
    & dotnet publish $ProjectFile -c Release -r $RuntimeIdentifier --self-contained true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    Write-Host '==> Rebuilding app bundle'
    if (Test-Path $AppBundle) {
        Remove-Item $AppBundle -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $MacOSDir | Out-Null
    New-Item -ItemType Directory -Force -Path $ResourcesDir | Out-Null

    Write-Host '==> Generating app icon'
    New-Item -ItemType Directory -Force -Path $IconWorkDir | Out-Null

    $IconTiffFiles = foreach ($Size in 16, 32, 48, 128, 256, 512, 1024) {
        $TiffFile = Join-Path $IconWorkDir "icon-$Size.tiff"
        & sips -s format tiff -z $Size $Size $IconSource --out $TiffFile | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "sips failed to generate ${Size}x${Size} icon with exit code $LASTEXITCODE"
        }
        $TiffFile
    }

    $MultiSizeTiff = Join-Path $IconWorkDir "$AppName.tiff"
    & tiffutil -catnosizecheck @IconTiffFiles -out $MultiSizeTiff | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "tiffutil failed with exit code $LASTEXITCODE" }

    & tiff2icns $MultiSizeTiff $IcnsFile
    if ($LASTEXITCODE -ne 0) { throw "tiff2icns failed with exit code $LASTEXITCODE" }

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
    & rsync -a --delete --exclude "$AppName.app" "$PublishDir/" "$MacOSDir/"
    if ($LASTEXITCODE -ne 0) { throw "rsync failed with exit code $LASTEXITCODE" }

    & chmod +x (Join-Path $MacOSDir $AppName)
    if ($LASTEXITCODE -ne 0) { throw "chmod failed with exit code $LASTEXITCODE" }

    Write-Host '==> Signing app bundle'
    & codesign --force --deep --sign - $AppBundle
    if ($LASTEXITCODE -ne 0) { throw "codesign failed with exit code $LASTEXITCODE" }

    & codesign --verify --deep --strict $AppBundle
    if ($LASTEXITCODE -ne 0) { throw "codesign verification failed with exit code $LASTEXITCODE" }

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
    if (Test-Path $IconWorkDir) {
        Remove-Item $IconWorkDir -Recurse -Force
    }
}
