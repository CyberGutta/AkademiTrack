#!/usr/bin/env python3
import os
import shutil
import subprocess
import zipfile
from pathlib import Path
import re
import xml.etree.ElementTree as ET

def get_version_input():
    """Get version number from user or use current version from .csproj"""
    print("\n📦 Version Configuration")
    print("=" * 50)
    
    # Try to read current version from .csproj
    current_version = get_current_version()
    if current_version:
        print(f"Current version in .csproj: {current_version}")
    
    version = input(f"Enter version number (e.g., 1.0.1) or press Enter to use current [{current_version or '1.0.0'}]: ").strip()
    
    if not version:
        version = current_version or "1.0.0"
    
    # Validate version format
    if not re.match(r'^\d+\.\d+\.\d+$', version):
        print(f"⚠️  Invalid version format. Using default: 1.0.0")
        version = "1.0.0"
    
    return version

def get_current_version():
    """Read current version from .csproj file"""
    try:
        tree = ET.parse("./AkademiTrack.csproj")
        root = tree.getroot()
        
        # Look for Version element
        for prop_group in root.findall('.//PropertyGroup'):
            version_elem = prop_group.find('Version')
            if version_elem is not None and version_elem.text:
                return version_elem.text.strip()
        
        return None
    except Exception as e:
        print(f"⚠️  Could not read version from .csproj: {e}")
        return None

def update_csproj_version(version):
    """Update version in .csproj file"""
    try:
        tree = ET.parse("./AkademiTrack.csproj")
        root = tree.getroot()
        
        version_updated = False
        
        # Look for existing Version element
        for prop_group in root.findall('.//PropertyGroup'):
            version_elem = prop_group.find('Version')
            if version_elem is not None:
                version_elem.text = version
                version_updated = True
                break
        
        # If no Version element exists, add it to first PropertyGroup
        if not version_updated:
            prop_groups = root.findall('.//PropertyGroup')
            if prop_groups:
                version_elem = ET.SubElement(prop_groups[0], 'Version')
                version_elem.text = version
                version_updated = True
        
        if version_updated:
            tree.write("./AkademiTrack.csproj", encoding='utf-8', xml_declaration=True)
            print(f"✅ Updated .csproj version to {version}")
            return True
        else:
            print(f"⚠️  Could not update version in .csproj")
            return False
            
    except Exception as e:
        print(f"❌ Failed to update .csproj: {e}")
        return False

def create_windows_package(version, use_single_file=True):
    """Create Windows package with optional single file"""
    PROJECT_PATH = "./AkademiTrack.csproj"
    BUILD_DIR = "./build-windows"
    APP_NAME = "AkademiTrack"
    
    print(f"\n🏗️  Building AkademiTrack for Windows (x64)...")
    print("=" * 50)
    
    # Clean build directory
    if os.path.exists(BUILD_DIR):
        print(f"🧹 Cleaning existing build directory: {BUILD_DIR}")
        shutil.rmtree(BUILD_DIR)
    
    # Build command
    build_cmd = [
        "dotnet", "publish", PROJECT_PATH,
        "--configuration", "Release",
        "--runtime", "win-x64",
        "--self-contained", "true",
        "--output", BUILD_DIR,
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:PublishTrimmed=false",
    ]
    
    if use_single_file:
        build_cmd.append("-p:PublishSingleFile=true")
        print("📦 Building as single file executable...")
    else:
        build_cmd.append("-p:PublishSingleFile=false")
        print("📦 Building with separate files...")
    
    print(f"🔨 Building...")
    result = subprocess.run(build_cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"❌ Build failed: {result.stderr}")
        return False
    
    print("✅ Build completed successfully")
    
    # Check what was built
    build_path = Path(BUILD_DIR)
    if not build_path.exists():
        print(f"❌ Build directory doesn't exist: {build_path}")
        return False
    
    # Find the executable
    exe_path = build_path / f"{APP_NAME}.exe"
    if not exe_path.exists():
        print(f"⚠️  Executable not found at expected path, searching...")
        exes = list(build_path.glob("*.exe"))
        if exes:
            exe_path = exes[0]
            APP_NAME = exe_path.stem
            print(f"✅ Found executable: {APP_NAME}.exe")
        else:
            print("❌ No executables found!")
            return False
    
    exe_size = exe_path.stat().st_size / 1024 / 1024
    print(f"✅ Executable: {exe_path.name} ({exe_size:.1f} MB)")
    
    # Create zip file
    zip_path = Path(f"AkademiTrack-Windows-v{version}.zip").absolute()
    if zip_path.exists():
        zip_path.unlink()
    
    print(f"📦 Creating zip archive...")
    try:
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            file_count = 0
            for file_path in build_path.rglob('*'):
                if file_path.is_file():
                    arc_name = file_path.relative_to(build_path)
                    zipf.write(file_path, arc_name)
                    file_count += 1
            print(f"✅ Added {file_count} files to zip")
    except Exception as e:
        print(f"❌ Failed to create zip: {e}")
        return False
    
    zip_size = zip_path.stat().st_size / 1024 / 1024
    print(f"✅ Zip archive created: {zip_path.name} ({zip_size:.1f} MB)")
    
    return True

def create_vpk_package(version, use_single_file=True):
    """Create VPK package using Velopack"""
    print("\n📦 Creating VPK Package for Windows")
    print("=" * 50)
    
    publish_dir = Path("./publish-windows")
    
    # Check if vpk is installed
    try:
        result = subprocess.run(["vpk", "--version"], capture_output=True, text=True)
        print(f"✅ Velopack found: {result.stdout.strip()}")
    except FileNotFoundError:
        print("❌ Velopack (vpk) not found!")
        print("Install it with: dotnet tool install -g vpk")
        return False
    
    # Clean and create publish directory
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    publish_dir.mkdir(parents=True)
    
    print(f"📂 Publishing to {publish_dir}...")
    
    # Publish the application
    publish_cmd = [
        "dotnet", "publish",
        "-c", "Release",
        "--self-contained",
        "-r", "win-x64",
        "-o", str(publish_dir),
        "-p:PublishTrimmed=false"
    ]
    
    if use_single_file:
        publish_cmd.append("-p:PublishSingleFile=true")
        publish_cmd.append("-p:IncludeNativeLibrariesForSelfExtract=true")
        print("📦 Publishing as single file for VPK...")
    else:
        publish_cmd.append("-p:PublishSingleFile=false")
        print("📦 Publishing with separate files for VPK...")
    
    result = subprocess.run(publish_cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"❌ Publish failed: {result.stderr}")
        return False
    
    print("✅ Published successfully")
    
    # Create VPK package
    print(f"📦 Creating VPK package (version {version})...")
    
    vpk_cmd = [
        "vpk", "pack",
        "--packId", "AkademiTrack",
        "--packVersion", version,
        "--packDir", str(publish_dir),
        "--mainExe", "AkademiTrack.exe"
    ]
    
    print(f"Running: {' '.join(vpk_cmd)}")
    result = subprocess.run(vpk_cmd, capture_output=True, text=True)
    
    if result.returncode != 0:
        print(f"❌ VPK pack failed: {result.stderr}")
        return False
    
    print("✅ VPK package created successfully")
    print(result.stdout)
    
    return True

def main():
    print("🚀 AkademiTrack Windows Build & Package Tool")
    print("=" * 50)
    
    # Get version number
    version = get_version_input()
    print(f"\n📌 Using version: {version}")
    
    # Ask if user wants to update .csproj
    update_proj = input("\nUpdate version in .csproj file? (y/n) [y]: ").strip().lower()
    if update_proj != 'n':
        update_csproj_version(version)
    
    # Ask about single file
    print("\n📦 Build Type:")
    print("1. Single file executable (recommended)")
    print("2. Multiple files")
    
    single_choice = input("\nSelect option (1/2) [1]: ").strip()
    use_single_file = (single_choice != "2")
    
    # Ask what to build
    print("\n🔧 Build Options:")
    print("1. Create Windows zip only")
    print("2. Create VPK package only")
    print("3. Create both")
    
    choice = input("\nSelect option (1/2/3) [3]: ").strip()
    if not choice:
        choice = "3"
    
    success = True
    
    if choice in ["1", "3"]:
        print("\n" + "=" * 50)
        success = create_windows_package(version, use_single_file)
        if not success:
            print("❌ Windows package creation failed")
            return
    
    if choice in ["2", "3"]:
        print("\n" + "=" * 50)
        vpk_success = create_vpk_package(version, use_single_file)
        if not vpk_success:
            print("❌ VPK package creation failed")
            success = False
    
    if success:
        print("\n" + "=" * 50)
        print("🎉 Build completed successfully!")
        print(f"📦 Version: {version}")
        
        if choice in ["1", "3"]:
            print(f"✅ Windows build: ./build-windows/")
            print(f"✅ Zip file: AkademiTrack-Windows-v{version}.zip")
        
        if choice in ["2", "3"]:
            print(f"✅ VPK package: ./publish-windows/")
        
        print("\n📋 Next steps:")
        print("  • Test the .exe from ./build-windows/")
        print("  • Distribute the zip file to users")
        print("  • Use VPK for auto-updates")
        print(f"  • {'Single file' if use_single_file else 'Multiple files'} build ready!")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n⚠️  Build interrupted by user")
    except Exception as e:
        print(f"\n❌ Unexpected error: {e}")
        import traceback
        traceback.print_exc()