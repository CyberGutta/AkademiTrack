
"""
AkademiTrack v1

Version: 1.0
License: MIT

Requirements:
- Python 3.7+
- PyQt5
- Selenium
- Requests
- Schedule
- WebDriver Manager
- psutil
- browser-cookie3

Usage:
    python main.py

"""

import sys
import os
import subprocess
from pathlib import Path

# Add current directory to Python path
current_dir = Path(__file__).parent.absolute()
sys.path.insert(0, str(current_dir))

def check_python_version():
    """Check if Python version is compatible"""
    if sys.version_info < (3, 7):
        print("❌ Python 3.7 or higher is required!")
        print(f"Current version: {sys.version}")
        print("Please upgrade Python and try again.")
        sys.exit(1)
    else:
        print(f"✅ Python version: {sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}")

def install_package(package):
    """Install a single package using pip"""
    try:
        print(f"📦 Installing {package}...")
        subprocess.check_call([
            sys.executable, "-m", "pip", "install", package, "--user", "--quiet"
        ])
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Failed to install {package}: {e}")
        return False

def check_and_install_requirements():
    """Check and install required packages"""
    required_packages = {
        'PyQt5': 'PyQt5',
        'requests': 'requests',
        'selenium': 'selenium',
        'webdriver_manager': 'webdriver-manager',
        'schedule': 'schedule',
        'psutil': 'psutil',
        'browser_cookie3': 'browser-cookie3'
    }
    
    missing_packages = []
    
    print("🔍 Checking required packages...")
    
    for import_name, pip_name in required_packages.items():
        try:
            __import__(import_name)
            print(f"✅ {pip_name}")
        except ImportError:
            print(f"❌ {pip_name} - Missing")
            missing_packages.append(pip_name)
    
    if missing_packages:
        print(f"\n📦 Installing {len(missing_packages)} missing packages...")
        
        failed_packages = []
        for package in missing_packages:
            if not install_package(package):
                failed_packages.append(package)
        
        if failed_packages:
            print(f"\n❌ Failed to install: {', '.join(failed_packages)}")
            print("\nPlease install them manually:")
            for package in failed_packages:
                print(f"  pip install {package}")
            return False
        else:
            print("✅ All packages installed successfully!")
    
    return True

def check_files():
    """Check if required files exist"""
    required_files = ['backend.py', 'gui.py']  # Remove 'manual.py' from required files
    missing_files = []
    
    for file in required_files:
        if not os.path.exists(file):
            missing_files.append(file)
    
    if missing_files:
        print(f"❌ Missing required files: {', '.join(missing_files)}")
        print("Please ensure all files are in the same directory:")
        print("- main.py")
        print("- backend.py")
        print("- gui.py")
        # Remove manual.py reference
        return False
    
    return True

def main():
    """Main application launcher"""
    print("AkademiTrack v1")
    print("=" * 50)
    
    check_python_version()
    
    if not check_files():
        input("\nPress Enter to exit...")
        sys.exit(1)
    
    if not check_and_install_requirements():
        input("\nPress Enter to exit...")
        sys.exit(1)
    
    print("\n🚀 Starting application...")
    
    try:
        from gui import main as gui_main
        gui_main()
        
    except ImportError as e:
        print(f"❌ Import error: {e}")
        print("Please ensure all files are properly installed and in the same directory.")
        input("\nPress Enter to exit...")
        sys.exit(1)
        
    except KeyboardInterrupt:
        print("\n\n👋 Application interrupted by user")
        
    except Exception as e:
        print(f"❌ Unexpected error: {e}")
        print("\nIf this error persists, please check:")
        print("1. All required files are present")
        print("2. All dependencies are properly installed")
        print("3. You have internet connection")
        input("\nPress Enter to exit...")
        sys.exit(1)

if __name__ == "__main__":
    main()