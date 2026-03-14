param(
    [string]$Version = "v1.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^v\d+\.\d+$') {
    throw "Version must match vX.Y, e.g. v1.0"
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\TranslateTool\TranslateTool.csproj"
$nugetConfig = Join-Path $root "NuGet.Config"
$staging = Join-Path $root ".staging\$Version\$Runtime"
$versionDir = Join-Path $root "versions\$Version"
$zipPath = Join-Path $versionDir "TranslateTool-$Version-$Runtime.zip"

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet_home"
$env:NUGET_CONFIG_FILE = $nugetConfig

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}

if (Test-Path $versionDir) {
    Get-ChildItem $versionDir -Force | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $versionDir | Out-Null
}

dotnet publish $project -c $Configuration -r $Runtime --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false --configfile $nugetConfig -o $staging
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

# Keep a directly runnable app in versions/<version> for local developer usage.
Copy-Item -Path (Join-Path $staging "*") -Destination $versionDir -Recurse -Force

Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force

if (Test-Path (Join-Path $root ".staging")) {
    Remove-Item (Join-Path $root ".staging") -Recurse -Force
}

Write-Host "Package created: $zipPath"
