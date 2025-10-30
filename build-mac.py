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

import os, shutil, subprocess, zipfile
from pathlib import Path

def create_avalonia_macos_bundle(version):
    """Create .app bundle for macOS"""
    PROJECT_PATH = "./AkademiTrack.csproj"
    BUILD_DIR = "./build"
    APP_NAME = "AkademiTrack"
    BUNDLE_IDENTIFIER = "com.CyberBrothers.akademitrack"
    ICON_PATH = "./Assets/AT-1024.icns"
    HELPER_APP_SOURCE = Path("./Assets/helper/AkademiTrackHelper.app")
    ENTITLEMENTS_PATH = Path("./entitlements.plist")
    SIGNING_IDENTITY = "Apple Development: Andreas Nilsen (673WFZN2KZ)"

    print("\n🏗️  Building AkademiTrack app for macOS Apple Silicon...")
    print("=" * 50)

    if not os.path.exists(ICON_PATH):
        print(f"❌ Icon file not found: {ICON_PATH}")
        return False
    else:
        print(f"✅ Icon file found: {ICON_PATH} ({Path(ICON_PATH).stat().st_size} bytes)")

    if os.path.exists(BUILD_DIR):
        print(f"🧹 Cleaning existing build directory: {BUILD_DIR}")
        shutil.rmtree(BUILD_DIR)

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

    build_path = Path(BUILD_DIR)
    executable_path = build_path / APP_NAME
    if not executable_path.exists():
        executables = [f for f in build_path.iterdir() if f.is_file() and os.access(f, os.X_OK)]
        if executables:
            executable_path = executables[0]
            APP_NAME = executable_path.name
            print(f"✅ Found executable: {APP_NAME}")
        else:
            print("❌ No executables found!")
            return False

    print("📦 Creating app bundle...")
    bundle_dir = build_path / f"{APP_NAME}.app"
    contents_dir = bundle_dir / "Contents"
    macos_dir = contents_dir / "MacOS"
    resources_dir = contents_dir / "Resources"
    macos_dir.mkdir(parents=True, exist_ok=True)
    resources_dir.mkdir(parents=True, exist_ok=True)

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

    icon_filename = "AppIcon.icns"
    icon_dest = resources_dir / icon_filename
    try:
        shutil.copy2(ICON_PATH, icon_dest)
        print(f"✅ Added app icon: {icon_filename} ({icon_dest.stat().st_size} bytes)")
    except Exception as e:
        print(f"❌ Failed to copy icon: {e}")
        return False

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
    <array><string>arm64</string></array>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>LSUIElement</key>
    <false/>
</dict>
</plist>"""
    plist_path = contents_dir / "Info.plist"
    with open(plist_path, "w") as f:
        f.write(info_plist_content)
    print(f"✅ Created Info.plist with version {version}")

    executable_in_bundle = macos_dir / APP_NAME
    if executable_in_bundle.exists():
        os.chmod(executable_in_bundle, 0o755)

    for lib in macos_dir.rglob("*.[ds]o"):
        os.chmod(lib, 0o755)
    print("✅ Set permissions for native libraries")

    try:
        subprocess.run(["xattr", "-cr", str(bundle_dir)], check=True)
        print("✅ Removed quarantine attributes")
    except:
        pass

    try:
        subprocess.run(["touch", str(bundle_dir)])
        subprocess.run(["killall", "Finder"], stderr=subprocess.DEVNULL)
        subprocess.run(["killall", "Dock"], stderr=subprocess.DEVNULL)
        print("✅ Refreshed icon cache")
    except:
        pass

    # ✅ Bundle AkademiTrackHelper.app
    helper_dest = macos_dir / "AkademiTrackHelper.app"
    if HELPER_APP_SOURCE.exists():
        try:
            shutil.copytree(HELPER_APP_SOURCE, helper_dest, dirs_exist_ok=True)
            print("✅ AkademiTrackHelper.app bundled")
        except Exception as e:
            print(f"❌ Failed to bundle helper: {e}")
            return False
    else:
        print("⚠️ AkademiTrackHelper.app not found")

    # ✅ Sign both apps
    def sign_app(app_path, identity, entitlements_path=None):
        cmd = ["codesign", "--force", "--deep", "--sign", identity]
        if entitlements_path and entitlements_path.exists():
            cmd += ["--entitlements", str(entitlements_path)]
        cmd.append(str(app_path))
        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode == 0:
            print(f"✅ Signed: {app_path.name}")
        else:
            print(f"❌ Failed to sign {app_path.name}: {result.stderr}")

    sign_app(bundle_dir, SIGNING_IDENTITY, ENTITLEMENTS_PATH)
    sign_app(helper_dest, SIGNING_IDENTITY, ENTITLEMENTS_PATH)

    zip_path = Path(f"AkademiTrack-macOS-v{version}.zip").absolute()
    if zip_path.exists():
        zip_path.unlink()

    print("📦 Creating zip archive...")
    try:
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            for file_path in bundle_dir.rglob('*'):
                if file_path.is_file():
                    arc_name = file_path.relative_to(build_path)
                    zipf.write(file_path, arc_name)
        print(f"✅ Zip archive created: {zip_path.name} ({zip_path.stat().st_size / 1024 / 1024:.1f} MB)")
    except Exception as e:
        print(f"❌ Failed to create zip: {e}")
        return False

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