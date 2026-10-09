param([Parameter(Mandatory=$true)][string]$TModLoaderPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'MilkcraftTerraria'
$saveDir = Join-Path $repoRoot 'build-sandbox'
$outputDir = Join-Path $repoRoot 'dist'
$dotnet = Join-Path $TModLoaderPath 'dotnet\dotnet.exe'
$loader = Join-Path $TModLoaderPath 'tModLoader.dll'
if (!(Test-Path -LiteralPath $dotnet) -or !(Test-Path -LiteralPath $loader)) { throw 'tModLoader runtime not found' }
New-Item -ItemType Directory -Force -Path $saveDir,$outputDir | Out-Null
Push-Location $TModLoaderPath
try {
    & $dotnet $loader -server -build $sourceDir -tmlsavedirectory $saveDir
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $saveDir 'Mods\MilkcraftTerraria.tmod') -Destination $outputDir -Force
