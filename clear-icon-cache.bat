@echo off
echo Clearing Windows Icon Cache...
echo.

echo Stopping Explorer...
taskkill /f /im explorer.exe

echo Deleting icon cache files...
del /a /q "%localappdata%\IconCache.db" 2>nul
del /a /f /q "%localappdata%\Microsoft\Windows\Explorer\iconcache*.db" 2>nul

echo Restarting Explorer...
start explorer.exe

echo.
echo Done! Icon cache cleared.
echo The Setup.exe icon should now display correctly.
echo.
pause
