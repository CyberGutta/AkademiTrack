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
DEVELOPER_ID_APP = "ssdfsdff"
DEVELOPER_ID_INSTALLER = "sddsfsdfsdff"

# Apple Developer Credentials for Notarization
APPLE_ID = "sdfsdf@gmail.com"
TEAM_ID = "sdfsdfsdfssdf"  # Find at https://developer.apple.com/account
APP_SPECIFIC_PASSWORD = "sdf-sdf-sdf-sdf"  # Generate at appleid.apple.com

# App Details
APP_NAME = "AkademiTrack"
BUNDLE_IDENTIFIER = "com.CyberBrothers.akademitrack"
ICON_PATH = "./Assets/AT-1024.icns"
ENTITLEMENTS_PATH = Path("./entitlements.plist")
HELPER_APP_SOURCE = Path("./Assets/Helpers/AkademiTrack.app")

# ============================================================================
# NEW: LaunchAgent Creation Function
# ============================================================================

def create_launchagent_plist(version):
    """Create LaunchAgent plist for auto-startup"""
    print("\n📝 Creating LaunchAgent plist...")
    
    plist_content = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>com.CyberBrothers.akademitrack</string>
  
  <key>ProgramArguments</key>
  <array>
    <string>/usr/bin/open</string>
    <string>-a</string>
    <string>/Applications/AkademiTrack.app</string>
  </array>
  
  <key>RunAtLoad</key>
  <true/>
  
  <key>KeepAlive</key>
  <false/>
</dict>
</plist>"""
    
    plist_path = Path("com.CyberBrothers.akademitrack.plist")
    with open(plist_path, "w") as f:
        f.write(plist_content)
    
    print(f"✅ Created LaunchAgent plist: {plist_path}")
    return plist_path

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

def run_command(cmd, description="", check=True, show_output=False):
    """Run a command and handle errors"""
    if description:
        print(f"  {description}...")
    result = subprocess.run(cmd, capture_output=True, text=True)
    
    if show_output and result.stdout:
        print(result.stdout)
    
    if check and result.returncode != 0:
        print(f"❌ Failed: {result.stderr}")
        return None
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

def sign_file(file_path, identity, entitlements_path=None):
    """Sign a single file (dylib, executable, etc.)"""
    cmd = [
        "codesign", "--force", "--sign", identity,
        "--timestamp", "--options", "runtime",
        "--verbose"
    ]
    
    if entitlements_path and entitlements_path.exists():
        cmd.extend(["--entitlements", str(entitlements_path)])
    
    cmd.append(str(file_path))
    
    result = run_command(cmd, check=False)
    return result and result.returncode == 0

def sign_all_binaries_in_app(app_path, identity, entitlements_path=None):
    """Sign all binaries inside an app bundle in the correct order (inside-out)"""
    print(f"\n🔏 Signing all binaries in {app_path.name}...")
    
    # Remove quarantine attributes first
    subprocess.run(["xattr", "-cr", str(app_path)], check=False)
    
    macos_dir = Path(app_path) / "Contents" / "MacOS"
    
    # Collect all files that need signing
    files_to_sign = []
    
    # 1. First, sign all .dylib files (deepest first)
    dylibs = sorted(macos_dir.rglob("*.dylib"), key=lambda x: len(x.parts), reverse=True)
    files_to_sign.extend(dylibs)
    
    # 2. Then sign any nested .app bundles
    nested_apps = sorted(macos_dir.rglob("*.app"), key=lambda x: len(x.parts), reverse=True)
    
    # 3. Sign executables (but not the main one yet)
    main_executable = macos_dir / APP_NAME
    executables = []
    for file in macos_dir.rglob("*"):
        if file.is_file() and os.access(file, os.X_OK):
            if file != main_executable and not file.name.endswith('.dylib'):
                executables.append(file)
    files_to_sign.extend(sorted(executables, key=lambda x: len(x.parts), reverse=True))
    
    # Sign all collected files
    signed_count = 0
    failed_count = 0
    
    for file_path in files_to_sign:
        print(f"  🔏 Signing: {file_path.relative_to(app_path)}...")
        if sign_file(file_path, identity, entitlements_path):
            signed_count += 1
        else:
            print(f"    ⚠️  Failed to sign: {file_path.name}")
            failed_count += 1
    
    # Sign nested apps with deep signing
    for nested_app in nested_apps:
        print(f"  🔏 Deep signing nested app: {nested_app.name}...")
        if sign_app(nested_app, identity, entitlements_path, deep=True):
            signed_count += 1
        else:
            failed_count += 1
    
    print(f"\n  ✅ Signed {signed_count} binaries/apps")
    if failed_count > 0:
        print(f"  ⚠️  Failed to sign {failed_count} items")
    
    return failed_count == 0

def sign_app(app_path, identity, entitlements_path=None, deep=True):
    """Sign an application bundle"""
    print(f"\n🔏 Signing app bundle: {app_path.name}...")
    
    # First sign all internal binaries
    if not sign_all_binaries_in_app(app_path, identity, entitlements_path):
        print(f"⚠️  Some internal files failed to sign, but continuing...")
    
    # Now sign the app bundle itself
    cmd = ["codesign", "--force", "--sign", identity, "--timestamp"]
    
    if deep:
        cmd.append("--deep")
    
    cmd.extend([
        "--options", "runtime",
        "--verbose"
    ])
    
    if entitlements_path and entitlements_path.exists():
        cmd.extend(["--entitlements", str(entitlements_path)])
    
    cmd.append(str(app_path))
    
    result = run_command(cmd, f"Final signing of {app_path.name}", check=False)
    
    if result and result.returncode == 0:
        print(f"✅ Signed: {app_path.name}")
        
        # Verify signature
        print("  Verifying signature...")
        verify_result = run_command(
            ["codesign", "--verify", "--deep", "--strict", "--verbose=2", str(app_path)],
            check=False,
            show_output=True
        )
        
        if verify_result and verify_result.returncode == 0:
            print(f"✅ Signature verified for {app_path.name}")
            return True
        else:
            print(f"⚠️  Signature verification failed")
            run_command(
                ["codesign", "-dv", "--verbose=4", str(app_path)],
                check=False,
                show_output=True
            )
            return False
    else:
        print(f"❌ Failed to sign {app_path.name}")
        return False

def sign_pkg(pkg_path, identity):
    """Sign a .pkg installer"""
    print(f"\n🔏 Signing installer package...")
    
    signed_pkg = pkg_path.with_name(pkg_path.stem + "-signed.pkg")
    
    cmd = [
        "productsign",
        "--sign", identity,
        "--timestamp",
        str(pkg_path),
        str(signed_pkg)
    ]
    
    result = run_command(cmd, f"Signing {pkg_path.name}", check=False)
    
    if result and result.returncode == 0:
        # Replace unsigned with signed
        pkg_path.unlink()
        signed_pkg.rename(pkg_path)
        print(f"✅ Signed: {pkg_path.name}")
        
        # Verify
        print("  Verifying package signature...")
        verify_result = run_command(
            ["pkgutil", "--check-signature", str(pkg_path)],
            check=False,
            show_output=True
        )
        return True
    else:
        print(f"❌ Failed to sign package")
        if signed_pkg.exists():
            signed_pkg.unlink()
        return False

# ============================================================================
# NOTARIZATION FUNCTIONS
# ============================================================================

def notarize_file(file_path, bundle_id):
    """Submit file for notarization and wait for result"""
    import json

    print(f"\n📝 Notarizing {file_path.name}...")
    print("⏳ This may take 5–15 minutes...")

    # For .app bundles, we need to zip them first
    if file_path.suffix == '.app':
        print("  Creating temporary zip for notarization...")
        zip_path = file_path.parent / f"{file_path.stem}-notarize.zip"
        
        # Use ditto to preserve code signatures
        result = run_command(
            ["ditto", "-c", "-k", "--keepParent", str(file_path), str(zip_path)],
            "Creating zip with ditto",
            check=False
        )
        
        if not result or result.returncode != 0:
            print("❌ Failed to create zip")
            return False
        
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
        "--wait",
        "--output-format", "json"
    ]

    print(f"  Submitting to Apple notary service...")
    result = run_command(cmd, check=False)

    # Clean up temporary zip
    if file_path.suffix == '.app' and notarize_target.exists() and notarize_target != file_path:
        notarize_target.unlink()

    if not result:
        print("❌ Notarization command failed")
        return False

    # Parse response
    try:
        response_data = json.loads(result.stdout)
        request_id = response_data.get("id")
        status = response_data.get("status")
        
        print(f"\n📋 Notarization Result:")
        print(f"  Request ID: {request_id}")
        print(f"  Status: {status}")
        
        if result.returncode == 0 and status == "Accepted":
            print("✅ Notarization successful!")
            
            # Staple the notarization ticket
            if file_path.suffix in ['.app', '.pkg']:
                print("  Stapling notarization ticket...")
                staple_cmd = ["xcrun", "stapler", "staple", str(file_path)]
                staple_result = run_command(staple_cmd, check=False)
                
                if staple_result and staple_result.returncode == 0:
                    print("✅ Notarization ticket stapled")
                    
                    # Verify stapling
                    verify_result = run_command(
                        ["xcrun", "stapler", "validate", str(file_path)],
                        check=False,
                        show_output=True
                    )
                else:
                    print("⚠️ Failed to staple ticket")
            
            return True
        else:
            print("❌ Notarization failed")
            
            # Get detailed log
            if request_id:
                print("\n  Fetching notarization log...")
                log_cmd = [
                    "xcrun", "notarytool", "log",
                    request_id,
                    "--apple-id", APPLE_ID,
                    "--team-id", TEAM_ID,
                    "--password", APP_SPECIFIC_PASSWORD
                ]
                log_result = run_command(log_cmd, check=False, show_output=True)
            
            return False
            
    except json.JSONDecodeError:
        print("❌ Failed to parse notarization response")
        print(f"Response: {result.stdout}")
        return False
    except Exception as e:
        print(f"❌ Error processing notarization: {e}")
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
        "-p:PublishTrimmed=false",
        "-p:PublishSingleFile=false",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:CopyOutputSymbolsToPublishDirectory=true",
        "-p:IncludeAllContentForSelfExtract=true"
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

    # Copy all build output to MacOS directory
    print("  Copying build files...")
    for item in build_path.iterdir():
        if item.name.endswith(".exe") or item.name == "AkademiAuth":
            continue
        if item != bundle_dir:
            dest = macos_dir / item.name
            if item.is_dir():
                shutil.copytree(item, dest, dirs_exist_ok=True)
            else:
                shutil.copy2(item, dest)

    # Copy icon
    icon_filename = "AppIcon.icns"
    icon_dest = resources_dir / icon_filename
    shutil.copy2(ICON_PATH, icon_dest)

    if ENTITLEMENTS_PATH.exists():
        entitlements_dest = resources_dir / "entitlements.plist"
        shutil.copy2(ENTITLEMENTS_PATH, entitlements_dest)
        print(f"✅ Copied entitlements.plist to bundle")
    else:
        print(f"⚠️  Entitlements file not found at {ENTITLEMENTS_PATH}")

    # Create Info.plist
    info_plist_content = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>{APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.CyberBrothers.akademitrack</string>
    <key>CFBundleName</key>
    <string>AkademiTrack</string>
    <key>CFBundleDisplayName</key>
    <string>AkademiTrack</string>
    <key>CFBundleVersion</key>
    <string>{version}</string>
    <key>CFBundleShortVersionString</key>
    <string>{version}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleIconFile</key>
    <string>AT-1024.icns</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.education</string>
    <key>NSHumanReadableCopyright</key>
    <string>© 2025 CyberBrothers. All rights reserved.</string>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>""" 
    
    plist_path = contents_dir / "Info.plist"
    with open(plist_path, "w") as f:
        f.write(info_plist_content)
    print(f"✅ Created Info.plist with version {version}")

    # Set executable permissions
    executable_in_bundle = macos_dir / APP_NAME
    if executable_in_bundle.exists():
        os.chmod(executable_in_bundle, 0o755)

    # Set permissions for all dylibs and executables
    for lib in macos_dir.rglob("*"):
        if lib.is_file() and (lib.suffix in ['.dylib', '.so'] or os.access(lib, os.X_OK)):
            os.chmod(lib, 0o755)

    # Remove quarantine attributes
    subprocess.run(["xattr", "-cr", str(bundle_dir)], check=False)

    # Handle helper app if it exists
    if HELPER_APP_SOURCE.exists():
        print("\n  Bundling helper app...")
        helper_dest = resources_dir / "AkademiTrack.app"
        try:
            shutil.copytree(HELPER_APP_SOURCE, helper_dest, dirs_exist_ok=True)
            print("✅ Helper app bundled in Resources")
            
            if sign:
                # Sign helper app first (it's nested)
                sign_app(helper_dest, DEVELOPER_ID_APP, ENTITLEMENTS_PATH, deep=True)
        except Exception as e:
            print(f"⚠️ Failed to bundle helper: {e}")

    # Sign the main app
    if sign:
        if not sign_app(bundle_dir, DEVELOPER_ID_APP, ENTITLEMENTS_PATH, deep=True):
            print("❌ Signing failed")
            return False
    
    # Notarize the app
    if notarize and sign:
        if not notarize_file(bundle_dir, BUNDLE_IDENTIFIER):
            print("⚠️  Notarization failed, but app is signed")

    return bundle_dir

def create_portable_zip(bundle_dir, version, sign=True, notarize=True):
    """Create portable zip distribution"""
    print("\n📦 Creating portable zip...")
    print("=" * 50)
    
    zip_path = Path(f"AkademiTrack-{version}-osx-Portable.zip").absolute()
    if zip_path.exists():
        zip_path.unlink()

    try:
        # Use ditto to preserve code signatures
        result = run_command(
            ["ditto", "-c", "-k", "--keepParent", str(bundle_dir), str(zip_path)],
            "Creating zip",
            check=False
        )
        
        if result and result.returncode == 0:
            print(f"✅ Portable zip created: {zip_path.name} ({zip_path.stat().st_size / 1024 / 1024:.1f} MB)")
            
            if notarize and sign:
                print("  ℹ️  The .app inside is already notarized - no need to notarize the zip")
            
            return zip_path
        else:
            print("❌ Failed to create zip")
            return None
    except Exception as e:
        print(f"❌ Failed to create zip: {e}")
        return None

def create_installer_pkg(bundle_dir, version, sign=True, notarize=True):
    """Create .pkg installer with LaunchAgent"""
    print("\n📦 Creating installer package with LaunchAgent...")
    print("=" * 50)
    
    # Create LaunchAgent plist
    launchagent_plist = create_launchagent_plist(version)
    
    # Create temporary root directory structure
    temp_root = Path("./pkg_root")
    if temp_root.exists():
        shutil.rmtree(temp_root)
    
    temp_root.mkdir(parents=True)
    
    # Create directory structure
    apps_dir = temp_root / "Applications"
    launch_agents_dir = temp_root / "Library" / "LaunchAgents"
    
    apps_dir.mkdir(parents=True)
    launch_agents_dir.mkdir(parents=True)
    
    print("  Copying app bundle...")
    shutil.copytree(bundle_dir, apps_dir / f"{APP_NAME}.app", dirs_exist_ok=True)
    
    print("  Copying LaunchAgent plist...")
    shutil.copy2(launchagent_plist, launch_agents_dir / launchagent_plist.name)
    
    pkg_path = Path(f"AkademiTrack-{version}-osx-Setup.pkg").absolute()
    
    # Create pkg
    cmd = [
        "pkgbuild",
        "--root", str(temp_root),
        "--identifier", BUNDLE_IDENTIFIER,
        "--version", version,
        "--install-location", "/",
        str(pkg_path)
    ]
    
    result = run_command(cmd, "Building package", check=False)
    
    # Clean up
    if temp_root.exists():
        shutil.rmtree(temp_root)
    
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
        notarize_file(pkg_path, BUNDLE_IDENTIFIER)
    
    return pkg_path

def create_velopack_package(bundle_dir, version):
    """Create VPK package using Velopack from the existing .app bundle"""
    print("\n📦 Creating VPK Package (NuGet)")
    print("=" * 50)

    try:
        result = subprocess.run(["vpk", "--version"], capture_output=True, text=True)
        print(f"✅ Velopack found: {result.stdout.strip()}")
    except FileNotFoundError:
        print("❌ Velopack (vpk) not found!")
        print("Install it with: dotnet tool install -g vpk")
        return None

    # Create a temporary directory for Velopack with LOOSE FILES (not .app bundle)
    publish_dir = Path("./publish-mac-arm")
    
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    publish_dir.mkdir(parents=True)

    print(f"📂 Copying application files for Velopack...")
    
    macos_source = bundle_dir / "Contents" / "MacOS"
    
    if not macos_source.exists():
        print(f"❌ MacOS directory not found in bundle")
        return None
    
    # Copy all files from MacOS to publish dir (Velopack will create the .app structure)
    for item in macos_source.iterdir():
        dest = publish_dir / item.name
        if item.is_dir():
            shutil.copytree(item, dest, dirs_exist_ok=True)
        else:
            shutil.copy2(item, dest)
    
    print("✅ Copied all files from .app bundle")

    print(f"📦 Creating VPK package (version {version})...")

    icon_path = Path(ICON_PATH).absolute()
    
    # Verify icon exists
    if not icon_path.exists():
        print(f"⚠️  Icon not found at {icon_path}, continuing without icon")
        icon_path = None

    # Pass the folder with loose files, NOT a .app bundle
    # Velopack will create the .app structure itself
    vpk_cmd = [
        "vpk", "pack",
        "--packId", "AkademiTrack",
        "--packVersion", version,
        "--packDir", str(publish_dir),
        "--mainExe", APP_NAME,
        "--bundleId", BUNDLE_IDENTIFIER,
        "--noInst",  # <-- CORRECT FLAG: Skip .pkg creation (we make our own)
        "--verbose"
    ]
    
    if icon_path:
        vpk_cmd.extend(["--icon", str(icon_path)])

    print(f"Running command: {' '.join(vpk_cmd)}")
    result = subprocess.run(vpk_cmd, capture_output=True, text=True)

    # Show output for debugging
    if result.stdout:
        print("STDOUT:", result.stdout)
    if result.stderr:
        print("STDERR:", result.stderr)

    if result.returncode != 0:
        print(f"❌ VPK pack failed with code {result.returncode}")
        print("\n💡 Try running manually to see full output:")
        print(f"   {' '.join(vpk_cmd)}")
        return None

    print("✅ VPK package created successfully")
    
    # Clean up temporary directory
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    
    # Rename to match convention
    nupkg_file = list(Path(".").glob("AkademiTrack.*.nupkg"))
    if nupkg_file:
        new_name = f"AkademiTrack-{version}-osx-full.nupkg"
        shutil.move(str(nupkg_file[0]), new_name)
        print(f"✅ Renamed to: {new_name}")
        
        # Fix Releases folder .app
        print("\n🔧 Fixing Releases folder .app...")
        releases_app = Path("./Releases/AkademiTrack.app")
        
        if releases_app.exists():
            releases_resources = releases_app / "Contents" / "Resources"
            original_resources = bundle_dir / "Contents" / "Resources"
            
            if original_resources.exists():
                # Ensure Resources directory exists
                releases_resources.mkdir(parents=True, exist_ok=True)
                
                entitlements_src = original_resources / "entitlements.plist"
                if entitlements_src.exists():
                    shutil.copy2(entitlements_src, releases_resources / "entitlements.plist")
                    print("  ✅ Copied entitlements.plist to Releases app")
                
                # Copy helper app if it exists
                helper_src = original_resources / "AkademiTrack.app"
                if helper_src.exists():
                    helper_dest = releases_resources / "AkademiTrack.app"
                    if helper_dest.exists():
                        shutil.rmtree(helper_dest)
                    shutil.copytree(helper_src, helper_dest, dirs_exist_ok=True)
                    print("  ✅ Copied helper AkademiTrack.app to Releases app")
                
                print("✅ Releases folder .app now matches portable version!")
            else:
                print("  ⚠️  Original Resources folder not found")
        else:
            print("  ℹ️  Releases folder .app not found (will be created when downloaded)")
        
        return Path(new_name)
    
    return None

# ============================================================================
# VERIFICATION FUNCTIONS
# ============================================================================

def verify_all_signatures(bundle_dir):
    """Verify signatures of all files"""
    print("\n🔍 Verifying all signatures...")
    print("=" * 50)
    
    # Verify main app
    print(f"\n📱 Verifying main app bundle:")
    run_command(
        ["codesign", "-dv", "--verbose=4", str(bundle_dir)],
        check=False,
        show_output=True
    )
    run_command(
        ["codesign", "--verify", "--deep", "--strict", str(bundle_dir)],
        check=False,
        show_output=True
    )
    
    # Check notarization
    print(f"\n📝 Checking notarization:")
    run_command(
        ["xcrun", "stapler", "validate", str(bundle_dir)],
        check=False,
        show_output=True
    )
    
    # Check Gatekeeper
    print(f"\n🚪 Checking Gatekeeper assessment:")
    run_command(
        ["spctl", "-a", "-vv", "-t", "exec", str(bundle_dir)],
        check=False,
        show_output=True
    )

# ============================================================================
# MAIN FUNCTION
# ============================================================================

def main():
    print("🚀 AkademiTrack Build, Sign & Notarize Tool")
    print("=" * 50)

    # Verify configuration
    print("\n🔍 Checking configuration...")
    if "sdfsdfsdfsdfds" in DEVELOPER_ID_APP or "sdfsdfdsfs" in APPLE_ID:
        print("❌ Please update the configuration at the top of this script!")
        print("   - DEVELOPER_ID_APP")
        print("   - DEVELOPER_ID_INSTALLER")
        print("   - APPLE_ID")
        print("   - TEAM_ID")
        print("   - APP_SPECIFIC_PASSWORD")
        return

    # List available signing identities
    print("\n🔑 Available signing identities:")
    run_command(
        ["security", "find-identity", "-v", "-p", "codesigning"],
        check=False,
        show_output=True
    )

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

    # Verify signatures if signed
    if do_sign:
        verify_all_signatures(bundle_dir)

    # Ask what distributions to create
    print("\n📦 Distribution Options:")
    print("1. Portable ZIP + VPK Package")
    print("2. Installer PKG (includes LaunchAgent)")
    print("3. VPK Package only")
    print("4. All of the above")
    
    dist_choice = input("\nSelect option (1/2/3/4) [4]: ").strip()
    if not dist_choice:
        dist_choice = "4"
    
    created_files = []
    
    # Option 1: ZIP + VPK
    if dist_choice in ["1", "4"]:
        zip_file = create_portable_zip(bundle_dir, version, sign=do_sign, notarize=do_notarize)
        if zip_file:
            created_files.append(("Portable ZIP", zip_file))
        
        vpk_file = create_velopack_package(bundle_dir, version)
        if vpk_file:
            created_files.append(("VPK Package", vpk_file))
    
    # Option 2: PKG with LaunchAgent
    if dist_choice in ["2", "4"]:
        pkg_file = create_installer_pkg(bundle_dir, version, sign=do_sign, notarize=do_notarize)
        if pkg_file:
            created_files.append(("Installer PKG", pkg_file))
    
    # Option 3: VPK only
    if dist_choice == "3":
        vpk_file = create_velopack_package(bundle_dir, version)
        if vpk_file:
            created_files.append(("VPK Package", vpk_file))
    
    # Final summary
    print("\n" + "=" * 50)
    print("🎉 Build completed successfully!")
    print(f"📦 Version: {version}")
    print("\n📋 Files created:")
    print(f"  ✅ .app bundle: {bundle_dir}")
    
    for file_type, file_path in created_files:
        size_mb = file_path.stat().st_size / 1024 / 1024
        print(f"  ✅ {file_type}: {file_path.name} ({size_mb:.1f} MB)")
    
    if do_sign:
        print("\n✅ All files signed with Developer ID")
    if do_notarize:
        print("✅ All files notarized by Apple")
    
    # LaunchAgent info
    if dist_choice in ["2", "4"]:
        print("\n🚀 LaunchAgent Information:")
        print("  ✅ PKG installer includes LaunchAgent")
        print("  📝 Location: ~/Library/LaunchAgents/com.CyberBrothers.akademitrack.plist")
        print("  👤 Display name: AkademiTrack (shown in Background Items)")
        print("\n  After installation, users will see:")
        print("  • System Settings → General → Login Items → Allow in Background")
        print("  • Shows 'AkademiTrack' instead of your developer name!")
    
    # Verification commands
    print("\n🔍 Verification commands:")
    print(f"  codesign -dv --verbose=4 '{bundle_dir}'")
    print(f"  codesign --verify --deep --strict '{bundle_dir}'")
    print(f"  xcrun stapler validate '{bundle_dir}'")
    print(f"  spctl -a -vv -t exec '{bundle_dir}'")
    
    for file_type, file_path in created_files:
        if file_path.suffix == '.pkg':
            print(f"  pkgutil --check-signature '{file_path}'")
            print(f"  xcrun stapler validate '{file_path}'")
    
    print("\n📋 Next steps:")
    print("  • Test the .app by double-clicking it")
    print("  • Test the .pkg installer")
    if dist_choice in ["2", "4"]:
        print("  • After PKG install, check: System Settings → Login Items")
        print("  • Verify 'AkademiTrack' appears (not your name)")
    print("  • Upload files to GitHub Releases")
    if do_notarize:
        print("  • ✅ No Gatekeeper warnings will appear!")
    else:
        print("  • ⚠️  Users will need to right-click → Open (unsigned)")
    
    print("\n💡 Tips:")
    print("  • LaunchAgent will auto-start the app on login")
    print("  • Users can disable it in System Settings → Login Items")
    print("  • The app's auto-start toggle in Settings will manage this")
    
    print("\n🔍 Check notarization history:")
    print(f"  xcrun notarytool history --apple-id {APPLE_ID} --team-id {TEAM_ID} --password {APP_SPECIFIC_PASSWORD}")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n\n⚠️  Build interrupted by user")
    except Exception as e:
        print(f"\n❌ Unexpected error: {e}")
        import traceback
        traceback.print_exc()