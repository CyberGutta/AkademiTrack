<div align="center">
<img width="124" height="124" alt="AT-1024" src="https://github.com/user-attachments/assets/5dba8bdc-b59f-4431-84c5-e797f3352295" />
</div>

<div align="center">
   
**Automated attendance registration for STU sessions at Akademiet schools**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Version](https://img.shields.io/badge/version-1.1.0-blue.svg)](https://github.com/CyberGutta/AkademiTrack/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg)](https://github.com/CyberGutta/AkademiTrack/releases)

[Features](#features) • [Installation](#installation) • [Quick Start](#quick-start) • [Documentation](#documentation) • [Support](#support)

</div>

---

## 📋 Overview

AkademiTrack is a desktop automation tool designed for students at Akademiet schools in Norway. It automatically monitors your schedule and registers attendance for **STU** (Selvstendige Terminoppgaver / Independent Term Projects) sessions when registration windows open, eliminating the need to manually track and register throughout the day.

<img width="598" height="433" alt="image" src="https://github.com/user-attachments/assets/44306565-d9ec-4bd2-af33-0284913afd44" />


---

## ✨ What's New in v1.1.0

### 🚀 Major Improvements

- **Enhanced Browser Engine** - Upgraded from Selenium to Playwright for faster, more reliable authentication
- **Improved Performance** - Better resource management and reduced memory footprint
- **Enhanced Stability** - More robust error handling and recovery mechanisms
- **Faster Updates** - Streamlined update checking process
- **Better Logging** - Enhanced debugging capabilities for troubleshooting

### 🔧 Technical Upgrades

- **Upgraded to .NET 9.0** - Latest framework for improved performance and security
- **Modern Dependencies** - Updated all packages to latest stable versions
- **Optimized Authentication** - Faster and more reliable Feide login process

---

## 🎯 Features

### Core Functionality

- ✅ **Automatic Authentication** - Secure Feide login with encrypted credential storage
- 🔍 **Intelligent Session Detection** - Identifies STU sessions from your daily schedule
- ⚠️ **Conflict Detection** - Automatically skips sessions that overlap with regular classes
- ⚡ **Real-Time Registration** - Registers attendance the moment windows open
- 📊 **Efficient Monitoring** - Single API request per day, minimal resource usage

### User Experience

- 🚀 **Auto-Start Options** - Launch with system and/or begin automation automatically
- 📝 **Activity Logging** - Comprehensive logs with optional debug mode for troubleshooting
- 🔔 **System Notifications** - Discrete alerts for successful registrations and important events
- 💻 **Cross-Platform** - Native support for Windows, macOS, and Linux

### Maintenance

- 🔄 **Automatic Updates** - Checks for new versions every 10 minutes
- 📦 **One-Click Updates** - Download and install updates with a single button
- ⚙️ **Configuration Management** - Flexible settings for personalized workflows

---

## 📥 Installation

### Windows

1. Download `AkademiTrack-win-Setup.exe` from [releases](https://github.com/CyberGutta/AkademiTrack/releases)
2. Run the installer and follow the setup wizard

### macOS

1. Download `AkademiTrack-osx-Setup.pkg` from [releases](https://github.com/CyberGutta/AkademiTrack/releases)
2. Install the package and launch from Applications
3. If needed, remove quarantine:
   ```bash
   xattr -cr /Applications/AkademiTrack.app
   ```

### Linux

1. Download `AkademiTrack.AppImage` from [releases](https://github.com/CyberGutta/AkademiTrack/releases)
2. Make executable:
   ```bash
   chmod +x ./AkademiTrack.AppImage
   ```
3. Run:
   ```bash
   ./AkademiTrack.AppImage
   ```

---

## 🚀 Quick Start

### First-Time Setup

1. Launch AkademiTrack
2. Select your Akademiet school from the dropdown
3. Enter your Feide credentials (email and password)
4. Click **"Lagre og fortsett"** (Save and continue)
5. Your encrypted credentials are now saved for automatic login

### Usage

1. Click **"Start automatisering"** to begin monitoring
2. AkademiTrack will automatically:
   - Log in to iSkole using the improved browser engine
   - Fetch today's schedule
   - Identify STU sessions
   - Skip sessions with conflicts
   - Register attendance when windows open
3. Monitor progress in the activity log
4. The application will notify you of successful registrations

---

## 📚 Documentation

- 📖 [**Wiki**](https://github.com/CyberGutta/AkademiTrack/wiki) - Comprehensive documentation
- 🛠️ [**Installation Guide**](https://github.com/CyberGutta/AkademiTrack/wiki/Installation) - Detailed setup instructions
- 📘 [**User Guide**](https://github.com/CyberGutta/AkademiTrack/wiki/User-Guide) - Complete usage documentation
- 🔧 [**Troubleshooting**](https://github.com/CyberGutta/AkademiTrack/wiki/Troubleshooting) - Common issues and solutions
- ❓ [**FAQ**](https://github.com/CyberGutta/AkademiTrack/wiki/FAQ) - Frequently asked questions

---

## 💻 System Requirements

| Requirement | Specification |
|------------|---------------|
| **OS** | Windows 10/11, macOS 10.15+, or Linux (Ubuntu 20.04+, Fedora 35+) |
| **Internet** | Stable connection required |
| **Storage** | ~500 MB free space (increased due to browser engine) |
| **Account** | Valid iSkole account at an Akademiet school |

---

## 🔒 Security & Privacy

AkademiTrack takes your security seriously:

- 🏠 **Local Storage Only** - All data stored exclusively on your device
- 🔐 **Encrypted Credentials** - Industry-standard encryption for stored passwords
- 📊 **Minimal Telemetry** - Only essential error reporting and usage statistics
- ✅ **Official Authentication** - Uses Feide's official SSO system
- 🔓 **Open Source** - Code available for community review

Read our [Security Policy](SECURITY.md) for vulnerability reporting.

---

## ⚠️ Known Issues

- 🐧 **Linux Auto-Start Bug** - "Start with system" feature currently doesn't work on Linux (fix planned)

See [Issues](https://github.com/CyberGutta/AkademiTrack/issues) for current bugs and feature requests.

---

## 🛠️ Development

### Setup

```bash
# Clone repository
git clone https://github.com/CyberGutta/AkademiTrack.git
cd AkademiTrack

# Build and run
dotnet restore
dotnet build
dotnet run
```

### Contributing

Contributions are welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md) before submitting pull requests.

---

## 🗺️ Roadmap

- [ ] Improved error recovery
- [ ] Performance optimizations
- [ ] Widgets

See [CHANGELOG.md](CHANGELOG.md) for version history.

---

## 💬 Support

- 📖 **Documentation:** [Wiki](https://github.com/CyberGutta/AkademiTrack/wiki)
- 🐛 **Issues:** [GitHub Issues](https://github.com/CyberGutta/AkademiTrack/issues)
- 💭 **Discussions:** [GitHub Discussions](https://github.com/CyberGutta/AkademiTrack/discussions)
- 📧 **Email:** cyberbrothershq@gmail.com
- 🌐 **Website:** [cybergutta.github.io/AkademietTrack](https://cybergutta.github.io/AkademietTrack)

---

## 🔧 Technology Stack

| Component | Technology |
|-----------|-----------|
| **Language** | C# (.NET 9.0) |
| **UI Framework** | Avalonia UI |
| **Authentication** | Playwright (WebKit-based) |
| **Architecture** | MVVM pattern |
| **Security** | System.Security.Cryptography.ProtectedData |

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## ⚖️ Disclaimer

AkademiTrack is designed to assist students with legitimate attendance registration. Users are responsible for ensuring their use complies with school policies and academic integrity standards. **This is not an official Akademiet or iSkole product.**

---

## 👥 Authors

- [@CyberNilsen](https://github.com/CyberNilsen)
- [@CyberHansen](https://github.com/CyberHansen)

---

## 🙏 Acknowledgments

Built for the Akademiet student community. Special thanks to all contributors and users providing feedback.

> **Note for Norwegian speakers:** While the documentation is in English, the application interface is in Norwegian to match the iSkole system used at Akademiet schools.

---

<div align="center">

Made with ❤️ for Akademiet students

</div>
