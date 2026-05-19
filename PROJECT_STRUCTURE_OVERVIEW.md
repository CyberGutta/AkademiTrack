# AkademiTrack - Complete Project Structure Overview

## 📁 Services Folder (Backend Logic)

### 🔐 Authentication & Security

#### **AuthenticationService.cs**
- **Purpose**: Handles user authentication with iskole.net
- **Key Functions**: 
  - Authenticates using Feide credentials
  - Retrieves session cookies and user parameters (FylkeId, SkoleId, PlanPeri)
  - Uses Selenium WebDriver to automate login process
  - Caches credentials securely

#### **SecureCredentialStorage.cs**
- **Purpose**: Secure storage for sensitive data (passwords, cookies)
- **Platform-specific**:
  - macOS: Uses Keychain
  - Windows: Uses Windows Credential Manager
  - Linux: Uses encrypted file storage
- **Stores**: Username, password, cookies, user parameters

#### **KeychainService.cs**
- **Purpose**: macOS-specific keychain integration
- **Functions**: Save, load, and delete credentials from macOS Keychain
- **Security**: Handles hex encoding/decoding for special characters

#### **SecureAuthService.cs**
- **Purpose**: Platform-agnostic authentication wrapper
- **Functions**: Determines which auth method to use based on OS

#### **WindowsHelloAuthService.cs**
- **Purpose**: Windows Hello biometric authentication
- **Functions**: Uses Windows Hello for secure app access on Windows

#### **MacOSAuthService.cs**
- **Purpose**: macOS Touch ID/Face ID authentication
- **Functions**: Uses LocalAuthentication framework for biometric auth

#### **LinuxPinAuthService.cs**
- **Purpose**: PIN-based authentication for Linux
- **Functions**: Simple PIN verification for Linux systems

---

### 🤖 Automation & Core Logic

#### **AutomationService.cs**
- **Purpose**: Main automation engine - registers attendance automatically
- **Key Functions**:
  - Monitors school hours and checks for registration windows
  - Opens registration dialogs using Selenium
  - Clicks "Bekreft tilstedeværelse" button automatically
  - Runs on a timer during school hours
  - Handles session registration logic

#### **AttendanceDataService.cs**
- **Purpose**: Fetches attendance data from iskole.net
- **Key Functions**:
  - Gets today's schedule and STU sessions
  - Retrieves weekly attendance data
  - Fetches monthly statistics
  - Calculates overtime/undertime
  - Provides calendar data (removed but service still exists)
- **Caching**: Uses cache to reduce API calls

#### **SchoolTimeChecker.cs**
- **Purpose**: Determines if automation should run
- **Key Functions**:
  - Checks if current time is within school hours
  - Validates if it's a school day (Mon-Fri)
  - Handles manual start/stop flags
  - Prevents automation outside configured hours

#### **SchoolHoursSettings.cs**
- **Purpose**: Stores school hours configuration
- **Data**: Start/end times for each weekday
- **Default**: 08:00-15:30 Monday-Friday

---

### 📊 Data & Caching

#### **Caching/CacheService.cs**
- **Purpose**: In-memory caching system
- **Key Functions**:
  - Stores frequently accessed data (attendance, schedule)
  - Reduces API calls to iskole.net
  - Provides cache statistics
  - TTL (Time To Live) management

#### **Models.cs**
- **Purpose**: Data models and structures
- **Contains**: 
  - UserParameters (FylkeId, SkoleId, PlanPeri)
  - ScheduleItem, AttendanceData
  - Session information structures

#### **DataExportService.cs**
- **Purpose**: Exports user data for debugging/support
- **Key Functions**:
  - Collects logs, settings, cache data
  - Sanitizes sensitive information
  - Creates export package for troubleshooting

---

### 🔔 Notifications & UI

#### **NotificationService.cs**
- **Purpose**: Cross-platform notification system
- **Key Functions**:
  - Shows toast notifications
  - Manages notification queue
  - Handles notification levels (Info, Warning, Error, Success)
  - Dismissal logic

#### **NativeNotificationService.cs**
- **Purpose**: Platform-specific native notifications
- **Platforms**:
  - macOS: Uses NSUserNotification
  - Windows: Uses Windows Toast Notifications
  - Linux: Uses libnotify

#### **NotificationPermissionChecker.cs**
- **Purpose**: Checks if app has notification permissions
- **Functions**: Verifies OS-level notification access

#### **LoggingService.cs**
- **Purpose**: Application logging system
- **Key Functions**:
  - Logs to UI (ObservableCollection for real-time display)
  - Logs to file (persistent storage)
  - Log levels: Debug, Info, Success, Warning, Error
  - Thread-safe logging

#### **LogRetentionManager.cs**
- **Purpose**: Manages log file cleanup
- **Functions**: 
  - Deletes old log files (keeps last 7 days)
  - Prevents disk space issues

---

### 🖥️ System Integration

#### **TrayIconManager.cs**
- **Purpose**: System tray icon management
- **Key Functions**:
  - Creates tray icon with menu
  - Show/hide main window
  - Quick actions (Start/Stop automation)
  - Minimize to tray functionality

#### **MacOSDockHandler.cs**
- **Purpose**: macOS Dock integration
- **Functions**: 
  - Manages app icon in Dock
  - Badge notifications
  - Dock menu items

#### **MacOSCaffeinateService.cs**
- **Purpose**: Prevents Mac from sleeping during automation
- **Functions**: Uses `caffeinate` command to keep system awake

#### **ResourceMonitor.cs**
- **Purpose**: Monitors system resources
- **Functions**: 
  - Tracks CPU and memory usage
  - Helps identify performance issues

#### **SystemHealthCheck.cs**
- **Purpose**: Validates system requirements
- **Functions**:
  - Checks for required dependencies
  - Verifies Chrome/ChromeDriver availability
  - Network connectivity checks

---

### 🌐 Web & Browser

#### **ChromeDriverManager.cs**
- **Purpose**: Manages ChromeDriver for Selenium automation
- **Key Functions**:
  - Downloads correct ChromeDriver version
  - Matches Chrome browser version
  - Handles driver updates
  - Error reporting for driver issues

#### **Http/HttpClientFactory.cs**
- **Purpose**: Creates configured HTTP clients
- **Functions**: 
  - Centralized HTTP client configuration
  - Timeout settings
  - Header management

---

### 📱 Widget & macOS Integration

#### **WidgetDataService.cs**
- **Purpose**: Provides data to macOS widget
- **Key Functions**:
  - Writes attendance data to shared container
  - Updates widget display
  - Syncs dashboard data with widget

#### **WidgetHeartbeatService.cs**
- **Purpose**: Keeps widget data fresh
- **Functions**:
  - Periodic updates to widget
  - Ensures widget shows current data
  - Background refresh logic

---

### ⚙️ Configuration & Settings

#### **SettingsService.cs**
- **Purpose**: Manages app settings
- **Key Functions**:
  - Loads/saves user preferences
  - Start with system, minimize to tray, etc.
  - Settings change notifications
  - Persistent storage

#### **Configuration/AppConfiguration.cs**
- **Purpose**: App-wide configuration
- **Contains**: 
  - API endpoints
  - Default values
  - Feature flags

---

### 📈 Analytics & Tracking

#### **AnalyticsService.cs**
- **Purpose**: Usage analytics and error tracking
- **Key Functions**:
  - Tracks app sessions
  - Logs errors for debugging
  - Automation start/stop tracking
  - Anonymous usage statistics
- **Privacy**: No personal data collected

#### **ChangelogService.cs**
- **Purpose**: Manages changelog display
- **Key Functions**:
  - Loads changelog JSON files
  - Tracks which version user has seen
  - Shows "What's New" on updates

---

### 🔄 Migration & Updates

#### **MigrationService.cs**
- **Purpose**: Handles data migrations between versions
- **Key Functions**:
  - Migrates old settings format to new
  - Updates data structures
  - Ensures backward compatibility

---

### 🛠️ Utilities

#### **Utilities/GlobalExceptionHandler.cs**
- **Purpose**: Catches unhandled exceptions
- **Functions**: 
  - Logs crashes
  - Prevents app from crashing silently
  - Error reporting

#### **Utilities/InputValidator.cs**
- **Purpose**: Validates user input
- **Functions**: 
  - Email validation
  - Password strength checks
  - Input sanitization

---

### 🔌 Dependency Injection

#### **DependencyInjection/ServiceContainer.cs**
- **Purpose**: Service locator pattern
- **Key Functions**:
  - Registers all services
  - Provides singleton instances
  - Manages service lifecycle
  - Initializes background services

---

### 📋 Interfaces

#### **Interfaces/IAutomationService.cs**
- Defines automation service contract

#### **Interfaces/ICacheService.cs**
- Defines caching service contract

#### **Interfaces/ILoggingService.cs**
- Defines logging service contract

#### **Interfaces/INotificationService.cs**
- Defines notification service contract

#### **Interfaces/ISettingsService.cs**
- Defines settings service contract

---

## 📁 ViewModels Folder (UI Logic)

### **RefactoredMainWindowViewModel.cs**
- **Purpose**: Main window logic (Dashboard)
- **Key Functions**:
  - Manages authentication state
  - Controls automation start/stop
  - Handles navigation (Settings, Tutorial, Feide)
  - Coordinates all services
  - Manages UI state and commands
- **Commands**: Start/Stop automation, refresh data, open settings, etc.

### **DashboardViewModel.cs**
- **Purpose**: Dashboard statistics and display
- **Key Functions**:
  - Shows today's attendance (X/Y sessions)
  - Displays next/current class
  - Weekly progress bar
  - Monthly statistics
  - Overtime/undertime calculation
  - Auto-refresh logic
- **Data Sources**: AttendanceDataService, Cache

### **SettingsViewModel.cs**
- **Purpose**: Settings page logic
- **Key Functions**:
  - Manages all app settings
  - Credential management (username/password)
  - Auto-start configuration
  - Theme settings
  - Export logs functionality
  - Advanced settings (clear cache, reset data)
- **Events**: CredentialsSaved, CloseRequested

### **FeideWindowViewModel.cs**
- **Purpose**: Initial Feide setup wizard
- **Key Functions**:
  - First-time setup flow
  - School selection
  - Feide credential entry
  - Validates and saves credentials
  - Triggers initial authentication
- **Used**: Only on first launch or after reset

### **ChangelogWindowViewModel.cs**
- **Purpose**: Displays changelog overlay
- **Key Functions**:
  - Shows "What's New" after updates
  - Loads changelog from JSON
  - Displays features, fixes, improvements
  - Supports images in changelog

### **DependencyDownloadViewModel.cs**
- **Purpose**: ChromeDriver download progress
- **Key Functions**:
  - Shows download progress
  - Handles ChromeDriver installation
  - Error handling for failed downloads

### **ThemeManager.cs**
- **Purpose**: Theme management (Dark/Light mode)
- **Key Functions**:
  - Switches between themes
  - Provides theme colors
  - Persists theme preference
  - Observable theme changes

### **ViewModelBase.cs**
- **Purpose**: Base class for all ViewModels
- **Provides**: INotifyPropertyChanged implementation

### **Converters.cs**
- **Purpose**: XAML value converters
- **Contains**:
  - PercentageToWidthConverter (progress bars)
  - PercentageToHeightConverter (weekly grid)
  - ColorNameToBrushConverter (dynamic colors)
  - DayOfWeekConverter (day names)
  - TextLengthToFontSizeConverter (responsive text)
  - BoolToStringConverter (conditional text)
  - And many more...

### **ColorNameToBrushConverter.cs**
- **Purpose**: Converts color names to brushes
- **Functions**: Maps color strings to Avalonia brushes

---

## 🎯 Key Workflows

### **Startup Flow**
1. `Program.cs` → `ServiceContainer.Initialize()`
2. Load settings from `SettingsService`
3. Check if first launch → Show `FeideWindow`
4. Otherwise → Show `MainWindow` with `RefactoredMainWindowViewModel`
5. `InitializeAsync()` → `AuthenticationService.AuthenticateAsync()`
6. Load dashboard data via `AttendanceDataService`
7. Start background services (widget heartbeat, log retention)

### **Automation Flow**
1. User clicks "Start automatisering"
2. `AutomationService.StartAsync()`
3. `SchoolTimeChecker` validates it's school hours
4. Timer checks every minute for registration windows
5. When window opens → Selenium clicks "Bekreft tilstedeværelse"
6. Dashboard updates automatically
7. Widget syncs with new data

### **Data Refresh Flow**
1. User clicks refresh or auto-refresh timer triggers
2. `DashboardViewModel.RefreshDataAsync()`
3. `AttendanceDataService` fetches from iskole.net
4. Cache updated with new data
5. UI updates via property change notifications
6. Widget receives updated data

---

## 🗂️ File Count Summary

**Services**: 33 files
- Core Services: 27 files
- Interfaces: 5 files
- Utilities: 2 files
- Caching: 1 file
- Configuration: 1 file
- DependencyInjection: 1 file
- Http: 1 file

**ViewModels**: 10 files
- Main ViewModels: 6 files
- Converters: 2 files
- Base/Utilities: 2 files

**Total**: 43 files in Services + ViewModels

---

## 🧹 Cleanup Recommendations

Based on this analysis, here are files you might consider removing or consolidating:

### **Potentially Unused/Redundant**:
1. **DataExportService.cs** - Only used for debugging, could be optional
2. **ResourceMonitor.cs** - If not actively monitoring performance
3. **DependencyDownloadViewModel.cs** - If ChromeDriver is pre-bundled
4. **ColorNameToBrushConverter.cs** - Duplicate of functionality in Converters.cs

### **Platform-Specific (Remove if not targeting that platform)**:
- **MacOSAuthService.cs** - Only for macOS
- **MacOSDockHandler.cs** - Only for macOS
- **MacOSCaffeinateService.cs** - Only for macOS
- **WindowsHelloAuthService.cs** - Only for Windows
- **LinuxPinAuthService.cs** - Only for Linux
- **WidgetDataService.cs** - Only for macOS widget
- **WidgetHeartbeatService.cs** - Only for macOS widget

### **Could Be Consolidated**:
- **NotificationService.cs** + **NativeNotificationService.cs** → Single service
- **KeychainService.cs** → Already wrapped by SecureCredentialStorage
- **SchoolHoursSettings.cs** → Could be part of SettingsService

---

## 📊 Architecture Summary

```
┌─────────────────────────────────────────┐
│           ViewModels (UI Logic)         │
│  - RefactoredMainWindowViewModel        │
│  - DashboardViewModel                   │
│  - SettingsViewModel                    │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│      Services (Business Logic)          │
│  ┌─────────────────────────────────┐   │
│  │  Core Services                  │   │
│  │  - AuthenticationService        │   │
│  │  - AutomationService            │   │
│  │  - AttendanceDataService        │   │
│  └─────────────────────────────────┘   │
│  ┌─────────────────────────────────┐   │
│  │  Platform Services              │   │
│  │  - SecureCredentialStorage      │   │
│  │  - NotificationService          │   │
│  │  - TrayIconManager              │   │
│  └─────────────────────────────────┘   │
│  ┌─────────────────────────────────┐   │
│  │  Support Services               │   │
│  │  - LoggingService               │   │
│  │  - CacheService                 │   │
│  │  - SettingsService              │   │
│  └─────────────────────────────────┘   │
└─────────────────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│      External Systems                   │
│  - iskole.net API                       │
│  - Selenium WebDriver                   │
│  - OS Keychain/Credential Manager      │
│  - System Notifications                 │
└─────────────────────────────────────────┘
```
