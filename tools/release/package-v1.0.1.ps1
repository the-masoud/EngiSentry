$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$version = '1.0.1'
$releaseRoot = Join-Path $repo 'artifacts\release\v1.0.1'
$bundleRoot = Join-Path $releaseRoot 'EngiSentry.bundle'
$win64Root = Join-Path $bundleRoot 'Contents\Win64'
$manifest = Join-Path $repo 'packaging\EngiSentry.bundle\PackageContents.xml'
$releaseReadme = Join-Path $repo 'packaging\RELEASE-README.md'
$dllNames = @('EngiSentry.AutoCAD2021.dll','EngiSentry.Contracts.dll','EngiSentry.Core.dll','EngiSentry.Semantics.dll','EngiSentry.Rules.dll','EngiSentry.PlotPlan.dll','EngiSentry.Standards.dll','EngiSentry.CrossDocument.dll','EngiSentry.Revision.dll')
$dllRoot = Join-Path $repo 'src\EngiSentry.AutoCAD2021\bin\x64\Release'

if (-not (Test-Path -LiteralPath $manifest)) { throw 'PackageContents.xml is missing.' }
if (-not (Test-Path -LiteralPath $releaseReadme)) { throw 'RELEASE-README.md is missing.' }
[xml]$xml = Get-Content -Raw -LiteralPath $manifest
$app = $xml.ApplicationPackage
if ($app.AppVersion -ne $version -or $app.SchemaVersion -ne '1.0') { throw 'Manifest version or schema is invalid.' }
$component = @($app.Components.ComponentEntry)
if ($component.Count -ne 1 -or $component.OS -ne 'Win64' -or $component.Platform -ne 'AutoCAD' -or $component.SeriesMin -ne 'R24.0' -or $component.SeriesMax -ne 'R24.0' -or $component.ModuleName -ne './Contents/Win64/EngiSentry.AutoCAD2021.dll' -or $component.LoadReasons -ne 'LoadOnCommandInvocation') { throw 'Manifest target or load behavior is invalid.' }
$commands = @($component.Commands.Command)
$expectedCommands = @('ENGISENTRY','ES_HELLO','ES_SCAN','ES_SEMANTIC','ES_INDEX','ES_QA','ES_PLOTQA','ES_PROFILEQA','ES_CROSSQA','ES_REVBASELINE','ES_REVISION')
if ($commands.Count -ne $expectedCommands.Count -or (Compare-Object $commands.Global $expectedCommands)) { throw 'Manifest command registration is invalid.' }

if (Test-Path -LiteralPath $releaseRoot) { Remove-Item -LiteralPath $releaseRoot -Recurse -Force }
New-Item -ItemType Directory -Path $win64Root -Force | Out-Null
Copy-Item -LiteralPath $manifest -Destination $bundleRoot
Copy-Item -LiteralPath $releaseReadme -Destination $releaseRoot
foreach ($dll in $dllNames) { $source = Join-Path $dllRoot $dll; if (-not (Test-Path -LiteralPath $source)) { throw "Required Release DLL is missing: $source" }; Copy-Item -LiteralPath $source -Destination (Join-Path $win64Root $dll) }

$stagedFiles = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse | Where-Object { $_.Name -in $dllNames -or $_.Name -eq 'PackageContents.xml' })
if ($stagedFiles.Count -ne 10) { throw 'Staged bundle does not contain exactly the manifest and nine DLLs.' }
if (Get-ChildItem -LiteralPath $bundleRoot -File -Recurse | Where-Object { $_.Extension -in '.pdb','.exe','.dwg' -or $_.Name -match '^(acmgd|acdbmgd|accoremgd)\.dll$' }) { throw 'Forbidden staged file detected.' }
$hashLines = foreach ($file in ($stagedFiles | Sort-Object { $_.FullName.Substring($bundleRoot.Length + 1) }, @{Expression={$_.Name};Ascending=$true})) { $relative = $file.FullName.Substring($releaseRoot.Length + 1).Replace('\','/'); $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant(); "$hash  $relative" }
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value $hashLines -Encoding ascii

$zipRoot = Join-Path $releaseRoot '_zip-staging'; New-Item -ItemType Directory -Path $zipRoot -Force | Out-Null
Copy-Item -LiteralPath $bundleRoot -Destination $zipRoot -Recurse
Copy-Item -LiteralPath (Join-Path $releaseRoot 'RELEASE-README.md') -Destination $zipRoot
Copy-Item -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Destination $zipRoot
$zipPath = Join-Path $releaseRoot 'EngiSentry-v1.0.1.zip'; if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $zipRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $zipRoot -Recurse -Force
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant(); Set-Content -LiteralPath ($zipPath + '.sha256') -Value "$zipHash  EngiSentry-v1.0.1.zip" -Encoding ascii

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try { $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') } | Where-Object { -not $_.EndsWith('/') } | Sort-Object); $expected = @('EngiSentry.bundle/Contents/Win64/EngiSentry.AutoCAD2021.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.Contracts.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.Core.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.Semantics.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.Rules.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.PlotPlan.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.Standards.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.CrossDocument.dll','EngiSentry.bundle/Contents/Win64/EngiSentry.Revision.dll','EngiSentry.bundle/PackageContents.xml','RELEASE-README.md','SHA256SUMS.txt') | Sort-Object; if ($entries.Count -ne $expected.Count -or (Compare-Object $entries $expected)) { throw 'ZIP contents are invalid.' } } finally { $archive.Dispose() }
Write-Output "Package validation PASS: $releaseRoot"
