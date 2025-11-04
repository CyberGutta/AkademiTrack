#!/usr/bin/env python3
import os
import shutil
import subprocess
import zipfile
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import time

# ============================================================================
# CONFIGURATION - Update these values
# ============================================================================

# Code Signing Identities
DEVELOPER_ID_APP = "Developer ID Application: Your Name (TEAM_ID)"
DEVELOPER_ID_INSTALLER = "Developer ID Installer: Your Name (TEAM_ID)"

# Apple Developer Credentials for Notarization
APPLE_ID = "your-apple-id@example.com"
TEAM_ID = "YOUR_TEAM_ID"  # Find at https://developer.apple.com/account
APP_SPECIFIC_PASSWORD = "xxxx-xxxx-xxxx-xxxx"  # Generate at appleid.apple.com

# App Details
APP_NAME = "AkademiTrack"
BUNDLE_IDENTIFIER = "com.CyberBrothers.akademitrack"
ICON_PATH = "./Assets/AT-1024.icns"
ENTITLEMENTS_PATH = Path("./entitlements.plist")
HELPER_APP_SOURCE = Path("./Assets/Helpers/AkademiTrack.app")

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

def run_command(cmd, description="", check=True):
    """Run a command and handle errors"""
    if description:
        print(f"  {description}...")
    result = subprocess.run(cmd, capture_output=True, text=True)
    if check and result.returncode != 0:
        print(f"❌ Failed: {result.stderr}")
        return False
    return result

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

# ============================================================================
# CODE SIGNING FUNCTIONS
# ============================================================================

def sign_app(app_path, identity, entitlements_path=None, deep=True):
    """Sign an application bundle"""
    print(f"\n🔏 Signing {app_path.name}...")
    
    cmd = ["codesign", "--force", "--sign", identity, "--timestamp"]
    
    if deep:
        cmd.append("--deep")
    
    cmd.extend([
        "--options", "runtime",  # Hardened runtime required for notarization
        "--verbose"
    ])
    
    if entitlements_path and entitlements_path.exists():
        cmd.extend(["--entitlements", str(entitlements_path)])
    
    cmd.append(str(app_path))
    
    result = run_command(cmd, f"Signing {app_path.name}", check=False)
    
    if result and result.returncode == 0:
        print(f"✅ Signed: {app_path.name}")
        # Verify signature
        verify_result = run_command(
            ["codesign", "--verify", "--deep", "--strict", "--verbose=2", str(app_path)],
            "Verifying signature",
            check=False
        )
        if verify_result and verify_result.returncode == 0:
            print(f"✅ Signature verified")
            return True
        else:
            print(f"⚠️  Signature verification failed")
            return False
    else:
        print(f"❌ Failed to sign {app_path.name}")
        return False

def sign_pkg(pkg_path, identity):
    """Sign a .pkg installer"""
    print(f"\n🔏 Signing installer package...")
    
    cmd = [
        "productsign",
        "--sign", identity,
        "--timestamp",
        str(pkg_path),
        str(pkg_path.with_suffix('.signed.pkg'))
    ]
    
    result = run_command(cmd, f"Signing {pkg_path.name}", check=False)
    
    if result and result.returncode == 0:
        # Replace unsigned with signed
        shutil.move(str(pkg_path.with_suffix('.signed.pkg')), str(pkg_path))
        print(f"✅ Signed: {pkg_path.name}")
        
        # Verify
        verify_result = run_command(
            ["pkgutil", "--check-signature", str(pkg_path)],
            "Verifying package signature",
            check=False
        )
        if verify_result:
            print(verify_result.stdout)
        return True
    else:
        print(f"❌ Failed to sign package")
        return False

# ============================================================================
# NOTARIZATION FUNCTIONS
# ============================================================================

def notarize_app(file_path, bundle_id):
    """Submit app for notarization and wait for result"""
    print(f"\n📝 Notarizing {file_path.name}...")
    print("⏳ This may take 5-15 minutes...")
    
    # Create a temporary zip for notarization
    zip_path = file_path.with_suffix('.zip')
    
    if file_path.suffix == '.app':
        print("  Creating zip for notarization...")
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            for file in file_path.rglob('*'):
                if file.is_file():
                    arcname = file.relative_to(file_path.parent)
                    zipf.write(file, arcname)
        notarize_target = zip_path
    else:
        notarize_target = file_path
    
    # Submit for notarization
    cmd = [
        "xcrun", "notarytool", "submit",
        str(notarize_target),
        "--apple-id", APPLE_ID,
        "--team-id", TEAM_ID,
        "--password", APP_SPECIFIC_PASSWORD,
        "--wait"
    ]
    
    print("  Submitting to Apple...")
    result = run_command(cmd, check=False)
    
    # Clean up temporary zip
    if file_path.suffix == '.app' and zip_path.exists():
        zip_path.unlink()
    
    if result and result.returncode == 0:
        print("✅ Notarization successful!")
        
        # Staple the notarization ticket
        if file_path.suffix in ['.app', '.pkg']:
            print("  Stapling notarization ticket...")
            staple_cmd = ["xcrun", "stapler", "staple", str(file_path)]
            staple_result = run_command(staple_cmd, check=False)
            if staple_result and staple_result.returncode == 0:
                print("✅ Notarization ticket stapled")
            else:
                print("⚠️  Failed to staple ticket (not critical)")
        
        return True
    else:
        print("❌ Notarization failed")
        if result:
            print(result.stdout)
            print(result.stderr)
        return False

# ============================================================================
# BUILD FUNCTIONS
# ============================================================================

def create_avalonia_macos_bundle(version, sign=True, notarize=True):
    """Create .app bundle for macOS"""
    PROJECT_PATH = "./AkademiTrack.csproj"
    BUILD_DIR = "./build"
    
    print("\n🏗️  Building AkademiTrack app for macOS Apple Silicon...")
    print("=" * 50)

    if not os.path.exists(ICON_PATH):
        print(f"❌ Icon file not found: {ICON_PATH}")
        return False

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
    shutil.copy2(ICON_PATH, icon_dest)

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

    subprocess.run(["xattr", "-cr", str(bundle_dir)], check=False)

    # Bundle and sign helper app FIRST (before signing main app)
    helper_dest = macos_dir / "AkademiTrack.app"
    if HELPER_APP_SOURCE.exists():
        print("\n📦 Bundling helper app (AkademiTrack.app)...")
        try:
            # Copy helper app
            if helper_dest.exists():
                shutil.rmtree(helper_dest)
            shutil.copytree(HELPER_APP_SOURCE, helper_dest, dirs_exist_ok=True)
            print("✅ Helper app copied to bundle")
            
            # Set executable permissions on helper app
            helper_executable = helper_dest / "Contents" / "MacOS" / "AkademiTrack"
            if helper_executable.exists():
                os.chmod(helper_executable, 0o755)
                print("✅ Helper app executable permissions set")
            
            # Clear extended attributes from helper app
            subprocess.run(["xattr", "-cr", str(helper_dest)], check=False)
            
            # Sign helper app FIRST (critical: sign nested apps before parent app)
            if sign:
                print("\n🔐 Signing helper app (MUST be done before main app)...")
                if not sign_app(helper_dest, DEVELOPER_ID_APP, ENTITLEMENTS_PATH, deep=True):
                    print("❌ Helper app signing failed - this will cause Gatekeeper issues!")
                    print("⚠️  Continuing anyway, but app may not work on other Macs...")
                else:
                    print("✅ Helper app signed successfully")
        except Exception as e:
            print(f"❌ Failed to bundle/sign helper app: {e}")
            print("⚠️  Continuing without helper app...")
    else:
        print(f"⚠️  Helper app not found at: {HELPER_APP_SOURCE}")
        print("    If you need authentication features, add the helper app to Assets/Helpers/")

    # Sign the main app (AFTER helper app is signed)
    # IMPORTANT: Do NOT use --deep here, as it will re-sign the helper app incorrectly
    if sign:
        print("\n🔐 Signing main app bundle...")
        # Sign without --deep since we already signed nested components
        if not sign_app(bundle_dir, DEVELOPER_ID_APP, ENTITLEMENTS_PATH, deep=False):
            print("❌ Main app signing failed")
            return False
        print("✅ Main app signed successfully")
    
    # Notarize the app (this covers both main and helper apps)
    if notarize and sign:
        print("\n📝 Notarizing complete app bundle...")
        if not notarize_app(bundle_dir, BUNDLE_IDENTIFIER):
            print("⚠️  Notarization failed, but app is signed")
            print("    The app will work but may show Gatekeeper warnings on first launch")
            # Continue anyway, app will work but show warning

    print("\n✅ App bundle creation completed!")
    print(f"📦 Bundle location: {bundle_dir}")
    
    return bundle_dir

def create_portable_zip(bundle_dir, version, notarize=True):
    """Create portable zip distribution"""
    print("\n📦 Creating portable zip...")
    print("=" * 50)
    
    zip_path = Path(f"AkademiTrack-osx-Portable.zip").absolute()
    if zip_path.exists():
        zip_path.unlink()

    try:
        with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            for file_path in bundle_dir.rglob('*'):
                if file_path.is_file():
                    arc_name = file_path.relative_to(bundle_dir.parent)
                    zipf.write(file_path, arc_name)
        
        print(f"✅ Portable zip created: {zip_path.name} ({zip_path.stat().st_size / 1024 / 1024:.1f} MB)")
        
        # Notarize the zip
        if notarize:
            notarize_app(zip_path, BUNDLE_IDENTIFIER)
        
        return zip_path
    except Exception as e:
        print(f"❌ Failed to create zip: {e}")
        return None

def create_installer_pkg(bundle_dir, version, sign=True, notarize=True):
    """Create .pkg installer"""
    print("\n📦 Creating installer package...")
    print("=" * 50)
    
    pkg_path = Path(f"AkademiTrack-osx-Setup.pkg").absolute()
    
    # Create pkg using pkgbuild
    cmd = [
        "pkgbuild",
        "--root", str(bundle_dir.parent),
        "--identifier", BUNDLE_IDENTIFIER,
        "--version", version,
        "--install-location", "/Applications",
        str(pkg_path)
    ]
    
    result = run_command(cmd, "Building package", check=False)
    
    if not result or result.returncode != 0:
        print("❌ Failed to create package")
        return None
    
    print(f"✅ Package created: {pkg_path.name} ({pkg_path.stat().st_size / 1024 / 1024:.1f} MB)")
    
    # Sign the package
    if sign:
        if not sign_pkg(pkg_path, DEVELOPER_ID_INSTALLER):
            print("⚠️  Package signing failed")
            return pkg_path
    
    # Notarize the package
    if notarize and sign:
        notarize_app(pkg_path, BUNDLE_IDENTIFIER)
    
    return pkg_path

def create_velopack_package(version):
    """Create VPK package using Velopack"""
    print("\n📦 Creating VPK Package (NuGet)")
    print("=" * 50)

    publish_dir = Path("./publish-mac-arm")

    try:
        subprocess.run(["vpk"], capture_output=True, text=True)
        print(f"✅ Velopack (vpk) found")
    except FileNotFoundError:
        print("❌ Velopack (vpk) not found!")
        print("Install it with: dotnet tool install -g vpk")
        return False

    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    publish_dir.mkdir(parents=True)

    print(f"📂 Publishing to {publish_dir}...")

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

    print(f"📦 Creating VPK package (version {version})...")

    icon_path = Path(ICON_PATH)

    vpk_cmd = [
        "vpk", "pack",
        "--packId", "AkademiTrack",
        "--packVersion", version,
        "--packDir", str(publish_dir),
        "--mainExe", APP_NAME,
        "--icon", str(icon_path.absolute())
    ]

    result = subprocess.run(vpk_cmd, capture_output=True, text=True)

    if result.returncode != 0:
        print(f"❌ VPK pack failed: {result.stderr}")
        return False

    print("✅ VPK package created successfully")
    
    # Rename to match your naming convention
    nupkg_file = list(Path(".").glob("AkademiTrack.*.nupkg"))
    if nupkg_file:
        new_name = f"AkademiTrack-{version}-osx-full.nupkg"
        shutil.move(str(nupkg_file[0]), new_name)
        print(f"✅ Renamed to: {new_name}")
    
    return True

# ============================================================================
# MAIN FUNCTION
# ============================================================================

def main():
    print("🚀 AkademiTrack Build, Sign & Notarize Tool")
    print("=" * 50)

    # Verify configuration
    print("\n🔍 Checking configuration...")
    if "Your Name" in DEVELOPER_ID_APP or "your-apple-id" in APPLE_ID:
        print("❌ Please update the configuration at the top of this script!")
        print("   - DEVELOPER_ID_APP")
        print("   - DEVELOPER_ID_INSTALLER")
        print("   - APPLE_ID")
        print("   - TEAM_ID")
        print("   - APP_SPECIFIC_PASSWORD")
        return

    # Get version number
    version = get_version_input()
    print(f"\n📌 Using version: {version}")

    # Ask if user wants to update .csproj
    update_proj = input("\nUpdate version in .csproj file? (y/n) [y]: ").strip().lower()
    if update_proj != 'n':
        update_csproj_version(version)

    # Ask about signing and notarization
    print("\n🔐 Signing & Notarization Options:")
    sign_choice = input("Sign applications? (y/n) [y]: ").strip().lower()
    do_sign = sign_choice != 'n'
    
    do_notarize = False
    if do_sign:
        notarize_choice = input("Notarize applications? (y/n) [y]: ").strip().lower()
        do_notarize = notarize_choice != 'n'

    # Build the app
    print("\n" + "=" * 50)
    bundle_dir = create_avalonia_macos_bundle(version, sign=do_sign, notarize=do_notarize)
    if not bundle_dir:
        print("❌ App bundle creation failed")
        return

    # Ask what distributions to create
    print("\n📦 Distribution Options:")
    print("1. Portable ZIP")
    print("2. Installer PKG")
    print("3. VPK Package (NuGet)")
    print("4. All of the above")
    
    dist_choice = input("\nSelect option (1/2/3/4) [4]: ").strip()
    if not dist_choice:
        dist_choice = "4"
    
    success = True
    
    if dist_choice in ["1", "4"]:
        create_portable_zip(bundle_dir, version, notarize=do_notarize)
    
    if dist_choice in ["2", "4"]:
        create_installer_pkg(bundle_dir, version, sign=do_sign, notarize=do_notarize)
    
    if dist_choice in ["3", "4"]:
        create_velopack_package(version)
    
    # Final summary
    print("\n" + "=" * 50)
    print("🎉 Build completed successfully!")
    print(f"📦 Version: {version}")
    print("\n📋 Files created:")
    print(f"  ✅ .app bundle: ./build/{APP_NAME}.app")
    
    if dist_choice in ["1", "4"]:
        print(f"  ✅ Portable ZIP: AkademiTrack-osx-Portable.zip")
    if dist_choice in ["2", "4"]:
        print(f"  ✅ Installer PKG: AkademiTrack-osx-Setup.pkg")
    if dist_choice in ["3", "4"]:
        print(f"  ✅ VPK Package: AkademiTrack-{version}-osx-full.nupkg")
    
    if do_sign:
        print("\n✅ All files signed with Developer ID")
    if do_notarize:
        print("✅ All files notarized by Apple")
    
    print("\n📋 Next steps:")
    print("  • Test the .app by opening it")
    print("  • Test the .pkg installer")
    print("  • Distribute the files to users")
    if do_notarize:
        print("  • No Gatekeeper warnings will appear!")
    else:
        print("  • Users may need to right-click → Open (Gatekeeper)")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n⚠️  Build interrupted by user")
    except Exception as e:
        print(f"\n❌ Unexpected error: {e}")
        import traceback
        traceback.print_exc()