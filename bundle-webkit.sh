#!/bin/bash

# Script to bundle WebKit with your app for distribution
# This avoids runtime downloads and Gatekeeper issues

echo "Bundling WebKit for distribution..."

# Download WebKit to a temporary location
export PLAYWRIGHT_BROWSERS_PATH="./temp-webkit"
dotnet run --project AkademiTrack.csproj -- install webkit

# Copy WebKit to app bundle
WEBKIT_SOURCE="./temp-webkit"
WEBKIT_DEST="./AkademiTrack.app/Contents/Resources/webkit-browsers"

if [ -d "$WEBKIT_SOURCE" ]; then
    echo "Copying WebKit to app bundle..."
    mkdir -p "$WEBKIT_DEST"
    cp -R "$WEBKIT_SOURCE"/* "$WEBKIT_DEST/"
    
    # Remove quarantine attributes
    echo "Removing quarantine attributes..."
    xattr -dr com.apple.quarantine "$WEBKIT_DEST" 2>/dev/null || true
    
    # Sign the WebKit binaries (you'll need your developer certificate)
    echo "Signing WebKit binaries..."
    find "$WEBKIT_DEST" -type f -perm +111 -exec codesign --force --sign "Developer ID Application: Your Name" {} \;
    
    # Clean up temp directory
    rm -rf "$WEBKIT_SOURCE"
    
    echo "WebKit bundling completed!"
else
    echo "Error: WebKit not found in temp directory"
    exit 1
fi