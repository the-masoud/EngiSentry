param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$releaseRoot = Join-Path $repositoryRoot 'src\EngiSentry.AutoCAD2021\bin\x64\Release'
$manifestSource = Join-Path $repositoryRoot 'packaging\EngiSentry.bundle\PackageContents.xml'
$destinationRootPath = [System.IO.Path]::GetFullPath($DestinationRoot)
$bundleRoot = Join-Path $destinationRootPath 'EngiSentry.bundle'
$win64Root = Join-Path $bundleRoot 'Contents\Win64'

$dllNames = @(
    'DocumentFormat.OpenXml.dll',
    'DocumentFormat.OpenXml.Framework.dll',
    'EngiSentry.AutoCAD2021.dll',
    'EngiSentry.Contracts.dll',
    'EngiSentry.Core.dll',
    'EngiSentry.CrossDocument.dll',
    'EngiSentry.Intelligence.dll',
    'EngiSentry.PlotPlan.dll',
    'EngiSentry.Revision.dll',
    'EngiSentry.Rules.dll',
    'EngiSentry.Semantics.dll',
    'EngiSentry.Standards.dll',
    'ExcelDataReader.dll',
    'System.Buffers.dll',
    'System.IO.Packaging.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll'
)
$resourceRelativePath = 'fa\EngiSentry.AutoCAD2021.resources.dll'

if (-not (Test-Path -LiteralPath $manifestSource -PathType Leaf))
{
    throw "Required manifest is missing: $manifestSource"
}
if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container))
{
    throw "Release output directory is missing: $releaseRoot"
}

$actualReleaseDlls = @(Get-ChildItem -LiteralPath $releaseRoot -File -Filter '*.dll' | Select-Object -ExpandProperty Name | Sort-Object)
$expectedReleaseDlls = @($dllNames | Sort-Object)
if ($actualReleaseDlls.Count -ne $expectedReleaseDlls.Count -or (Compare-Object $actualReleaseDlls $expectedReleaseDlls))
{
    throw "Release DLL inventory drift detected. Expected exactly $($expectedReleaseDlls.Count) canonical runtime DLLs."
}

foreach ($dllName in $dllNames)
{
    $sourcePath = Join-Path $releaseRoot $dllName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf))
    {
        throw "Required Release DLL is missing: $sourcePath"
    }
}

$resourceSource = Join-Path $releaseRoot $resourceRelativePath
if (-not (Test-Path -LiteralPath $resourceSource -PathType Leaf))
{
    throw "Required Persian satellite resource is missing: $resourceSource"
}

if (Test-Path -LiteralPath $bundleRoot)
{
    throw "Destination bundle already exists: $bundleRoot"
}

New-Item -ItemType Directory -Path $win64Root -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $win64Root 'fa') -Force | Out-Null
Copy-Item -LiteralPath $manifestSource -Destination (Join-Path $bundleRoot 'PackageContents.xml')

foreach ($dllName in $dllNames)
{
    Copy-Item -LiteralPath (Join-Path $releaseRoot $dllName) -Destination (Join-Path $win64Root $dllName)
}
Copy-Item -LiteralPath $resourceSource -Destination (Join-Path $win64Root $resourceRelativePath)

$expectedRelativePaths = @('PackageContents.xml')
$expectedRelativePaths += @($dllNames | ForEach-Object { 'Contents/Win64/' + $_ })
$expectedRelativePaths += 'Contents/Win64/fa/EngiSentry.AutoCAD2021.resources.dll'
$expectedRelativePaths = @($expectedRelativePaths | Sort-Object)

$stagedFiles = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse)
$stagedRelativePaths = @($stagedFiles | ForEach-Object {
    $_.FullName.Substring($bundleRoot.Length + 1).Replace('\', '/')
} | Sort-Object)

if ($stagedRelativePaths.Count -ne $expectedRelativePaths.Count -or (Compare-Object $stagedRelativePaths $expectedRelativePaths))
{
    throw "Staged bundle inventory mismatch. Expected exactly $($expectedRelativePaths.Count) canonical files."
}

$forbiddenFiles = @($stagedFiles | Where-Object {
    $_.Extension -in @('.pdb', '.exe', '.dwg', '.bak') -or
    $_.Name -match '(?i)^(acmgd|acdbmgd|accoremgd)\.dll$'
})
if ($forbiddenFiles.Count -ne 0)
{
    throw 'Forbidden staged file detected.'
}

foreach ($dllName in $dllNames)
{
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $releaseRoot $dllName) -Algorithm SHA256).Hash
    $stagedHash = (Get-FileHash -LiteralPath (Join-Path $win64Root $dllName) -Algorithm SHA256).Hash
    if ($sourceHash -ne $stagedHash)
    {
        throw "Staged DLL hash does not match source: $dllName"
    }
}

$resourceHash = (Get-FileHash -LiteralPath $resourceSource -Algorithm SHA256).Hash
$stagedResourceHash = (Get-FileHash -LiteralPath (Join-Path $win64Root $resourceRelativePath) -Algorithm SHA256).Hash
if ($resourceHash -ne $stagedResourceHash)
{
    throw 'Staged Persian satellite resource hash does not match source.'
}

$manifestHash = (Get-FileHash -LiteralPath $manifestSource -Algorithm SHA256).Hash
$stagedManifestHash = (Get-FileHash -LiteralPath (Join-Path $bundleRoot 'PackageContents.xml') -Algorithm SHA256).Hash
if ($manifestHash -ne $stagedManifestHash)
{
    throw 'Staged manifest hash does not match source.'
}

Write-Output "CURRENT_BUNDLE_STAGE_PASS"
Write-Output "BUNDLE=$bundleRoot"
Write-Output "FILE_COUNT=$($stagedRelativePaths.Count)"
Write-Output "MAIN_DLL_SHA256=$((Get-FileHash -LiteralPath (Join-Path $win64Root 'EngiSentry.AutoCAD2021.dll') -Algorithm SHA256).Hash)"
Write-Output "MANIFEST_SHA256=$stagedManifestHash"
