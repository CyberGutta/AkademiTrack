# Velopack Setup Guide

This document explains how to set up Velopack for AkademiTrack auto-updates.

## Prerequisites

1. Install the Velopack CLI tool:
   ```bash
   dotnet tool install -g vpk
   ```

2. Velopack package is already added to `AkademiTrack.csproj`

## Build Process

The build scripts now automatically create Velopack packages:

### Windows
```bash
python build-windows.py
```
Creates: `AkademiTrack-{version}-win-x64-full.nupkg`

### macOS
```bash
python build-mac.py
```
Creates: `AkademiTrack-{version}-osx-arm64-full.nupkg`

### Linux
```bash
python build-linux.py
```
Creates: `AkademiTrack-{version}-linux-x64-full.nupkg`

## Release Server Setup

1. Upload the `.nupkg` files and `RELEASES` file to your release server
2. The URL structure should be:
   ```
   https://your-server.com/releases/
   ├── RELEASES
   ├── AkademiTrack-1.0.0-win-x64-full.nupkg
   ├── AkademiTrack-1.0.1-win-x64-full.nupkg
   └── ...
   ```

## App Integration

To integrate Velopack updates into your app, you would typically add code like:

```csharp
// This is NOT included in the current build - just for reference
using Velopack;

public class UpdateService
{
    private readonly UpdateManager _updateManager;
    
    public UpdateService()
    {
        _updateManager = new UpdateManager("https://your-server.com/releases/");
    }
    
    public async Task CheckForUpdatesAsync()
    {
        var updateInfo = await _updateManager.CheckForUpdatesAsync();
        if (updateInfo != null)
        {
            await _updateManager.DownloadUpdatesAsync(updateInfo);
            _updateManager.ApplyUpdatesAndRestart();
        }
    }
}
```

## Notes

- The Velopack package reference is added to the project file for build compatibility
- No Velopack code is integrated into the app itself - only the build tools
- You can integrate Velopack updates later by adding the appropriate service code
- The build scripts handle all the packaging automatically