param([string]$DotNet = '', [switch]$NoRestore, [string]$RuntimeDirectory = '')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceRoot = Split-Path -Parent $projectRoot
$toolsRoot = Join-Path $workspaceRoot '.buildtools'
if (-not $RuntimeDirectory) {
    $RuntimeDirectory = Join-Path $workspaceRoot 'AsusBiosDiyStudio\dist\AsusBiosDiyStudio'
}
foreach ($dependency in @('runtime\node.exe', 'tools\UEFIExtract.exe', 'tools\UEFIReplace.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $RuntimeDirectory $dependency))) {
        throw "Missing $dependency. Use -RuntimeDirectory to select an extracted portable release outside the output directory."
    }
}
if (-not $DotNet) {
    $localSdk = Join-Path $toolsRoot 'dotnet\dotnet.exe'
    $DotNet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
}
$env:DOTNET_CLI_HOME = Join-Path $toolsRoot 'cli'
$env:NUGET_PACKAGES = Join-Path $toolsRoot 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dist = Join-Path $projectRoot 'dist\AsusBiosDiyStudio.WinUI'
$arguments = @('publish', (Join-Path $projectRoot 'AsusBiosDiyStudio.WinUI.csproj'), '-c', 'Release', '-r', 'win-x64', '-p:Platform=x64', '-o', $dist)
if ($NoRestore) { $arguments += '--no-restore' }
& $DotNet @arguments
if ($LASTEXITCODE -ne 0) { throw "WinUI publish failed: $LASTEXITCODE" }

foreach ($directory in @('runtime', 'tools', 'scripts')) { New-Item -ItemType Directory -Path (Join-Path $dist $directory) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'runtime\node.exe') -Destination (Join-Path $dist 'runtime\node.exe')
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'tools\UEFIExtract.exe') -Destination (Join-Path $dist 'tools\UEFIExtract.exe')
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'tools\UEFIReplace.exe') -Destination (Join-Path $dist 'tools\UEFIReplace.exe')
foreach ($name in @('unpack_asus_ui_bundle.mjs', 'replace_ui_bundle_images.mjs', 'inventory_amitse_diy_colors.mjs', 'patch_amitse_diy_colors.mjs')) {
    Copy-Item -LiteralPath (Join-Path $workspaceRoot "tools\$name") -Destination (Join-Path $dist "scripts\$name")
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $dist 'README.md')
$zip = Join-Path $projectRoot 'dist\AsusBiosDiyStudio-WinUI3-win-x64.zip'
Compress-Archive -LiteralPath $dist -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Host "Built: $dist"
Write-Host "Archive: $zip"
