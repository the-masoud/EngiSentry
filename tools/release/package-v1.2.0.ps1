$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$version = '1.2.0'
$releaseRoot = Join-Path $repo 'artifacts\release\v1.2.0'
$bundleRoot = Join-Path $releaseRoot 'EngiSentry.bundle'
$win64Root = Join-Path $bundleRoot 'Contents\Win64'
$manifest = Join-Path $repo 'packaging\EngiSentry.bundle\PackageContents.xml'
$releaseReadme = Join-Path $repo 'packaging\RELEASE-README.md'
$stageScript = Join-Path $repo 'tools\release\stage-current-bundle.ps1'
$dllNames = @('EngiSentry.AutoCAD2021.dll','EngiSentry.Contracts.dll','EngiSentry.Core.dll','EngiSentry.Semantics.dll','EngiSentry.Rules.dll','EngiSentry.PlotPlan.dll','EngiSentry.Standards.dll','EngiSentry.CrossDocument.dll','EngiSentry.Revision.dll','ExcelDataReader.dll')
$dllRoot = Join-Path $repo 'src\EngiSentry.AutoCAD2021\bin\x64\Release'

if (-not (Test-Path -LiteralPath $manifest) -or -not (Test-Path -LiteralPath $releaseReadme) -or -not (Test-Path -LiteralPath $stageScript)) { throw 'Required packaging input is missing.' }
[xml]$xml = Get-Content -Raw -LiteralPath $manifest
$app = $xml.ApplicationPackage
if ($app.AppVersion -ne $version -or $app.SchemaVersion -ne '1.0') { throw 'Manifest version or schema is invalid.' }
$component = @($app.Components.ComponentEntry)
if ($component.Count -ne 1 -or $component.OS -ne 'Win64' -or $component.Platform -ne 'AutoCAD' -or $component.SeriesMin -ne 'R24.0' -or $component.SeriesMax -ne 'R24.0' -or $component.ModuleName -ne './Contents/Win64/EngiSentry.AutoCAD2021.dll' -or $component.LoadReasons -ne 'LoadOnCommandInvocation') { throw 'Manifest target or load behavior is invalid.' }
$expectedCommands = @('ENGISENTRY','ES_HELLO','ES_SCAN','ES_SEMANTIC','ES_INDEX','ES_QA','ES_PLOTQA','ES_PROFILEQA','ES_CROSSQA','ES_REVBASELINE','ES_REVISION','ES_DISTANCESETTINGS')
$commands = @($component.Commands.Command | ForEach-Object { $_.Global })
if ($commands.Count -ne $expectedCommands.Count -or (Compare-Object $commands $expectedCommands)) { throw 'Manifest command registration is invalid.' }

$mainDll = Join-Path $dllRoot 'EngiSentry.AutoCAD2021.dll'
if (-not (Test-Path -LiteralPath $mainDll)) { throw 'Release main DLL is missing.' }
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($mainDll)
if ($versionInfo.FileVersion -ne '1.2.0.0' -or -not $versionInfo.ProductVersion.StartsWith('1.2.0', [StringComparison]::Ordinal)) { throw 'Release main DLL version is invalid.' }

if (Test-Path -LiteralPath $releaseRoot) { Remove-Item -LiteralPath $releaseRoot -Recurse -Force }
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
& $stageScript -DestinationRoot $releaseRoot
Copy-Item -LiteralPath $releaseReadme -Destination $releaseRoot

$stagedFiles = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse)
$expectedBundle = @('PackageContents.xml') + @($dllNames | ForEach-Object { 'Contents/Win64/' + $_ })
$actualBundle = @($stagedFiles | ForEach-Object { $_.FullName.Substring($bundleRoot.Length + 1).Replace('\','/') } | Sort-Object)
if ($actualBundle.Count -ne 11 -or (Compare-Object $actualBundle ($expectedBundle | Sort-Object))) { throw 'Staged bundle contents are invalid.' }
$forbidden = @($stagedFiles | Where-Object { $_.Extension -in @('.pdb','.exe','.dwg','.bak') -or $_.Name -match '(?i)^(acmgd|acdbmgd|accoremgd)\.dll$' })
if ($forbidden.Count -ne 0) { throw 'Forbidden staged file detected.' }
foreach ($dll in $dllNames) { if ((Get-FileHash -LiteralPath (Join-Path $dllRoot $dll) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $win64Root $dll) -Algorithm SHA256).Hash) { throw "Staged DLL hash does not match Release: $dll" } }

$hashFiles = @((Get-Item -LiteralPath (Join-Path $bundleRoot 'PackageContents.xml'))) + @($dllNames | ForEach-Object { Get-Item -LiteralPath (Join-Path $win64Root $_) })
$hashLines = foreach ($file in ($hashFiles | Sort-Object FullName)) { $relative = $file.FullName.Substring($releaseRoot.Length + 1).Replace('\','/'); "$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant())  $relative" }
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value $hashLines -Encoding ascii

$zipStaging = Join-Path $releaseRoot '_zip-staging'
New-Item -ItemType Directory -Path $zipStaging -Force | Out-Null
Copy-Item -LiteralPath $bundleRoot -Destination $zipStaging -Recurse
Copy-Item -LiteralPath (Join-Path $releaseRoot 'RELEASE-README.md') -Destination $zipStaging
Copy-Item -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Destination $zipStaging
$zipPath = Join-Path $releaseRoot 'EngiSentry-v1.2.0.zip'
Compress-Archive -Path (Join-Path $zipStaging '*') -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $zipStaging -Recurse -Force
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
Set-Content -LiteralPath ($zipPath + '.sha256') -Value "$zipHash  EngiSentry-v1.2.0.zip" -Encoding ascii

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') } | Where-Object { -not $_.EndsWith('/') } | Sort-Object)
    $expectedZip = @((@('EngiSentry.bundle/PackageContents.xml') + @($dllNames | ForEach-Object { 'EngiSentry.bundle/Contents/Win64/' + $_ }) + @('RELEASE-README.md','SHA256SUMS.txt')) | Sort-Object)
    if ($entries.Count -ne 13 -or (Compare-Object $entries $expectedZip)) { throw 'ZIP contents are invalid.' }
}
finally { $archive.Dispose() }

Write-Output "Package validation PASS: $releaseRoot"
