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

        /// <summary>
        /// Performs one-time migration cleanup for users upgrading from old app version.
        /// This prevents infinite login loops caused by invalid credentials from the old app.
        /// </summary>
        public static async Task<bool> PerformOneTimeMigrationAsync()
        {
            try
            {
                Debug.WriteLine("[Migration] Checking if one-time migration is needed...");
                
                // Check if migration has already been performed
                if (File.Exists(MigrationMarkerPath))
                {
                    Debug.WriteLine("[Migration] Migration already completed, skipping");
                    return false; // No migration needed
                }

                Debug.WriteLine("[Migration] First launch of new version detected - performing one-time cleanup");
                
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

                // Create migration marker to prevent future cleanups
                await CreateMigrationMarkerAsync();
                
                Debug.WriteLine("[Migration] One-time migration completed successfully");
                return hadOldData; // Return true if we actually cleaned up data
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Migration failed: {ex.Message}");
                // Don't throw - app should continue even if migration fails
                return false;
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

                // Check for any credential-related files that might contain old data
                var credentialFiles = new[]
                {
                    Path.Combine(akademiTrackDir, "credentials.json"),
                    Path.Combine(akademiTrackDir, "cookies.json"),
                    Path.Combine(akademiTrackDir, "user_parameters.json"),
                    Path.Combine(akademiTrackDir, "settings.json")
                };

                bool hasOldData = false;
                foreach (var file in credentialFiles)
                {
                    if (File.Exists(file))
                    {
                        Debug.WriteLine($"[Migration] Found existing data file: {Path.GetFileName(file)}");
                        hasOldData = true;
                    }
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
                var markerContent = $"AkademiTrack v2 Migration Completed\nDate: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nVersion: 2.0.0";
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
            return File.Exists(MigrationMarkerPath);
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Migration] Error resetting migration marker: {ex.Message}");
            }
        }
    }
}