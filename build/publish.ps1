# Builds, tests and packages the portable Windows x64 distribution.
#   ./build/publish.ps1                 -> dist/XboxControllerCenter-<version>-win-x64.zip + SHA256SUMS.txt
#   ./build/publish.ps1 -SkipTests
# Requires the .NET 10 SDK (dotnet on PATH, or DOTNET_ROOT pointing to it).
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = if ($env:DOTNET_ROOT -and (Test-Path (Join-Path $env:DOTNET_ROOT 'dotnet.exe'))) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { 'dotnet' }

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "XboxControllerCenter-$version-win-x64"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name

if (-not $SkipTests) {
    & $dotnet test (Join-Path $root 'tests/XboxControllerCenter.Core.Tests') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
& $dotnet publish (Join-Path $root 'src/XboxControllerCenter.App/XboxControllerCenter.App.csproj') `
    -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# Only the executable is needed at runtime; drop debug symbols that may sit next to it.
Get-ChildItem $stage -Include *.pdb, *.xml -Recurse | Remove-Item -Force

# Notices and documentation.
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
foreach ($f in 'README.md', 'THIRD_PARTY_NOTICES.md', 'DEPENDENCIES.md') { Copy-Item (Join-Path $root $f) $stage }
New-Item -ItemType Directory -Force (Join-Path $stage 'docs') | Out-Null
Copy-Item (Join-Path $root 'docs/*.md') (Join-Path $stage 'docs')
Copy-Item (Join-Path $root 'docs/screenshots') (Join-Path $stage 'docs') -Recurse

# The bundled .NET runtime's own license and notices.
$sdkRoot = Split-Path (Get-Command $dotnet).Source
foreach ($pair in @(@('LICENSE.txt', 'dotnet-LICENSE.txt'), @('ThirdPartyNotices.txt', 'dotnet-THIRD-PARTY-NOTICES.txt'))) {
    $src = Join-Path $sdkRoot $pair[0]
    if (Test-Path $src) { Copy-Item $src (Join-Path $stage $pair[1]) } else { Write-Warning "$src not found" }
}

# Portable mode marker: settings are stored in .\data next to the executable when the folder is writable.
Set-Content -Path (Join-Path $stage 'portable.txt') -Encoding ascii -Value @(
    'Portable mode marker for Xbox Controller Center.',
    'While this file exists and this folder is writable, settings and profiles are stored in .\data.',
    'Delete this file to store them in %LOCALAPPDATA%\XboxControllerCenter instead.')

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

$exe = Join-Path $stage 'XboxControllerCenter.exe'
$lines = foreach ($file in @($exe, $zip)) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $file -Leaf)"
}
Set-Content -Path (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii -Value $lines
$lines
Write-Host "Packaged $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
