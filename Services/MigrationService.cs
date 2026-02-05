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
        private static readonly string CurrentMigrationVersion = "2.1.0";

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

                // Check if there's any truly old data to clean up
                bool hadTrulyOldData = CheckForTrulyOldData();
                
                if (hadTrulyOldData)
                {
                    Debug.WriteLine("[Migration] Found truly old/incompatible app data - clearing it");
                    await ClearTrulyOldDataAsync();
                }
                else
                {
                    Debug.WriteLine("[Migration] No truly old data found - just updating version tracking");
                }

                // Update version tracking (this is safe and doesn't clear user data)
                await UpdateVersionTrackingAsync();
                
                Debug.WriteLine("[Migration] Migration completed successfully");
                return hadTrulyOldData; // Return true if we actually cleaned up data
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
                        // Check if there's any TRULY old app data that needs migration
                        bool hasOldAppData = CheckForTrulyOldData();
                        
                        if (hasOldAppData)
                        {
                            Debug.WriteLine("[Migration] ✅ Found truly old app data - this is an upgrade from old version");
                            return true; // Upgrade from old app - needs migration
                        }
                        else
                        {
                            Debug.WriteLine("[Migration] ✅ No old app data - this is a fresh install or current version");
                            // Fresh install or current version - create version file but no migration needed
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
                    lastVersion = lastVersion.Trim().Split('\n')[0]; // Take only the first line (version number)
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

        private static bool CheckForTrulyOldData()
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

                // Only check for TRULY old files that indicate an incompatible old version
                // DO NOT include current version files like settings.json, last_version.txt, etc.
                var trulyOldFiles = new[]
                {
                    // These are files from very old versions that are incompatible
                    Path.Combine(akademiTrackDir, "old_credentials.json"), // Example old file
                    Path.Combine(akademiTrackDir, "legacy_settings.json"), // Example old file
                    Path.Combine(akademiTrackDir, "v1_data.json"), // Example old file
                    
                    // Add other truly old/incompatible files here if they exist
                    // DO NOT add current files like settings.json, last_version.txt, etc.
                };

                bool hasTrulyOldData = false;
                foreach (var file in trulyOldFiles)
                {
                    if (File.Exists(file))
                    {
                        Debug.WriteLine($"[Migration] Found truly old app file: {Path.GetFileName(file)}");
                        hasTrulyOldData = true;
                    }
                }

                // Check if we have current files but no version tracking (this indicates a recent version without version tracking)
                var currentFiles = new[]
                {
                    Path.Combine(akademiTrackDir, "settings.json"),
                    Path.Combine(akademiTrackDir, "v2_migration_complete.marker")
                };

                bool hasCurrentFiles = currentFiles.Any(File.Exists);
                bool hasVersionFile = File.Exists(LastVersionPath);

                if (hasCurrentFiles && !hasVersionFile)
                {
                    Debug.WriteLine("[Migration] Found current files without version tracking - this is a recent version, not old data");
                    // This is NOT old data - it's just a recent version without version tracking
                    // We should create version tracking but NOT clear the data
                    return false;
                }

                if (hasTrulyOldData)
                {
                    Debug.WriteLine("[Migration] ✅ Detected truly old/incompatible app installation - migration needed");
                }
                else
                {
                    Debug.WriteLine("[Migration] ✅ No truly old app data detected");
                }

                return hasTrulyOldData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error checking for truly old data: {ex.Message}");
                return false; // If we can't check, assume no old data to be safe
            }
        }

        private static Task ClearTrulyOldDataAsync()
        {
            try
            {
                Debug.WriteLine("[Migration] Clearing only truly old/incompatible data...");
                
                var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var akademiTrackDir = Path.Combine(appDataDir, "AkademiTrack");
                
                if (!Directory.Exists(akademiTrackDir))
                {
                    Debug.WriteLine("[Migration] No directory to clean");
                    return Task.CompletedTask;
                }

                // Only delete files that are truly from old/incompatible versions
                var trulyOldFiles = new[]
                {
                    Path.Combine(akademiTrackDir, "old_credentials.json"),
                    Path.Combine(akademiTrackDir, "legacy_settings.json"),
                    Path.Combine(akademiTrackDir, "v1_data.json"),
                    // Add other truly old files here if they exist
                };

                foreach (var file in trulyOldFiles)
                {
                    if (File.Exists(file))
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine($"[Migration] ✓ Deleted truly old file: {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Migration] ⚠️ Could not delete {Path.GetFileName(file)}: {ex.Message}");
                        }
                    }
                }

                // DO NOT clear current files like settings.json, credentials, etc.
                // These are valid current data that should be preserved
                
                Debug.WriteLine("[Migration] Truly old data cleanup completed - current user data preserved");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error clearing truly old data: {ex.Message}");
                // Don't throw - this is not critical enough to stop the app
                return Task.CompletedTask;
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