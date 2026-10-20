param(
    [string]$Version = '1.0.1',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version.' }
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$suffix = if ($FrameworkDependent) { '-framework-dependent' } else { '' }
$packageName = "AIUsageWidget-v$Version-win-x64$suffix"
$publish = Join-Path $artifacts $packageName
if (Test-Path -LiteralPath $publish) { throw "Output already exists: $packageName. Move it first or use a different version." }
New-Item -ItemType Directory -Path $publish -Force | Out-Null
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$arguments = @('publish', (Join-Path $root 'src\Widget\CodexUsageWidgetApp.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', $selfContained, '-o', $publish, "-p:Version=$Version")
# Use the current supported .NET 8 servicing release, not a developer machine's old SDK patch.
if (-not $FrameworkDependent) {
    $releases = Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json'
    $runtimeVersion = $releases.'latest-runtime'
    if ($runtimeVersion -notmatch '^8\.0\.\d+$') { throw 'Could not determine .NET 8 runtime version.' }
    $arguments += "-p:RuntimeFrameworkVersion=$runtimeVersion"
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $publish }
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination (Join-Path $publish 'docs') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Configure.cmd') -Destination $publish
if (-not $FrameworkDependent) {
    $packages = $env:NUGET_PACKAGES
    if (-not $packages) { $packages = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
    $licenseOutput = Join-Path $publish 'docs\runtime-licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    foreach ($runtimePackage in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $packageDirectory = Join-Path (Join-Path $packages $runtimePackage) $runtimeVersion
        foreach ($notice in Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE(?:\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' }) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseOutput "$runtimePackage-$($notice.Name)")
        }
    }
}
$archive = Join-Path $artifacts "$packageName.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $packageName.zip" | Set-Content -LiteralPath (Join-Path $artifacts "$packageName.sha256") -Encoding Ascii
Write-Output "Created $packageName.zip and SHA-256 checksum."
