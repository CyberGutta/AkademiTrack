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
    
    current_version = get_current_version()
    if current_version:
        print(f"Current version in .csproj: {current_version}")
    
    version = input(f"Enter version number (e.g., 1.0.1) or press Enter to use current [{current_version or '1.0.0'}]: ").strip()
    
    if not version:
        version = current_version or "1.0.0"
    
    if not re.match(r'^\d+\.\d+\.\d+$', version):
        print(f"⚠️  Invalid version format. Using default: 1.0.0")
        version = "1.0.0"
    
    return version

def get_current_version():
    """Read current version from .csproj file"""
    try:
        tree = ET.parse("./AkademiTrack.csproj")
        root = tree.getroot()
        
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
        
        for prop_group in root.findall('.//PropertyGroup'):
            version_elem = prop_group.find('Version')
            if version_elem is not None:
                version_elem.text = version
                version_updated = True
                break
        
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

def build_windows_release(version):
    """Build Windows release - creates exe, portable zip, and VPK package"""
    
    print(f"\n🏗️  Building AkademiTrack for Windows (x64)...")
    print("=" * 50)
    
    # Directories
    publish_dir = Path("./publish-win")
    publish_single = Path("./publish-win-single")
    release_folder = Path(f"./Releases/v{version}")
    
    # Check for icon file
    icon_path = Path("./Assets/AT-1024.ico")
    if not icon_path.exists():
        print(f"⚠️  Icon file not found: {icon_path}")
        print("Checking for alternative formats...")
        # Check for PNG as fallback
        png_icon = Path("./Assets/AT-1024.png")
        if png_icon.exists():
            print(f"⚠️  Found PNG but VPK requires .ico format")
            print("Please convert AT-1024.png to AT-1024.ico")
        icon_path = None
    else:
        icon_size = icon_path.stat().st_size
        print(f"✅ Icon file found: {icon_path} ({icon_size} bytes)")
    
    # Clean directories
    if publish_dir.exists():
        print(f"🧹 Cleaning publish directory...")
        shutil.rmtree(publish_dir)
    
    if publish_single.exists():
        print(f"🧹 Cleaning single-file directory...")
        shutil.rmtree(publish_single)
    
    if release_folder.exists():
        print(f"🧹 Cleaning release folder...")
        shutil.rmtree(release_folder)
    
    release_folder.mkdir(parents=True, exist_ok=True)
    
    # Step 1: Publish for VPK (multi-file, needed for VPK)
    print(f"\n📦 Step 1: Publishing for VPK (multi-file)...")
    publish_cmd = [
        "dotnet", "publish",
        "-c", "Release",
        "--self-contained",
        "-r", "win-x64",
        "-o", str(publish_dir),
        "-p:PublishSingleFile=false"
    ]
    
    print(f"Running: {' '.join(publish_cmd)}")
    result = subprocess.run(publish_cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"❌ Publish failed: {result.stderr}")
        return False
    
    print("✅ Published for VPK successfully")
    
    # Step 2: Publish single file exe (for standalone distribution)
    print(f"\n📦 Step 2: Publishing standalone single-file exe...")
    publish_single_cmd = [
        "dotnet", "publish",
        "-c", "Release",
        "--self-contained",
        "-r", "win-x64",
        "-o", str(publish_single),
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    ]
    
    print(f"Running: {' '.join(publish_single_cmd)}")
    result = subprocess.run(publish_single_cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"❌ Single-file publish failed: {result.stderr}")
        return False
    
    print("✅ Published single-file exe successfully")
    
    # Verify single-file executable exists and is substantial
    exe_single = publish_single / "AkademiTrack.exe"
    if not exe_single.exists():
        print(f"❌ Single-file executable not found: {exe_single}")
        return False
    
    exe_size = exe_single.stat().st_size / 1024 / 1024
    print(f"✅ Single-file executable: {exe_single.name} ({exe_size:.1f} MB)")
    
    if exe_size < 10:
        print(f"⚠️  Warning: Executable seems too small ({exe_size:.1f} MB). This might be a stub, not a full exe.")
    
    # Step 3: Create portable ZIP from multi-file build
    print(f"\n📦 Step 3: Creating portable ZIP...")
    portable_zip = release_folder / f"AkademiTrack-win-Portable.zip"
    
    try:
        with zipfile.ZipFile(portable_zip, 'w', zipfile.ZIP_DEFLATED) as zipf:
            file_count = 0
            for file_path in publish_dir.rglob('*'):
                if file_path.is_file():
                    arc_name = file_path.relative_to(publish_dir)
                    zipf.write(file_path, arc_name)
                    file_count += 1
            print(f"✅ Added {file_count} files to portable ZIP")
    except Exception as e:
        print(f"❌ Failed to create portable ZIP: {e}")
        return False
    
    portable_size = portable_zip.stat().st_size / 1024 / 1024
    print(f"✅ Portable ZIP created: {portable_zip.name} ({portable_size:.1f} MB)")
    
    # Step 4: Create VPK package
    print(f"\n📦 Step 4: Creating VPK package...")
    
    # Check if vpk is installed
    try:
        result = subprocess.run(["vpk", "--version"], capture_output=True, text=True)
        print(f"✅ Velopack found: {result.stdout.strip()}")
    except FileNotFoundError:
        print("❌ Velopack (vpk) not found!")
        print("Install it with: dotnet tool install -g vpk")
        return False
    
    # Get absolute paths for VPK
    publish_dir_abs = publish_dir.absolute()
    
    # Build VPK command with icon
    vpk_cmd = [
        "vpk", "pack",
        "--packId", "AkademiTrack",
        "--packVersion", version,
        "--packDir", str(publish_dir_abs),
        "--mainExe", "AkademiTrack.exe"
    ]
    
    # Add icon parameter if icon exists
    if icon_path and icon_path.exists():
        vpk_cmd.extend(["--icon", str(icon_path.absolute())])
        print(f"🎨 Using icon: {icon_path}")
    else:
        print(f"⚠️  Building without custom icon (will use default)")
    
    print(f"Running: {' '.join(vpk_cmd)}")
    result = subprocess.run(vpk_cmd, capture_output=True, text=True)
    
    if result.returncode != 0:
        print(f"❌ VPK pack failed:")
        print(f"STDERR: {result.stderr}")
        print(f"STDOUT: {result.stdout}")
        return False
    
    print("✅ VPK package created successfully")
    if result.stdout:
        print(result.stdout)
    
    # Step 5: Move VPK output to release folder
    print(f"\n📦 Step 5: Organizing VPK release files...")
    
    # Give VPK a moment to release file handles
    import time
    time.sleep(1)
    
    # VPK creates files directly in the Releases folder
    vpk_releases_path = Path("./Releases")
    
    if vpk_releases_path.exists():
        print(f"✅ Found VPK Releases folder")
        
        # Copy VPK files from Releases root to our version folder
        for item in vpk_releases_path.iterdir():
            if item.is_file():
                dest = release_folder / item.name
                if dest.exists():
                    print(f"  ⏭️  Already exists: {item.name}")
                    continue
                
                try:
                    max_retries = 3
                    for retry in range(max_retries):
                        try:
                            shutil.copy2(item, dest)
                            print(f"  ✅ Copied: {item.name}")
                            break
                        except PermissionError:
                            if retry < max_retries - 1:
                                time.sleep(0.5)
                            else:
                                raise
                except Exception as e:
                    print(f"  ⚠️  Skipped {item.name}: {e}")
        
        # Clean up VPK files from root Releases folder
        for item in vpk_releases_path.iterdir():
            if item.is_file() and item.parent == vpk_releases_path:
                try:
                    item.unlink()
                except:
                    pass
        
        print(f"✅ Organized VPK files into {release_folder}")
    else:
        print(f"⚠️  VPK Releases folder not found")
    
    # Step 6: Copy the standalone single-file EXE to release folder
    print(f"\n📦 Step 6: Adding standalone single-file EXE...")
    standalone_exe = release_folder / "AkademiTrack.exe"
    shutil.copy2(exe_single, standalone_exe)
    standalone_size = standalone_exe.stat().st_size / 1024 / 1024
    print(f"✅ Standalone single-file EXE: {standalone_exe.name} ({standalone_size:.1f} MB)")
    
    # Verify the release folder exists and has files
    if not release_folder.exists():
        print(f"❌ Release folder was not created!")
        return False
    
    files_in_release = list(release_folder.iterdir())
    if not files_in_release:
        print(f"❌ Release folder is empty!")
        return False
    
    return release_folder

def main():
    print("🚀 AkademiTrack Windows Build & Package Tool")
    print("=" * 50)
    
    # Get version number
    version = get_version_input()
    print(f"\n📌 Using version: {version}")
    
    # Ask if user wants to update .csproj
    update_proj = input("\nUpdate version in .csproj file? (y/n) [n]: ").strip().lower()
    if update_proj == 'y':  # Only updates if you type 'y'
        update_csproj_version(version)
    
    # Build everything
    release_folder = build_windows_release(version)
    
    if release_folder:
        print("\n" + "=" * 50)
        print("🎉 Build completed successfully!")
        print(f"📦 Version: {version}")
        print(f"\n📁 Release folder: {release_folder}/")
        print("\n📋 Contents:")
        
        # List all files in release folder
        for item in sorted(release_folder.iterdir()):
            if item.is_file():
                size = item.stat().st_size / 1024 / 1024
                print(f"  ✅ {item.name} ({size:.1f} MB)")
        
        print("\n📋 Files created:")
        print(f"  • AkademiTrack.exe - Standalone single-file executable (RUN THIS ONE!)")
        print(f"  • AkademiTrack-win-Portable.zip - Portable ZIP package")
        print(f"  • *.nupkg - VPK/NuGet package for auto-updates")
        print(f"  • RELEASES - VPK release manifest")
        print(f"  • AkademiTrack-win-Setup.exe - Installer with auto-update support")
        
        print("\n💡 Next steps:")
        print(f"  • Test AkademiTrack.exe from {release_folder}/")
        print(f"  • Distribute the portable ZIP for manual installs")
        print(f"  • Use the .nupkg + RELEASES for auto-updates")
        print(f"  • Upload to your release server/CDN")
        
        print("\n🎨 Icon troubleshooting:")
        print(f"  • Make sure Assets/AT-1024.ico exists (not just .png)")
        print(f"  • Convert PNG to ICO if needed (online tools available)")
        print(f"  • High-res ICO should contain multiple sizes: 16x16, 32x32, 48x48, 256x256")
    else:
        print("\n❌ Build failed!")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n⚠️  Build interrupted by user")
    except Exception as e:
        print(f"\n❌ Unexpected error: {e}")
        import traceback
        traceback.print_exc()