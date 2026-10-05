param(
    [string]$InstallRoot = (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'),
    [string]$BackupRoot = (Join-Path $env:LOCALAPPDATA 'EngiSentry\deployment-backups')
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stageScript = Join-Path $repositoryRoot 'tools\release\stage-current-bundle.ps1'
$manifestSource = Join-Path $repositoryRoot 'packaging\EngiSentry.bundle\PackageContents.xml'
$releaseRoot = Join-Path $repositoryRoot 'src\EngiSentry.AutoCAD2021\bin\x64\Release'
$installedBundle = Join-Path $InstallRoot 'EngiSentry.bundle'

if (Get-Process acad -ErrorAction SilentlyContinue)
{
    throw 'AutoCAD is running. Close every acad.exe process before deploying EngiSentry.'
}
if (-not (Test-Path -LiteralPath $stageScript -PathType Leaf))
{
    throw "Stage script is missing: $stageScript"
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stageRootRequested = Join-Path $env:TEMP ("EngiSentry-deploy-" + $stamp + "-" + $PID)
$stageRoot = $null
$stagedBundle = $null
$backupPath = Join-Path $BackupRoot ("EngiSentry.bundle-" + $stamp)
$failedNewPath = Join-Path $BackupRoot ("EngiSentry.bundle-failed-new-" + $stamp)
$previousMoved = $false
$newInstalled = $false
$stagedFiles = @()
$stagedHashes = @{}

function RelativeFiles([string]$root)
{
    $rootFullName = (Get-Item -LiteralPath $root -ErrorAction Stop).FullName.TrimEnd('\')
    return @(Get-ChildItem -LiteralPath $rootFullName -File -Recurse | ForEach-Object {
        $_.FullName.Substring($rootFullName.Length + 1).Replace('\', '/')
    } | Sort-Object)
}

function Assert-CanonicalInstalledBundle([string]$bundle)
{
    if (-not (Test-Path -LiteralPath $bundle -PathType Container))
    {
        throw "Installed bundle is missing: $bundle"
    }

    $installedFiles = RelativeFiles $bundle
    if ($stagedFiles.Count -ne $installedFiles.Count -or (Compare-Object $stagedFiles $installedFiles))
    {
        throw 'Installed bundle inventory does not match the verified staged bundle.'
    }

    foreach ($relativePath in $stagedFiles)
    {
        $installedFile = Join-Path $bundle ($relativePath.Replace('/', '\'))
        $installedHash = (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash
        if ($stagedHashes[$relativePath] -ne $installedHash)
        {
            throw "Installed file hash mismatch: $relativePath"
        }
    }

    $releaseMain = Join-Path $releaseRoot 'EngiSentry.AutoCAD2021.dll'
    $installedMain = Join-Path $bundle 'Contents\Win64\EngiSentry.AutoCAD2021.dll'
    if ((Get-FileHash -LiteralPath $releaseMain -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $installedMain -Algorithm SHA256).Hash)
    {
        throw 'Installed main DLL does not match current Release output.'
    }

    $installedManifest = Join-Path $bundle 'PackageContents.xml'
    if ((Get-FileHash -LiteralPath $manifestSource -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $installedManifest -Algorithm SHA256).Hash)
    {
        throw 'Installed PackageContents.xml does not match the repository manifest.'
    }
}

try
{
    New-Item -ItemType Directory -Path $stageRootRequested -Force | Out-Null
    $stageRoot = (Get-Item -LiteralPath $stageRootRequested -ErrorAction Stop).FullName
    $stagedBundle = Join-Path $stageRoot 'EngiSentry.bundle'
    & $stageScript -DestinationRoot $stageRoot | Out-Host

    if (-not (Test-Path -LiteralPath $stagedBundle -PathType Container))
    {
        throw "Verified staged bundle was not produced: $stagedBundle"
    }

    $stagedFiles = RelativeFiles $stagedBundle
    foreach ($relativePath in $stagedFiles)
    {
        $stageFile = Join-Path $stagedBundle ($relativePath.Replace('/', '\'))
        $stagedHashes[$relativePath] = (Get-FileHash -LiteralPath $stageFile -Algorithm SHA256).Hash
    }

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null

    if (Test-Path -LiteralPath $installedBundle)
    {
        Move-Item -LiteralPath $installedBundle -Destination $backupPath
        $previousMoved = $true
    }

    Move-Item -LiteralPath $stagedBundle -Destination $installedBundle
    $newInstalled = $true

    Assert-CanonicalInstalledBundle $installedBundle

    $installedMain = Join-Path $installedBundle 'Contents\Win64\EngiSentry.AutoCAD2021.dll'
    $installedManifest = Join-Path $installedBundle 'PackageContents.xml'

    Write-Output 'CURRENT_BUNDLE_DEPLOY_PASS'
    Write-Output "INSTALLED_BUNDLE=$installedBundle"
    Write-Output "BACKUP_BUNDLE=$(if ($previousMoved) { $backupPath } else { 'NONE' })"
    Write-Output "FILE_COUNT=$($stagedFiles.Count)"
    Write-Output "MAIN_DLL_SHA256=$((Get-FileHash -LiteralPath $installedMain -Algorithm SHA256).Hash)"
    Write-Output "MANIFEST_SHA256=$((Get-FileHash -LiteralPath $installedManifest -Algorithm SHA256).Hash)"
}
catch
{
    $originalError = $_

    try
    {
        if ($newInstalled -and (Test-Path -LiteralPath $installedBundle))
        {
            if (Test-Path -LiteralPath $failedNewPath)
            {
                throw "Rollback target already exists: $failedNewPath"
            }
            Move-Item -LiteralPath $installedBundle -Destination $failedNewPath
        }

        if ($previousMoved -and (Test-Path -LiteralPath $backupPath) -and -not (Test-Path -LiteralPath $installedBundle))
        {
            Move-Item -LiteralPath $backupPath -Destination $installedBundle
        }
    }
    catch
    {
        throw "DEPLOY_FAILED_AND_ROLLBACK_FAILED: $($originalError.Exception.Message) | ROLLBACK: $($_.Exception.Message)"
    }

    throw "DEPLOY_FAILED_ROLLED_BACK: $($originalError.Exception.Message)"
}
finally
{
    $cleanupRoot = if ($stageRoot) { $stageRoot } else { $stageRootRequested }
    if ($cleanupRoot -and (Test-Path -LiteralPath $cleanupRoot))
    {
        Remove-Item -LiteralPath $cleanupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
