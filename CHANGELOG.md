# Changelog

All notable changes to AkademiTrack will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Planned
- Fix for Linux auto-start functionality
- Additional notification customization options
- Enhanced error recovery mechanisms

## [1.0.0] - 5-10-2025

### Added
- **Encrypted Credential Storage** - Secure storage of Feide username, password, and school name
- **First-Time Setup Flow** - User-friendly configuration wizard for initial setup
- **Automatic Feide Login** - Automated authentication using Selenium WebDriver
- **STU Session Detection** - Intelligent identification of STU sessions from daily schedule
- **Class Conflict Detection** - Automatically identifies and skips STU sessions that overlap with regular classes
- **Automatic Attendance Registration** - Registers attendance when registration windows open
- **Real-Time Monitoring** - Efficient monitoring of registration windows
- **Activity Logging System** - Comprehensive logging with INFO, WARNING, ERROR, and DEBUG levels
- **Detailed Logging Mode** - Optional verbose logging for troubleshooting
- **System Notifications** - Queue-based notification system with priority handling
- **Settings Window** - Configurable preferences for logging, automation, and updates
- **Auto-Start Automation** - Optional automatic start of monitoring when app launches
- **Start with System** - Optional launch at system startup (Windows/macOS)
- **Automatic Update Checking** - Checks for new versions every 30 minutes
- **Cross-Platform Support** - Windows, macOS, and Linux compatibility
- **Modern UI** - Clean Avalonia-based user interface
- **Tutorial System** - Built-in guidance for first-time users

### Technical
- Built with C# and .NET
- Avalonia UI framework for cross-platform desktop support
- Selenium WebDriver for secure Feide authentication
- MVVM architecture pattern
- Efficient API usage (one schedule fetch per day)
- Encrypted local credential storage

### Known Issues
- **Linux Auto-Start Bug** - "Start with system" functionality does not work on Linux

### Security
- All credentials encrypted before storage using industry-standard encryption
- No data transmitted to external servers except iSkole/Feide
- No telemetry or usage tracking
- Open source for community verification

## Contributing

When contributing, please update this changelog with your changes under the `[Unreleased]` section using the appropriate category:

- **Added** for new features
- **Changed** for changes in existing functionality
- **Deprecated** for soon-to-be removed features
- **Removed** for now removed features
- **Fixed** for any bug fixes
- **Security** for vulnerability fixes

[Unreleased]: https://github.com/CyberGutta/AkademiTrack/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/CyberGutta/AkademiTrack/releases/tag/v1.0.0
