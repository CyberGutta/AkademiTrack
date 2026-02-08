#!/usr/bin/env pwsh
# AkademiTrack Windows Build and Package Tool (PowerShell Version)
# Contact: cyberbrothershq@gmail.com
# Website: https://cybergutta.github.io/CG/
# GitHub: https://github.com/CyberGutta/AkademiTrack

param(
    [string]$Version = "",
    [switch]$UpdateProject = $false,
    [switch]$Help = $false
)

function Show-Help {
    Write-Host "AkademiTrack Windows Build and Package Tool" -ForegroundColor Cyan
    Write-Host "=" * 50
    Write-Host "Usage: .\build-windows.ps1 [-Version <version>] [-UpdateProject] [-Help]"
    Write-Host ""
    Write-Host "Parameters:"
    Write-Host "  -Version <version>    Specify version (e.g. 1.0.1)"
    Write-Host "  -UpdateProject        Update version in .csproj file"
    Write-Host "  -Help                 Show this help message"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\build-windows.ps1                    # Interactive mode"
    Write-Host "  .\build-windows.ps1 -Version 1.0.1     # Specify version"
    Write-Host "  .\build-windows.ps1 -Version 1.0.1 -UpdateProject  # Update .csproj too"
}

function Get-CurrentVersion {
    try {
        [xml]$csproj = Get-Content ".\AkademiTrack.csproj"
        $version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ -ne $null } | Select-Object -First 1
        return $version
    }
    catch {
        Write-Host "Could not read version from .csproj: $($_.Exception.Message)" -ForegroundColor Yellow
        return $null
    }
}

function Update-CsprojVersion {
    param([string]$NewVersion)
    
    try {
        [xml]$csproj = Get-Content ".\AkademiTrack.csproj"
        
        $versionUpdated = $false
        foreach ($propertyGroup in $csproj.Project.PropertyGroup) {
            if ($propertyGroup.Version) {
                $propertyGroup.Version = $NewVersion
                $versionUpdated = $true
                break
            }
        }
        
        if (-not $versionUpdated) {
            $firstPropertyGroup = $csproj.Project.PropertyGroup[0]
            if ($firstPropertyGroup) {
                $versionElement = $csproj.CreateElement("Version")
                $versionElement.InnerText = $NewVersion
                $firstPropertyGroup.AppendChild($versionElement) | Out-Null
                $versionUpdated = $true
            }
        }
        
        if ($versionUpdated) {
            $csproj.Save(".\AkademiTrack.csproj")
            Write-Host "Updated .csproj version to $NewVersion" -ForegroundColor Green
            return $true
        }
        else {
            Write-Host "Could not update version in .csproj" -ForegroundColor Yellow
            return $false
        }
    }
    catch {
        Write-Host "Failed to update .csproj: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Build-WindowsRelease {
    param([string]$Version)
    
    Write-Host ""
    Write-Host "Building AkademiTrack for Windows (x64)..." -ForegroundColor Cyan
    Write-Host "=" * 50
    
    # Directories
    $publishDir = ".\publish-win"
    $publishSingle = ".\publish-win-single"
    $releaseFolder = ".\Releases\v$Version"
    
    # Clean directories
    if (Test-Path $publishDir) {
        Write-Host "Cleaning publish directory..."
        Remove-Item $publishDir -Recurse -Force
    }
    
    if (Test-Path $publishSingle) {
        Write-Host "Cleaning single-file directory..."
        Remove-Item $publishSingle -Recurse -Force
    }
    
    if (Test-Path $releaseFolder) {
        Write-Host "Cleaning release folder..."
        Remove-Item $releaseFolder -Recurse -Force
    }
    
    # Create release folder
    Write-Host "Creating release folder: $releaseFolder"
    New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
    
    # Step 1: Publish for VPK (multi-file)
    Write-Host ""
    Write-Host "Step 1: Publishing for VPK (multi-file)..." -ForegroundColor Cyan
    
    $publishArgs = @(
        "publish"
        "-c", "Release"
        "--self-contained"
        "-r", "win-x64"
        "-o", $publishDir
        "-p:PublishSingleFile=false"
    )
    
    Write-Host "Running: dotnet $($publishArgs -join ' ')"
    $publishOutput = & dotnet @publishArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed:" -ForegroundColor Red
        Write-Host $publishOutput -ForegroundColor Red
        return $null
    }
    
    Write-Host "Published for VPK successfully" -ForegroundColor Green
    
    # Step 2: Publish single file exe
    Write-Host ""
    Write-Host "Step 2: Publishing standalone single-file exe..." -ForegroundColor Cyan
    
    $publishSingleArgs = @(
        "publish"
        "-c", "Release"
        "--self-contained"
        "-r", "win-x64"
        "-o", $publishSingle
        "-p:PublishSingleFile=true"
        "-p:IncludeNativeLibrariesForSelfExtract=true"
        "-p:EnableCompressionInSingleFile=true"
    )
    
    Write-Host "Running: dotnet $($publishSingleArgs -join ' ')"
    $publishSingleOutput = & dotnet @publishSingleArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Single-file publish failed:" -ForegroundColor Red
        Write-Host $publishSingleOutput -ForegroundColor Red
        return $null
    }
    
    Write-Host "Published single-file exe successfully" -ForegroundColor Green
    
    # Verify single-file executable
    $exeSingle = Join-Path $publishSingle "AkademiTrack.exe"
    if (-not (Test-Path $exeSingle)) {
        Write-Host "Single-file executable not found: $exeSingle" -ForegroundColor Red
        return $null
    }
    
    $exeSize = (Get-Item $exeSingle).Length / 1MB
    $exeSizeRounded = [math]::Round($exeSize, 1)
    Write-Host "Single-file executable: AkademiTrack.exe ($exeSizeRounded MB)" -ForegroundColor Green
    
    if ($exeSize -lt 10) {
        Write-Host "Warning: Executable seems too small ($exeSizeRounded MB). This might be a stub, not a full exe." -ForegroundColor Yellow
    }
    
    # Step 3: Create portable ZIP
    Write-Host ""
    Write-Host "Step 3: Creating portable ZIP..." -ForegroundColor Cyan
    $portableZip = Join-Path $releaseFolder "AkademiTrack-win-Portable.zip"
    
    try {
        Compress-Archive -Path "$publishDir\*" -DestinationPath $portableZip -Force
        $fileCount = (Get-ChildItem $publishDir -Recurse -File).Count
        Write-Host "Added $fileCount files to portable ZIP" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed to create portable ZIP: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
    
    $portableSize = (Get-Item $portableZip).Length / 1MB
    $portableSizeRounded = [math]::Round($portableSize, 1)
    Write-Host "Portable ZIP created: AkademiTrack-win-Portable.zip ($portableSizeRounded MB)" -ForegroundColor Green
    
    # Step 4: Create VPK package with Velopack
    Write-Host ""
    Write-Host "Step 4: Creating VPK package with Velopack..." -ForegroundColor Cyan
    
    $vpkExe = "$env:USERPROFILE\.dotnet\tools\vpk.exe"
    if (-not (Test-Path $vpkExe)) {
        Write-Host "Velopack CLI not found. Install with: dotnet tool install -g vpk" -ForegroundColor Yellow
        Write-Host "Skipping VPK package creation..." -ForegroundColor Yellow
    }
    else {
        $vpkArgs = @(
            "pack"
            "--packId", "AkademiTrack"
            "--packVersion", $Version
            "--packDir", $publishDir
            "--mainExe", "AkademiTrack.exe"
            "--outputDir", $releaseFolder
        )
        
        Write-Host "Running: vpk $($vpkArgs -join ' ')"
        $vpkOutput = & $vpkExe @vpkArgs 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "VPK package created successfully" -ForegroundColor Green
            
            # Check for Setup.exe
            $setupExe = Join-Path $releaseFolder "AkademiTrack-Setup.exe"
            if (Test-Path $setupExe) {
                $setupSize = (Get-Item $setupExe).Length / 1MB
                $setupSizeRounded = [math]::Round($setupSize, 1)
                Write-Host "Windows Setup created: AkademiTrack-Setup.exe ($setupSizeRounded MB)" -ForegroundColor Green
            }
        }
        else {
            Write-Host "VPK package creation failed:" -ForegroundColor Yellow
            Write-Host $vpkOutput -ForegroundColor Yellow
        }
    }
    
    # Step 5: Copy standalone exe
    Write-Host ""
    Write-Host "Step 5: Adding standalone single-file EXE..." -ForegroundColor Cyan
    $standaloneExe = Join-Path $releaseFolder "AkademiTrack.exe"
    Copy-Item $exeSingle $standaloneExe
    $standaloneSize = (Get-Item $standaloneExe).Length / 1MB
    $standaloneSizeRounded = [math]::Round($standaloneSize, 1)
    Write-Host "Standalone single-file EXE: AkademiTrack.exe ($standaloneSizeRounded MB)" -ForegroundColor Green
    
    # Verify release folder
    if (-not (Test-Path $releaseFolder)) {
        Write-Host "Release folder was not created!" -ForegroundColor Red
        return $null
    }
    
    $filesInRelease = Get-ChildItem $releaseFolder
    if ($filesInRelease.Count -eq 0) {
        Write-Host "Release folder is empty!" -ForegroundColor Red
        return $null
    }
    
    Write-Host "Release folder created successfully: $releaseFolder" -ForegroundColor Green
    return $releaseFolder
}

# Main execution
if ($Help) {
    Show-Help
    exit 0
}

Write-Host "AkademiTrack Windows Build and Package Tool" -ForegroundColor Cyan
Write-Host "=" * 50
Write-Host "Contact: cyberbrothershq@gmail.com"
Write-Host "Website: https://cybergutta.github.io/CG/"
Write-Host "GitHub: https://github.com/CyberGutta/AkademiTrack"
Write-Host "=" * 50

# Get version
if (-not $Version) {
    Write-Host ""
    Write-Host "Version Configuration" -ForegroundColor Cyan
    Write-Host "=" * 50
    
    $currentVersion = Get-CurrentVersion
    if ($currentVersion) {
        Write-Host "Current version in .csproj: $currentVersion"
    }
    
    $prompt = "Enter version number (e.g. 1.0.1) or press Enter to use current"
    if ($currentVersion) {
        $prompt += " [$currentVersion]"
    }
    else {
        $prompt += " [1.0.0]"
    }
    
    $inputVersion = Read-Host $prompt
    
    if (-not $inputVersion) {
        $Version = if ($currentVersion) { $currentVersion } else { "1.0.0" }
    }
    else {
        $Version = $inputVersion
    }
    
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "Invalid version format. Using default: 1.0.0" -ForegroundColor Yellow
        $Version = "1.0.0"
    }
}

Write-Host ""
Write-Host "Using version: $Version" -ForegroundColor Green

# Update project file if requested
if (-not $UpdateProject) {
    $updateChoice = Read-Host "Update version in .csproj file? (y/n) [n]"
    $UpdateProject = $updateChoice -eq 'y'
}

if ($UpdateProject) {
    Update-CsprojVersion -NewVersion $Version
}

# Build
$releaseFolder = Build-WindowsRelease -Version $Version

if ($releaseFolder -and (Test-Path $releaseFolder)) {
    Write-Host ""
    Write-Host "=" * 50 -ForegroundColor Green
    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "Version: $Version"
    Write-Host ""
    Write-Host "Release folder: $releaseFolder\"
    Write-Host ""
    Write-Host "Contents:"
    
    # List files
    Get-ChildItem $releaseFolder -File | Sort-Object Name | ForEach-Object {
        $size = $_.Length / 1MB
        $sizeRounded = [math]::Round($size, 1)
        Write-Host "  $($_.Name) ($sizeRounded MB)" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Files created:"
    Write-Host "  • AkademiTrack.exe - Standalone single-file executable"
    Write-Host "  • AkademiTrack-win-Portable.zip - Portable ZIP package"
    if (Get-ChildItem "$releaseFolder\*Setup.exe" -ErrorAction SilentlyContinue) {
        Write-Host "  • AkademiTrack-Setup.exe - Windows installer (RECOMMENDED!)"
    }
    if (Get-ChildItem "$releaseFolder\*.nupkg" -ErrorAction SilentlyContinue) {
        Write-Host "  • *.nupkg - VPK/NuGet package for auto-updates"
        Write-Host "  • RELEASES - VPK release manifest"
    }
    
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  • Test AkademiTrack.exe from $releaseFolder\"
    Write-Host "  • Distribute the portable ZIP for manual installs"
    Write-Host "  • Upload to your release server/CDN"
    
    Write-Host ""
    Write-Host "Built with love by CyberGutta" -ForegroundColor Magenta
    Write-Host "Andreas Nilsen and Mathias Hansen"
}
else {
    Write-Host ""
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}