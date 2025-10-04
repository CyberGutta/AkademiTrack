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

def create_vpk_package(version, build_dir):
    """Create VPK package using Velopack"""
    print("\n📦 Creating VPK Package")
    print("=" * 50)

    publish_dir = Path("./publish-mac-arm")


    # Check if vpk is installed (without --version flag)
    try:
        result = subprocess.run(["vpk"], capture_output=True, text=True)
        print(f"✅ Velopack (vpk) found")
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
        "-r", "osx-arm64",
        "-o", str(publish_dir)
    ]
    result = subprocess.run(publish_cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"❌ Publish failed: {result.stderr}")
        return False
    print("✅ Published successfully")

    # Create VPK package
    print(f"📦 Creating VPK package (version {version})...")

    # Check if icon exists for VPK
    icon_path = Path("./Assets/AT-1024.icns")

    vpk_cmd = [
        "vpk", "pack",
        "--packId", "AkademiTrack",
        "--packVersion", version,
        "--packDir", str(publish_dir),
        "--mainExe", "AkademiTrack",
        "--icon", str(icon_path.absolute())
    ]

    print(f"Running: {' '.join(vpk_cmd)}")
    result = subprocess.run(vpk_cmd, capture_output=True, text=True)

    if result.returncode != 0:
        print(f"❌ VPK pack failed: {result.stderr}")
        return False

    print("✅ VPK package created successfully")
    if result.stdout:
        print(result.stdout)

    return True

def create_avalonia_macos_bundle(version):
    """Create .app bundle for macOS"""
    # Configuration for AkademiTrack
    PROJECT_PATH = "./AkademiTrack.csproj"
    BUILD_DIR = "./build"
    APP_NAME = "AkademiTrack"
    BUNDLE_IDENTIFIER = "com.CyberBrothers.akademitrack"
    ICON_PATH = "./Assets/AT-1024.icns"
    
    print("\n🏗️  Building AkademiTrack app for macOS Apple Silicon...")
    print("=" * 50)

    # Verify icon exists before building
    if not os.path.exists(ICON_PATH):
        print(f"❌ Icon file not found: {ICON_PATH}")
        print("\n📁 Looking for icon files in Assets directory...")
        assets_dir = Path("./Assets")
        if assets_dir.exists():
            icon_files = list(assets_dir.glob("*.icns"))
            if icon_files:
                print(f"Found {len(icon_files)} .icns file(s):")
                for icon in icon_files:
                    print(f"  - {icon.name} ({icon.stat().st_size} bytes)")
            else:
                print("  No .icns files found in Assets directory")
        return False
    else:
        icon_size = Path(ICON_PATH).stat().st_size
        print(f"✅ Icon file found: {ICON_PATH} ({icon_size} bytes)")
    
    # Clean build directory
    if os.path.exists(BUILD_DIR):
        print(f"🧹 Cleaning existing build directory: {BUILD_DIR}")
        shutil.rmtree(BUILD_DIR)
    
    # Build the application
    build_cmd = [
        "dotnet", "publish", PROJECT_PATH,
        "--configuration", "Release",
        "--runtime", "osx-arm64",
        "--self-contained", "true",
        "--output", BUILD_DIR,
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:PublishTrimmed=false",
        "-p:PublishSingleFile=false"
    ]
    
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
    executable_path = build_path / APP_NAME
    if not executable_path.exists():
        print(f"⚠️  Executable not found at expected path, searching...")
        executables = [f for f in build_path.iterdir() if f.is_file() and os.access(f, os.X_OK)]
        if executables:
            executable_path = executables[0]
            APP_NAME = executable_path.name
            print(f"✅ Found executable: {APP_NAME}")
        else:
            print("❌ No executables found!")
            return False
    
    print("📦 Creating app bundle...")
    
    # Create app bundle structure
    bundle_dir = build_path / f"{APP_NAME}.app"
    contents_dir = bundle_dir / "Contents"
    macos_dir = contents_dir / "MacOS"
    resources_dir = contents_dir / "Resources"
    
    # Create directories
    macos_dir.mkdir(parents=True, exist_ok=True)
    resources_dir.mkdir(parents=True, exist_ok=True)
    
    # Copy files to bundle
    files_to_bundle = [item for item in build_path.iterdir() if not item.name.endswith('.app')]

    print(f"📋 Copying {len(files_to_bundle)} items to bundle...")
    for item in files_to_bundle:
        dest_path = macos_dir / item.name
        try:

            if item.is_file():
                shutil.copy2(item, dest_path)
            elif item.is_dir():
                shutil.copytree(item, dest_path)
        except Exception as e:
            print(f"  ⚠️  Failed to copy {item.name}: {e}")
    
    # Handle icon file - CRITICAL: Use correct filename
    icon_filename = "AppIcon.icns"
    icon_dest = resources_dir / icon_filename
    
    try:
        shutil.copy2(ICON_PATH, icon_dest)
        copied_size = icon_dest.stat().st_size
        print(f"✅ Added app icon: {icon_filename} ({copied_size} bytes)")

        # Verify it was copied correctly
        if copied_size < 1000:
            print(f"⚠️  Warning: Icon file seems too small ({copied_size} bytes)")

    except Exception as e:
        print(f"❌ Failed to copy icon: {e}")

        return False

    # Create Info.plist with version
    info_plist_content = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>{APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>{BUNDLE_IDENTIFIER}</string>
    <key>CFBundleName</key>
    <string>{APP_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>AkademiTrack</string>
    <key>CFBundleVersion</key>
    <string>{version}</string>
    <key>CFBundleShortVersionString</key>
    <string>{version}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIconName</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.education</string>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright © 2025 AkademiTrack. All rights reserved.</string>
    <key>LSRequiresNativeExecution</key>
    <true/>
    <key>LSArchitecturePriority</key>
    <array>
        <string>arm64</string>
    </array>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>LSUIElement</key>
    <false/>
</dict>
</plist>"""
    
    # Write Info.plist
    plist_path = contents_dir / "Info.plist"
    with open(plist_path, "w") as f:
        f.write(info_plist_content)
    print(f"✅ Created Info.plist with version {version}")
    
    # Set executable permissions
    executable_in_bundle = macos_dir / APP_NAME
    if executable_in_bundle.exists():
        os.chmod(executable_in_bundle, 0o755)
    
    # Set permissions for native libraries
    dylib_count = sum(1 for _ in macos_dir.rglob("*.dylib"))
    so_count = sum(1 for _ in macos_dir.rglob("*.so"))
    
    for dylib in macos_dir.rglob("*.dylib"):
        os.chmod(dylib, 0o755)
    for so_file in macos_dir.rglob("*.so"):
        os.chmod(so_file, 0o755)
    
    if dylib_count + so_count > 0:
        print(f"✅ Set permissions for {dylib_count} .dylib and {so_count} .so files")
    
    # Remove quarantine attributes
    try:
        subprocess.run(["xattr", "-cr", str(bundle_dir)], check=True, capture_output=True)
        print("✅ Removed quarantine attributes")
    except (subprocess.CalledProcessError, FileNotFoundError):
        pass
    
    # Force icon cache refresh
    try:
        subprocess.run(["touch", str(bundle_dir)], capture_output=True)
        subprocess.run(["killall", "Finder"], capture_output=True, stderr=subprocess.DEVNULL)
        subprocess.run(["killall", "Dock"], capture_output=True, stderr=subprocess.DEVNULL)
        print("✅ Refreshed icon cache")
    except:
        pass
    
    # Create zip file
    zip_path = Path(f"AkademiTrack-macOS-v{version}.zip").absolute()
    if zip_path.exists():
        zip_path.unlink()
    
    print(f"📦 Creating zip archive...")
    try:
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            file_count = 0
            for file_path in bundle_dir.rglob('*'):
                if file_path.is_file():
                    arc_name = file_path.relative_to(build_path)
                    zipf.write(file_path, arc_name)
                    file_count += 1
            print(f"✅ Added {file_count} files to zip")
    except Exception as e:
        print(f"❌ Failed to create zip: {e}")
        return False

    print(f"✅ App bundle created: {bundle_dir}")
    print(f"✅ Zip archive created: {zip_path.name} ({zip_path.stat().st_size / 1024 / 1024:.1f} MB)")
    
    # Verify icon is in bundle
    final_icon_path = resources_dir / "AppIcon.icns"
    if final_icon_path.exists():
        print(f"✅ Icon verified in bundle: {final_icon_path.stat().st_size} bytes")
    else:
        print(f"⚠️  Warning: Icon not found in final bundle!")
    
    return True

def main():
    print("🚀 AkademiTrack Build & Package Tool")
    print("=" * 50)

    # Get version number
    version = get_version_input()
    print(f"\n📌 Using version: {version}")

    # Ask if user wants to update .csproj
    update_proj = input("\nUpdate version in .csproj file? (y/n) [y]: ").strip().lower()
    if update_proj != 'n':
        update_csproj_version(version)

    # Ask what to build
    print("\n🔧 Build Options:")
    print("1. Create .app bundle only")
    print("2. Create VPK package only")
    print("3. Create both")
    
    choice = input("\nSelect option (1/2/3) [3]: ").strip()
    if not choice:
        choice = "3"
    
    success = True
    
    if choice in ["1", "3"]:
        print("\n" + "=" * 50)
        success = create_avalonia_macos_bundle(version)
        if not success:
            print("❌ .app bundle creation failed")
            return

    if choice in ["2", "3"]:
        print("\n" + "=" * 50)
        vpk_success = create_vpk_package(version, "./build")
        if not vpk_success:
            print("❌ VPK package creation failed")
            success = False
    
    if success:
        print("\n" + "=" * 50)
        print("🎉 Build completed successfully!")
        print(f"📦 Version: {version}")

        if choice in ["1", "3"]:
            print(f"✅ .app bundle: ./build/AkademiTrack.app")
            print(f"✅ Zip file: AkademiTrack-macOS-v{version}.zip")

        if choice in ["2", "3"]:
            print(f"✅ VPK package: ./publish-mac-arm/")
        print("\n📋 Next steps:")
        print("  • Test the .app by opening it from ./build/")
        print("  • Distribute the zip file to users")
        print("  • Use VPK for auto-updates")
        print("  • Right-click → Open if blocked by Gatekeeper")
        print("\n💡 Icon troubleshooting:")
        print("  • If icon doesn't show, restart Finder: killall Finder")
        print("  • Clear icon cache: rm ~/Library/Caches/com.apple.iconservices.store")
        print("  • Verify icon file: ls -lh ./build/AkademiTrack.app/Contents/Resources/")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n⚠️  Build interrupted by user")
    except Exception as e:
        print(f"\n❌ Unexpected error: {e}")
        import traceback