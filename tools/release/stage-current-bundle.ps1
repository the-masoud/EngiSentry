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
    'EngiSentry.AutoCAD2021.dll',
    'EngiSentry.Contracts.dll',
    'EngiSentry.Core.dll',
    'EngiSentry.Semantics.dll',
    'EngiSentry.Rules.dll',
    'EngiSentry.PlotPlan.dll',
    'EngiSentry.Standards.dll',
    'EngiSentry.CrossDocument.dll',
    'EngiSentry.Revision.dll',
    'ExcelDataReader.dll'
)

if (-not (Test-Path -LiteralPath $manifestSource -PathType Leaf))
{
    throw "Required manifest is missing: $manifestSource"
}

foreach ($dllName in $dllNames)
{
    $sourcePath = Join-Path $releaseRoot $dllName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf))
    {
        throw "Required Release DLL is missing: $sourcePath"
    }
}

if (Test-Path -LiteralPath $bundleRoot)
{
    throw "destination bundle already exists: $bundleRoot"
}

New-Item -ItemType Directory -Path $win64Root -Force | Out-Null
Copy-Item -LiteralPath $manifestSource -Destination (Join-Path $bundleRoot 'PackageContents.xml')
foreach ($dllName in $dllNames)
{
    Copy-Item -LiteralPath (Join-Path $releaseRoot $dllName) -Destination (Join-Path $win64Root $dllName)
}

$expectedRelativePaths = @('PackageContents.xml') + @($dllNames | ForEach-Object { 'Contents/Win64/' + $_ })
$stagedFiles = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse)
$stagedRelativePaths = @($stagedFiles | ForEach-Object { $_.FullName.Substring($bundleRoot.Length + 1).Replace('\', '/') } | Sort-Object)
$expectedRelativePaths = @($expectedRelativePaths | Sort-Object)
if ($stagedRelativePaths.Count -ne 11 -or $expectedRelativePaths.Count -ne 11 -or (Compare-Object $stagedRelativePaths $expectedRelativePaths))
{
    throw 'staged bundle does not contain exactly the required manifest and ten DLLs'
}

$forbiddenFiles = @($stagedFiles | Where-Object {
    $_.Extension -in @('.pdb', '.exe', '.dwg', '.bak') -or
    $_.Name -match '(?i)^(acmgd|acdbmgd|accoremgd)\.dll$'
})
if ($forbiddenFiles.Count -ne 0)
{
    throw 'forbidden staged file detected'
}

foreach ($dllName in $dllNames)
{
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $releaseRoot $dllName) -Algorithm SHA256).Hash
    $stagedHash = (Get-FileHash -LiteralPath (Join-Path $win64Root $dllName) -Algorithm SHA256).Hash
    if ($sourceHash -ne $stagedHash)
    {
        throw "staged DLL hash does not match source: $dllName"
    }
}

$manifestHash = (Get-FileHash -LiteralPath $manifestSource -Algorithm SHA256).Hash
$stagedManifestHash = (Get-FileHash -LiteralPath (Join-Path $bundleRoot 'PackageContents.xml') -Algorithm SHA256).Hash
if ($manifestHash -ne $stagedManifestHash)
{
    throw 'staged manifest hash does not match source'
}

Write-Output "P2.6 bundle staging PASS: $bundleRoot"
