using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class MigrationService
    {
        private static readonly string MigrationMarkerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AkademiTrack",
            "v2_migration_complete.marker"
        );

        private static readonly string LastVersionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AkademiTrack",
            "last_version.txt"
        );

        // Update this version when you want to trigger migration for all users
        private static readonly string CurrentMigrationVersion = "1.1.1";

        // Add a unique identifier to prevent caching issues
        private static readonly string MigrationSessionId = Guid.NewGuid().ToString("N")[..8];

        /// <summary>
        /// Check if migration is needed WITHOUT performing it
        /// </summary>
        public static async Task<bool> IsMigrationNeededAsync()
        {
            try
            {
                Debug.WriteLine($"[Migration] Session ID: {MigrationSessionId}");
                return await CheckIfMigrationNeededAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error checking if migration needed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Performs one-time migration cleanup for users upgrading from old app version.
        /// This prevents infinite login loops caused by invalid credentials from the old app.
        /// </summary>
        public static async Task<bool> PerformOneTimeMigrationAsync()
        {
            try
            {
                Debug.WriteLine("[Migration] Checking if migration is needed...");
                
                // Check if this is a version update that requires migration
                bool needsMigration = await CheckIfMigrationNeededAsync();
                
                if (!needsMigration)
                {
                    Debug.WriteLine("[Migration] No migration needed - same version or already migrated");
                    return false;
                }

                Debug.WriteLine($"[Migration] Version update detected - performing migration to v{CurrentMigrationVersion}");
                
                // Ensure the directory exists
                var appDataDir = Path.GetDirectoryName(MigrationMarkerPath);
                if (!string.IsNullOrEmpty(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }

                // Check if there's any old data to clean up
                bool hadOldData = CheckForOldData();
                
                if (hadOldData)
                {
                    Debug.WriteLine("[Migration] Found old app data - clearing potentially invalid credentials");
                    await ClearOldCredentialsAsync();
                }
                else
                {
                    Debug.WriteLine("[Migration] No old data found - this appears to be a new user");
                }

                // Update version tracking
                await UpdateVersionTrackingAsync();
                
                Debug.WriteLine("[Migration] Migration completed successfully");
                return hadOldData; // Return true if we actually cleaned up data
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Migration failed: {ex.Message}");
                // Don't throw - app should continue even if migration fails
                return false;
            }
        }

        private static async Task<bool> CheckIfMigrationNeededAsync()
        {
            try
            {
                Debug.WriteLine($"[Migration] Checking migration status (Session: {MigrationSessionId})");
                
                // STEP 1: Force refresh file system cache by checking multiple times
                await Task.Delay(100); // Small delay to ensure file system is ready
                
                bool versionFileExists = File.Exists(LastVersionPath);
                Debug.WriteLine($"[Migration] Version file exists: {versionFileExists} (Path: {LastVersionPath})");
                
                // STEP 2: Check if this is an upgrade from the old app (no version tracking)
                if (!versionFileExists)
                {
                    Debug.WriteLine("[Migration] No version file found - checking for old app data");
                    
                    // Double-check with a small delay to avoid caching issues
                    await Task.Delay(50);
                    versionFileExists = File.Exists(LastVersionPath);
                    
                    if (!versionFileExists)
                    {
                        // Check if there's any old app data (settings, credentials, etc.)
                        bool hasOldAppData = CheckForOldData();
                        
                        if (hasOldAppData)
                        {
                            Debug.WriteLine("[Migration] ✅ Found old app data - this is an upgrade from old version");
                            return true; // Upgrade from old app - needs migration
                        }
                        else
                        {
                            Debug.WriteLine("[Migration] ✅ No old app data - this is a fresh install");
                            // Fresh install - create version file but no migration needed
                            await UpdateVersionTrackingAsync();
                            return false;
                        }
                    }
                }

                // STEP 3: We have a version file - check if version changed
                string lastVersion;
                try
                {
                    lastVersion = await File.ReadAllTextAsync(LastVersionPath);
                    lastVersion = lastVersion.Trim();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Migration] Error reading version file: {ex.Message}");
                    // If we can't read the version file, assume migration is needed
                    return true;
                }
                
                Debug.WriteLine($"[Migration] Last version: '{lastVersion}', Current migration version: '{CurrentMigrationVersion}'");
                
                // If versions don't match, we need migration
                if (lastVersion != CurrentMigrationVersion)
                {
                    Debug.WriteLine("[Migration] ✅ Version mismatch - migration needed");
                    return true;
                }
                
                Debug.WriteLine("[Migration] ✅ Same version - no migration needed");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error in migration check: {ex.Message}");
                Debug.WriteLine($"[Migration] Stack trace: {ex.StackTrace}");
                // If anything goes wrong, assume migration is needed for safety
                return true;
            }
        }

        private static async Task UpdateVersionTrackingAsync()
        {
            try
            {
                Debug.WriteLine($"[Migration] Updating version tracking to: {CurrentMigrationVersion}");
                
                // Ensure directory exists
                var appDataDir = Path.GetDirectoryName(LastVersionPath);
                if (!string.IsNullOrEmpty(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }
                
                // Create version content with timestamp and session info for cache-busting
                var versionContent = $"{CurrentMigrationVersion}\n# Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n# Session: {MigrationSessionId}";
                
                // Write version file with retry logic
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await File.WriteAllTextAsync(LastVersionPath, versionContent);
                        
                        // Verify the write was successful
                        await Task.Delay(50);
                        if (File.Exists(LastVersionPath))
                        {
                            var verifyContent = await File.ReadAllTextAsync(LastVersionPath);
                            if (verifyContent.StartsWith(CurrentMigrationVersion))
                            {
                                Debug.WriteLine($"[Migration] ✅ Version file updated successfully (attempt {attempt})");
                                break;
                            }
                        }
                        
                        if (attempt == 3)
                        {
                            Debug.WriteLine("[Migration] ⚠️ Failed to verify version file write after 3 attempts");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Migration] Version file write attempt {attempt} failed: {ex.Message}");
                        if (attempt < 3)
                        {
                            await Task.Delay(100 * attempt); // Increasing delay
                        }
                    }
                }
                
                // Also create/update the migration marker for backward compatibility
                var markerContent = $"AkademiTrack Migration Completed\nDate: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nVersion: {CurrentMigrationVersion}\nSession: {MigrationSessionId}";
                
                try
                {
                    await File.WriteAllTextAsync(MigrationMarkerPath, markerContent);
                    Debug.WriteLine($"[Migration] ✅ Migration marker updated");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Migration] ⚠️ Failed to update migration marker: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error updating version tracking: {ex.Message}");
            }
        }

        private static bool CheckForOldData()
        {
            try
            {
                var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var akademiTrackDir = Path.Combine(appDataDir, "AkademiTrack");
                
                if (!Directory.Exists(akademiTrackDir))
                {
                    Debug.WriteLine("[Migration] No AkademiTrack directory found - new user");
                    return false;
                }

                // Check for any files that indicate old app usage
                var indicatorFiles = new[]
                {
                    // Credential and session files
                    Path.Combine(akademiTrackDir, "credentials.json"),
                    Path.Combine(akademiTrackDir, "cookies.json"),
                    Path.Combine(akademiTrackDir, "user_parameters.json"),
                    Path.Combine(akademiTrackDir, "settings.json"),
                    
                    // Old migration marker (from previous version)
                    Path.Combine(akademiTrackDir, "v2_migration_complete.marker"),
                    
                    // Any other files that might exist from old versions
                    Path.Combine(akademiTrackDir, "app_settings.json"),
                    Path.Combine(akademiTrackDir, "user_data.json")
                };

                bool hasOldData = false;
                foreach (var file in indicatorFiles)
                {
                    if (File.Exists(file))
                    {
                        Debug.WriteLine($"[Migration] Found old app file: {Path.GetFileName(file)}");
                        hasOldData = true;
                    }
                }

                // Also check for any subdirectories that might contain old data
                if (Directory.Exists(akademiTrackDir))
                {
                    var subdirs = Directory.GetDirectories(akademiTrackDir);
                    if (subdirs.Length > 0)
                    {
                        Debug.WriteLine($"[Migration] Found {subdirs.Length} subdirectories from old app");
                        hasOldData = true;
                    }
                }

                if (hasOldData)
                {
                    Debug.WriteLine("[Migration] ✅ Detected old app installation - migration needed");
                }
                else
                {
                    Debug.WriteLine("[Migration] ✅ No old app data detected - fresh installation");
                }

                return hasOldData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error checking for old data: {ex.Message}");
                return false;
            }
        }

        private static async Task ClearOldCredentialsAsync()
        {
            try
            {
                Debug.WriteLine("[Migration] Clearing old credentials and app data to prevent login loops...");
                
                // STEP 1: Clear all stored credentials and cookies from keychain/secure storage
                await SecureCredentialStorage.ClearAllDataAsync();
                Debug.WriteLine("[Migration] ✓ Keychain/secure storage cleared");
                
                // STEP 2: Clear ALL Application Support files (except the migration marker)
                var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var akademiTrackDir = Path.Combine(appDataDir, "AkademiTrack");
                
                if (Directory.Exists(akademiTrackDir))
                {
                    Debug.WriteLine($"[Migration] Clearing Application Support directory: {akademiTrackDir}");
                    
                    // Get all files except the migration marker (which we'll create after)
                    var filesToDelete = Directory.GetFiles(akademiTrackDir)
                        .Where(f => !Path.GetFileName(f).Equals("v2_migration_complete.marker", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    
                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine($"[Migration] ✓ Deleted: {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Migration] ⚠️ Could not delete {Path.GetFileName(file)}: {ex.Message}");
                        }
                    }
                    
                    // Also clear any subdirectories (but keep the main directory)
                    var dirsToDelete = Directory.GetDirectories(akademiTrackDir);
                    foreach (var dir in dirsToDelete)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            Debug.WriteLine($"[Migration] ✓ Deleted directory: {Path.GetFileName(dir)}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Migration] ⚠️ Could not delete directory {Path.GetFileName(dir)}: {ex.Message}");
                        }
                    }
                }
                
                Debug.WriteLine("[Migration] Old credentials and app data cleared successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error clearing old credentials: {ex.Message}");
                // Don't throw - this is not critical enough to stop the app
            }
        }

        private static async Task CreateMigrationMarkerAsync()
        {
            try
            {
                var markerContent = $"AkademiTrack Migration Completed\nDate: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nVersion: {CurrentMigrationVersion}";
                await File.WriteAllTextAsync(MigrationMarkerPath, markerContent);
                
                Debug.WriteLine($"[Migration] Migration marker created at: {MigrationMarkerPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error creating migration marker: {ex.Message}");
                // Don't throw - app should continue even if marker creation fails
            }
        }

        /// <summary>
        /// Check if the user has been migrated (for debugging/info purposes)
        /// </summary>
        public static bool IsMigrationCompleted()
        {
            return File.Exists(LastVersionPath) && File.Exists(MigrationMarkerPath);
        }

        /// <summary>
        /// Force reset migration status (for testing only)
        /// </summary>
        public static void ResetMigrationForTesting()
        {
            try
            {
                if (File.Exists(MigrationMarkerPath))
                {
                    File.Delete(MigrationMarkerPath);
                    Debug.WriteLine("[Migration] Migration marker reset for testing");
                }
                
                if (File.Exists(LastVersionPath))
                {
                    File.Delete(LastVersionPath);
                    Debug.WriteLine("[Migration] Version file reset for testing");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error resetting migration: {ex.Message}");
            }
        }

        /// <summary>
        /// Force migration on next app start (for updates that require data cleanup)
        /// </summary>
        public static void ForceMigrationOnNextStart()
        {
            try
            {
                if (File.Exists(LastVersionPath))
                {
                    File.Delete(LastVersionPath);
                    Debug.WriteLine("[Migration] Forced migration on next start by removing version file");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error forcing migration: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear any potential caches and force fresh migration check
        /// </summary>
        public static async Task<bool> ForceFreshMigrationCheckAsync()
        {
            try
            {
                Debug.WriteLine($"[Migration] Performing fresh migration check (Session: {MigrationSessionId})");
                
                // Clear any potential .NET file system caches
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                // Small delay to ensure file system operations are complete
                await Task.Delay(200);
                
                // Force re-check
                return await CheckIfMigrationNeededAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error in fresh migration check: {ex.Message}");
                return true; // Assume migration needed if check fails
            }
        }
    }
}