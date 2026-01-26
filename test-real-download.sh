#!/bin/bash

echo "🧪 Testing Real Chromium Download"
echo "=================================="
echo ""

# Find and delete Chromium cache
echo "🔍 Looking for Chromium cache directories..."

CHROMIUM_DIRS=(
    "./bin/Debug/net9.0/chromium"
    "./bin/Release/net9.0/chromium"
    "$HOME/.local-chromium"
    "$HOME/Library/Caches/ms-playwright"
    "$HOME/.cache/ms-playwright"
)

FOUND_DIRS=()

for dir in "${CHROMIUM_DIRS[@]}"; do
    if [ -d "$dir" ]; then
        FOUND_DIRS+=("$dir")
        echo "✅ Found: $dir"
    fi
done

if [ ${#FOUND_DIRS[@]} -eq 0 ]; then
    echo "❌ No Chromium directories found. The download window may not appear."
    echo ""
    echo "💡 Try running the app first to let it download Chromium, then run this script."
    exit 1
fi

echo ""
echo "🗑️  Deleting Chromium directories..."

for dir in "${FOUND_DIRS[@]}"; do
    echo "   Deleting: $dir"
    rm -rf "$dir"
done

echo ""
echo "✅ Chromium cache deleted!"
echo ""
echo "🚀 Now run the app to see the real download window:"
echo "   dotnet run"
echo ""
echo "📊 You should see:"
echo "   • Real download progress (0-120 MB)"
echo "   • Smooth progress updates"
echo "   • Transition to main app (not close)"
echo ""