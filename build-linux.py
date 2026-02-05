#!/usr/bin/env python3
import os
import shutil
import subprocess
import tarfile
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

def create_desktop_file(version, install_dir):
    """Create .desktop file for Linux"""
    desktop_content = f"""[Desktop Entry]
Version={version}
Type=Application
Name=AkademiTrack
Comment=Academic tracking and management application
Exec={install_dir}/AkademiTrack
Icon={install_dir}/akademitrack
Terminal=false
Categories=Education;Office;
StartupWMClass=AkademiTrack
"""
    return desktop_content

def build_linux_release(version):
    """Build Linux release - creates portable tarball and standalone binary"""
    
    print(f"\n🏗️  Building AkademiTrack for Linux (x64)...")
    print("=" * 50)
    
    # Directories
    publish_dir = Path("./publish-linux")
    publish_single = Path("./publish-linux-single")
    release_folder = Path(f"./Releases/v{version}")
    
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
    
    # Step 1: Publish for distribution (multi-file)
    print(f"\n📦 Step 1: Publishing for distribution (multi-file)...")
    publish_cmd = [
        "dotnet", "publish",
        "-c", "Release",
        "--self-contained",
        "-r", "linux-x64",
        "-o", str(publish_dir),
        "-p:PublishSingleFile=false"
    ]
    
    print(f"Running: {' '.join(publish_cmd)}")
    result = subprocess.run(publish_cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"❌ Publish failed: {result.stderr}")
        return False
    
    print("✅ Published for distribution successfully")
    
    # Step 2: Publish single file binary (for standalone distribution)
    print(f"\n📦 Step 2: Publishing standalone single-file binary...")
    publish_single_cmd = [
        "dotnet", "publish",
        "-c", "Release",
        "--self-contained",
        "-r", "linux-x64",
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
    
    print("✅ Published single-file binary successfully")
    
    # Verify single-file executable exists
    binary_single = publish_single / "AkademiTrack"
    if not binary_single.exists():
        print(f"❌ Single-file binary not found: {binary_single}")
        return False
    
    # Set executable permission
    os.chmod(binary_single, 0o755)
    
    binary_size = binary_single.stat().st_size / 1024 / 1024
    print(f"✅ Single-file binary: {binary_single.name} ({binary_size:.1f} MB)")
    
    if binary_size < 10:
        print(f"⚠️  Warning: Binary seems too small ({binary_size:.1f} MB)")
    
    # Step 3: Create portable tarball from multi-file build
    print(f"\n📦 Step 3: Creating portable tarball...")
    portable_tar = release_folder / f"AkademiTrack-linux-Portable.tar.gz"
    
    try:
        with tarfile.open(portable_tar, 'w:gz') as tar:
            file_count = 0
            for file_path in publish_dir.rglob('*'):
                if file_path.is_file():
                    arc_name = f"AkademiTrack/{file_path.relative_to(publish_dir)}"
                    tar.add(file_path, arcname=arc_name)
                    file_count += 1
            
            # Add desktop file to tarball
            desktop_content = create_desktop_file(version, "/opt/akademitrack")
            import tempfile
            with tempfile.NamedTemporaryFile(mode='w', suffix='.desktop', delete=False) as f:
                f.write(desktop_content)
                desktop_temp = f.name
            
            tar.add(desktop_temp, arcname="AkademiTrack/akademitrack.desktop")
            os.unlink(desktop_temp)
            file_count += 1
            
            print(f"✅ Added {file_count} files to portable tarball")
    except Exception as e:
        print(f"❌ Failed to create portable tarball: {e}")
        return False
    
    portable_size = portable_tar.stat().st_size / 1024 / 1024
    print(f"✅ Portable tarball created: {portable_tar.name} ({portable_size:.1f} MB)")
    
    # Step 4: Create Velopack release package
    print(f"\n📦 Step 4: Creating Velopack release package...")
    try:
        # Create releases directory
        releases_dir = Path("./Releases")
        releases_dir.mkdir(exist_ok=True)
        
        # Use vpk to pack the app
        vpk_cmd = [
            "vpk", "pack",
            "--packId", "AkademiTrack",
            "--packVersion", version,
            "--packDir", str(publish_dir),
            "--outputDir", str(releases_dir),
            "--runtime", "linux-x64",
            "--mainExe", "AkademiTrack"
        ]
        
        # Add icon if it exists (look for PNG version for Linux)
        icon_png = Path("./Assets/AT-1024.png")
        if icon_png.exists():
            vpk_cmd.extend(["--icon", str(icon_png)])
        
        print(f"Running: {' '.join(vpk_cmd)}")
        result = subprocess.run(vpk_cmd, capture_output=True, text=True)
        
        if result.returncode == 0:
            # Find the created release file
            release_files = list(releases_dir.glob(f"AkademiTrack-{version}-*.nupkg"))
            if release_files:
                release_file = release_files[0]
                velopack_size = release_file.stat().st_size / 1024 / 1024
                print(f"✅ Velopack release created: {release_file.name} ({velopack_size:.1f} MB)")
                
                # Copy to release folder
                shutil.copy2(release_file, release_folder / release_file.name)
                
                # Also copy RELEASES file if it exists
                releases_file = releases_dir / "RELEASES"
                if releases_file.exists():
                    shutil.copy2(releases_file, release_folder / "RELEASES")
                    print(f"✅ RELEASES file copied")
            else:
                print("⚠️  Velopack release file not found")
        else:
            print(f"⚠️  Velopack packaging failed: {result.stderr}")
            print("💡 Make sure 'vpk' tool is installed: dotnet tool install -g vpk")
    except Exception as e:
        print(f"⚠️  Velopack packaging failed: {e}")
        print("💡 Make sure 'vpk' tool is installed: dotnet tool install -g vpk")
    
    # Step 5: Copy the standalone single-file binary to release folder
    print(f"\n📦 Step 5: Adding standalone single-file binary...")
    standalone_binary = release_folder / "AkademiTrack"
    shutil.copy2(binary_single, standalone_binary)
    os.chmod(standalone_binary, 0o755)
    standalone_size = standalone_binary.stat().st_size / 1024 / 1024
    print(f"✅ Standalone single-file binary: {standalone_binary.name} ({standalone_size:.1f} MB)")
    
    # Step 6: Create install script
    print(f"\n📦 Step 6: Creating install script...")
    install_script = release_folder / "install.sh"
    install_content = f"""#!/bin/bash
# AkademiTrack Linux Installation Script

INSTALL_DIR="/opt/akademitrack"
DESKTOP_FILE="/usr/share/applications/akademitrack.desktop"
BIN_LINK="/usr/local/bin/akademitrack"

echo "🚀 Installing AkademiTrack v{version}..."

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo "❌ Please run as root (use sudo)"
    exit 1
fi

# Create install directory
echo "📁 Creating installation directory..."
mkdir -p "$INSTALL_DIR"

# Extract files
echo "📦 Extracting files..."
tar -xzf AkademiTrack-linux-Portable.tar.gz -C /opt/
mv /opt/AkademiTrack/* "$INSTALL_DIR/"
rmdir /opt/AkademiTrack

# Set permissions
echo "🔐 Setting permissions..."
chmod +x "$INSTALL_DIR/AkademiTrack"

# Create desktop entry
echo "🖥️  Creating desktop entry..."
cat > "$DESKTOP_FILE" << 'EOF'
[Desktop Entry]
Version={version}
Type=Application
Name=AkademiTrack
Comment=Academic tracking and management application
Exec=/opt/akademitrack/AkademiTrack
Icon=/opt/akademitrack/akademitrack
Terminal=false
Categories=Education;Office;
StartupWMClass=AkademiTrack
EOF

# Create symlink
echo "🔗 Creating symlink..."
ln -sf "$INSTALL_DIR/AkademiTrack" "$BIN_LINK"

# Update desktop database
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database /usr/share/applications
fi

echo "✅ Installation complete!"
echo "📋 You can now:"
echo "  • Run 'akademitrack' from the terminal"
echo "  • Launch from your application menu"
echo ""
echo "💡 To uninstall, run: sudo rm -rf $INSTALL_DIR $DESKTOP_FILE $BIN_LINK"
"""
    
    with open(install_script, 'w') as f:
        f.write(install_content)
    os.chmod(install_script, 0o755)
    print(f"✅ Install script created: {install_script.name}")
    
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
    print("🚀 AkademiTrack Linux Build & Package Tool")
    print("=" * 50)
    
    # Get version number
    version = get_version_input()
    print(f"\n📌 Using version: {version}")
    
    # Ask if user wants to update .csproj
    update_proj = input("\nUpdate version in .csproj file? (y/n) [n]: ").strip().lower()
    if update_proj == 'y':  # Only updates if you type 'y'
        update_csproj_version(version)
    
    # Build everything
    release_folder = build_linux_release(version)
    
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
        print(f"  • AkademiTrack - Standalone single-file binary (RUN THIS ONE!)")
        print(f"  • AkademiTrack-linux-Portable.tar.gz - Portable tarball package")
        print(f"  • AkademiTrack-{version}-*.nupkg - Velopack release package (for auto-updates)")
        print(f"  • RELEASES - Velopack releases index file")
        print(f"  • install.sh - Installation script (sudo ./install.sh)")
        
        print("\n💡 Next steps:")
        print(f"  • Test standalone binary: ./{release_folder}/AkademiTrack")
        print(f"  • Or install system-wide: cd {release_folder} && sudo ./install.sh")
        print(f"  • Distribute the tarball for manual installs")
        print(f"  • Use the .nupkg + RELEASES for Velopack auto-updates")
        
        print("\n🔧 Installation options:")
        print("  1. Standalone: Just run ./AkademiTrack")
        print("  2. System-wide: sudo ./install.sh (installs to /opt/akademitrack)")
        print("  3. Extract tarball: tar -xzf AkademiTrack-linux-Portable.tar.gz")
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