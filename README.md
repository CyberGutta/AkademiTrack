# AkademiTrack

**Automated attendance registration for STU sessions at Akademiet schools**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Version](https://img.shields.io/github/v/release/CyberGutta/AkademiTrack)](https://github.com/CyberGutta/AkademiTrack/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/CyberGutta/AkademiTrack/releases)

<img width="738" alt="AkademiTrack Interface" src="https://github.com/user-attachments/assets/2abb0222-9737-48da-bdb8-b82df2c0c32d" />

## Overview

AkademiTrack is a desktop automation tool designed for students at Akademiet schools in Norway. It automatically monitors your schedule and registers attendance for STU (Selvstendige Terminoppgaver / Independent Term Projects) sessions when registration windows open, eliminating the need to manually track and register throughout the day.

## Features

### Core Functionality
- **Automatic Authentication** - Secure Feide login with encrypted credential storage
- **Intelligent Session Detection** - Identifies STU sessions from your daily schedule
- **Conflict Detection** - Automatically skips sessions that overlap with regular classes
- **Real-Time Registration** - Registers attendance the moment windows open
- **Efficient Monitoring** - Single API request per day, minimal resource usage

### User Experience
- **Auto-Start Options** - Launch with system and/or begin automation automatically
- **Activity Logging** - Comprehensive logs with optional debug mode for troubleshooting
- **System Notifications** - Discrete alerts for successful registrations and important events
- **Cross-Platform** - Native support for Windows, macOS, and Linux

### Maintenance
- **Automatic Updates** - Checks for new versions every 30 minutes
- **One-Click Updates** - Download and install updates with a single button
- **Configuration Management** - Flexible settings for personalized workflows

## Quick Start

### Installation

**Windows**
1. Download `AkademiTrack-windows.zip` from [releases](https://github.com/CyberGutta/AkademiTrack/releases/latest)
2. Extract and run `AkademiTrack.exe`

**macOS**
1. Download `AkademiTrack-macos.zip` from [releases](https://github.com/CyberGutta/AkademiTrack/releases/latest)
2. Extract and remove quarantine: `xattr -cr /path/to/AkademiTrack.app`
3. Run the application

**Linux**
1. Download `AkademiTrack-linux.zip` from [releases](https://github.com/CyberGutta/AkademiTrack/releases/latest)
2. Extract and make executable: `chmod +x ./AkademiTrack`
3. Run: `./AkademiTrack`

### First-Time Setup

1. Launch AkademiTrack
2. Select your Akademiet school from the dropdown
3. Enter your Feide credentials (email and password)
4. Click "Lagre og fortsett" (Save and continue)
5. Your encrypted credentials are now saved for automatic login

### Usage

1. Click "Start automatisering" to begin monitoring
2. AkademiTrack will automatically:
   - Log in to iSkole
   - Fetch today's schedule
   - Identify STU sessions
   - Skip sessions with conflicts
   - Register attendance when windows open
3. Monitor progress in the activity log
4. The application will notify you of successful registrations

## Documentation

- **[Wiki](https://github.com/CyberGutta/AkademiTrack/wiki)** - Comprehensive documentation
- **[Installation Guide](https://github.com/CyberGutta/AkademiTrack/wiki/Installation-Guide)** - Detailed setup instructions
- **[User Guide](https://github.com/CyberGutta/AkademiTrack/wiki/User-Guide)** - Complete usage documentation
- **[Troubleshooting](https://github.com/CyberGutta/AkademiTrack/wiki/Troubleshooting)** - Common issues and solutions
- **[FAQ](https://github.com/CyberGutta/AkademiTrack/wiki/FAQ)** - Frequently asked questions

## System Requirements

- **OS**: Windows 10/11, macOS 10.15+, or Linux (Ubuntu 20.04+, Fedora 35+)
- **Internet**: Stable connection required
- **Storage**: ~100 MB free space
- **Account**: Valid iSkole account at an Akademiet school

## Security & Privacy

AkademiTrack takes your security seriously:

- **Local Storage Only** - All data stored exclusively on your device
- **Encrypted Credentials** - Industry-standard encryption for stored passwords
- **No Telemetry** - Zero data collection or tracking
- **Official Authentication** - Uses Feide's official SSO system
- **Open Source** - Code available for community review

Read our [Security Policy](SECURITY.md) for vulnerability reporting.

## Known Issues

- **Linux Auto-Start Bug** - "Start with system" feature currently doesn't work on Linux (fix planned)

See [Issues](https://github.com/CyberGutta/AkademiTrack/issues) for current bugs and feature requests.

## Contributing

Contributions are welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md) before submitting pull requests.

### Development
```bash
# Clone repository
git clone https://github.com/CyberGutta/AkademiTrack.git
cd AkademiTrack

# Build and run
dotnet restore
dotnet build
dotnet run
```
See the Development Guide for detailed information.

## Support

- Documentation: Wiki
- Issues: GitHub Issues
- Discussions: GitHub Discussions
- Email: cyberbrothershq@gmail.com
- Website: cybergutta.github.io/AkademietTrack

## Technology Stack

- Language: C# (.NET 6+)
- UI Framework: Avalonia UI
- Authentication: Selenium WebDriver
- Updates: Velopack
- Architecture: MVVM pattern

## Roadmap

 - Fix Linux auto-start functionality
 - Enhanced notification customization
 - Improved error recovery
 - Performance optimizations
 - Multi-account support (under consideration)

See CHANGELOG.md for version history.

## License
This project is licensed under the MIT License - see LICENSE file for details.

## Disclaimer
AkademiTrack is designed to assist students with legitimate attendance registration. Users are responsible for ensuring their use complies with school policies and academic integrity standards. This is not an official Akademiet or iSkole product.

## Authors

- @CyberNilsen
- @CyberHansen

## Acknowledgments
Built for the Akademiet student community. Special thanks to all contributors and users providing feedback.

Note for Norwegian speakers: While the documentation is in English, the application interface is in Norwegian to match the iSkole system used at Akademiet schools.
RetryClaude does not have the ability to run the code it generates yet.
