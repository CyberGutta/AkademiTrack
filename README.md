<div align="center">
<img width="124" height="124" alt="AT-1024" src="https://github.com/user-attachments/assets/5dba8bdc-b59f-4431-84c5-e797f3352295" />
</div>

<div align="center">
   
**Automated attendance registration for STU sessions at Akademiet schools**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Version](https://img.shields.io/badge/version-1.2.0-blue.svg)](https://github.com/CyberGutta/AkademiTrack/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg)](https://github.com/CyberGutta/AkademiTrack/releases)

[Features](#features) • [Installation](#installation) • [Quick Start](#quick-start) • [Documentation](#documentation) • [Support](#support)

</div>

---

## 📋 Overview

AkademiTrack is a desktop automation tool designed for students at Akademiet schools in Norway. It automatically monitors your schedule and registers attendance for **STU** (Selvstendige Terminoppgaver / Independent Term Projects) sessions when registration windows open, eliminating the need to manually track and register throughout the day.

<img width="598" height="433" alt="image" src="https://github.com/user-attachments/assets/44306565-d9ec-4bd2-af33-0284913afd44" />


---

## ✨ What's New in v1.2.0

### 🚀 Major Features

- **Dashboard View** - Real-time overview of today's STU sessions, weekly/monthly attendance, and overtime tracking
- **Next Class Display** - Shows your current or next scheduled class with room information
- **Weekly Attendance Tracker** - Visual breakdown of attendance by day with percentage tracking
- **Monthly Statistics** - Track your monthly attendance rate and registered sessions
- **Overtime/Undertime Tracking** - Monitor your attendance balance with color-coded status
- **Smart Caching** - Instant dashboard loading with background refresh for better performance
- **Sleep Detection** - Automatically refreshes data when system wakes from sleep
- **Enhanced Notifications** - Priority-based notification queue with system integration

### 🔧 Technical Improvements

- **Persistent Cache System** - 24-hour TTL cache with automatic cleanup
- **Optimized Data Loading** - Parallel API requests with individual timeouts
- **Better Error Recovery** - Automatic retry mechanisms with exponential backoff
- **Improved Logging** - Comprehensive activity logs with debug mode
- **Health Diagnostics** - Built-in system health checks and troubleshooting tools

---

## 🎯 Features

### Core Functionality

- ✅ **Automatic Authentication** - Secure Feide login with encrypted credential storage using Selenium WebDriver
- 🔍 **Intelligent Session Detection** - Identifies STU sessions from your daily schedule
- ⚠️ **Conflict Detection** - Automatically skips sessions that overlap with regular classes
- ⚡ **Real-Time Registration** - Registers attendance the moment windows open
- 📊 **Efficient Monitoring** - Optimized API usage with smart caching

### Dashboard & Analytics

- 📈 **Live Dashboard** - Real-time view of today's STU sessions and registration status
- 📅 **Weekly Overview** - Visual breakdown of attendance by day with completion tracking
- 📊 **Monthly Statistics** - Track attendance rates and session counts
- ⏰ **Next Class Widget** - Shows current or upcoming class with time and room info
- ⚖️ **Overtime Tracking** - Monitor attendance balance with color-coded indicators
- 💾 **Smart Caching** - Instant load times with background data refresh

### User Experience

- 🚀 **Auto-Start Options** - Launch with system and/or begin automation automatically
- 🎯 **Start Minimized** - Option to start in system tray for unobtrusive operation
- 📝 **Activity Logging** - Comprehensive logs with optional detailed debug mode
- 🔔 **Priority Notifications** - Queue-based system notifications with importance levels
- 💻 **Cross-Platform** - Native support for Windows, macOS, and Linux
- 🌙 **Sleep Detection** - Automatically refreshes after system wake

### Maintenance & Security

- 🔐 **Encrypted Storage** - Industry-standard encryption for credentials (ProtectedData API)
- 🏥 **Health Diagnostics** - Built-in system health checks and troubleshooting
- 📤 **Data Export** - Export logs and settings for backup or support
- ⚙️ **Flexible Settings** - Customizable school hours, notification preferences, and automation options

---

## 📥 Installation

### Windows

**Option 1: Standalone Executable (Recommended)**
1. Download `AkademiTrack.exe` from [releases](https://github.com/CyberGutta/AkademiTrack/releases)
2. Run the single-file executable - no installation required!

**Option 2: Portable ZIP**
1. Download `AkademiTrack-win-Portable.zip` from [releases](https://github.com/CyberGutta/AkademiTrack/releases)
2. Extract to your preferred location
3. Run `AkademiTrack.exe`

### macOS

1. Download `AkademiTrack-osx-Setup.pkg` from [releases](https://github.com/CyberGutta/AkademiTrack/releases)
2. Install the package and launch from Applications
3. If macOS blocks the app, remove quarantine:
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

**Note:** ChromeDriver is automatically downloaded and managed by the app on first run.

---

## 🚀 Quick Start

### First-Time Setup

1. Launch AkademiTrack
2. Select your Akademiet school from the dropdown
3. Enter your Feide credentials (email and password)
4. Click **"Lagre og fortsett"** (Save and continue)
5. Your encrypted credentials are now saved for automatic login

### Usage

1. **Dashboard View** - See your attendance overview at a glance:
   - Today's STU sessions (registered/total)
   - Current or next class information
   - Weekly attendance breakdown
   - Monthly statistics
   - Overtime/undertime balance

2. Click **"Start automatisering"** to begin monitoring
3. AkademiTrack will automatically:
   - Log in to iSkole using Selenium WebDriver
   - Fetch and cache today's schedule
   - Identify STU sessions
   - Skip sessions with conflicts
   - Register attendance when windows open
   - Update dashboard in real-time
4. Monitor progress in the activity log
5. Receive system notifications for successful registrations

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
| **.NET Runtime** | .NET 9.0 (included in installers) |
| **Internet** | Stable connection required |
| **Storage** | ~200 MB free space |
| **Browser** | ChromeDriver (automatically managed by WebDriverManager) |
| **Account** | Valid iSkole account at an Akademiet school |

---

## 🔒 Security & Privacy

AkademiTrack takes your security seriously:

- 🏠 **Local Storage Only** - All data stored exclusively on your device
- 🔐 **Encrypted Credentials** - Uses System.Security.Cryptography.ProtectedData for Windows, Keychain for macOS
- 🔒 **Secure Authentication** - Selenium-based browser automation with official Feide SSO
- 📊 **No Telemetry** - No usage tracking or data collection (optional analytics service exists but is not active)
- ✅ **Official APIs** - Only communicates with iSkole and Feide servers
- 🔓 **Open Source** - Code available for community review and security audits

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

- [ ] Enhanced error recovery mechanisms
- [ ] Desktop widgets for quick status view

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
| **UI Framework** | Avalonia UI 11.3.9 |
| **Authentication** | Selenium WebDriver 4.28.0 with ChromeDriver |
| **Architecture** | MVVM pattern with Dependency Injection |
| **Security** | System.Security.Cryptography.ProtectedData (Windows), Keychain (macOS) |
| **Caching** | Custom TTL-based cache service with automatic cleanup |
| **Notifications** | OsNotifications 1.1.3 for native system notifications |
| **HTTP Client** | System.Net.Http with retry logic |

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
