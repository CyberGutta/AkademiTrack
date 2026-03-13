# Changelog

All notable changes to AkademiTrack will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Planned
- Widgets for mac
- Enhanced error recovery mechanisms

## [1.2.0] - 2026-02-08

### Added
- **Real-Time Dashboard** - New dashboard view showing today's STU sessions, registration status, and attendance overview at a glance
- **Weekly Attendance Tracker** - Visual breakdown of attendance by day with completion percentages and color-coded status indicators
- **Monthly Statistics** - Track your monthly attendance rate and total registered sessions
- **Next Class Widget** - Shows your current or upcoming class with time, room number, and subject information
- **Overtime/Undertime Tracking** - Monitor your attendance balance with color-coded status (green/orange/red)
- **Smart Caching** - Instant dashboard loading with background data refresh for better performance
- **Persistent Cache System** - 24-hour TTL cache with automatic cleanup reduces API calls and improves load times
- **Health Diagnostics** - Built-in system health checks and troubleshooting tools in settings
- **Enhanced Logging** - Comprehensive activity logs with optional detailed debug mode
- **Priority Notifications** - Queue-based notification system with importance levels

### Changed
- **Optimized Data Loading** - Parallel API requests with individual timeouts for faster data fetching
- **Enhanced Error Recovery** - Automatic retry mechanisms with exponential backoff for failed requests
- **Improved Authentication** - Better handling of expired cookies with automatic re-authentication
- **Better Conflict Detection** - Improved logic for detecting STU sessions that overlap with regular classes
- **UI Improvements** - Better visual feedback and status indicators throughout the app

### Added
- **Sleep Detection** - Automatically refreshes data when system wakes from sleep
- **Network Status Handling** - Better detection and messaging when not connected to school WiFi
- **Memory Management** - Reduced memory footprint with proper resource disposal

### Fixed
- Threading issue with tray icon disposal on app exit
- Various stability improvements and bug fixes

### Technical
- Code quality improvements with dependency injection and better separation of concerns
- Refactored services architecture
- Better resource management and cleanup

## [1.1.0] - No real date

### Note
- **Test Release** - Testing version with experimental features

## [1.0.0] - 2025-10-05

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

[Unreleased]: https://github.com/CyberGutta/AkademiTrack/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/CyberGutta/AkademiTrack/compare/v1.0.0...v1.2.0
[1.1.0]: https://github.com/CyberGutta/AkademiTrack/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/CyberGutta/AkademiTrack/releases/tag/v1.0.0
