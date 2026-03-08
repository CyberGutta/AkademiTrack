using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Services
{
    public class ChangelogData
    {
        public string Version { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        public string? HeaderImage { get; set; } // Optional header image path
        public string? Description { get; set; } // Optional description text
        public ChangeCategory[] Changes { get; set; } = Array.Empty<ChangeCategory>();
    }

    public class ChangeCategory
    {
        public string Category { get; set; } = string.Empty;
        public string? Icon { get; set; } // Optional icon/emoji for the category
        public string? Image { get; set; } // Optional image for this category
        public string[] Items { get; set; } = Array.Empty<string>();
    }

    public static class ChangelogService
    {
        private static readonly string CurrentVersion = "1.3.0";

        private static string GetChangelogSeenFilePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");
            return Path.Combine(appFolderPath, "changelog-seen.txt");
        }

        public static async Task<(bool shouldShow, ChangelogData? data)> ShouldShowChangelogAsync()
        {
            try
            {
                Console.WriteLine("[ChangelogService] ========== ShouldShowChangelogAsync called ==========");
                
                // Check if user has any app data (existing user vs brand new user)
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string settingsPath = Path.Combine(appDataDir, "settings.json");
                
                Console.WriteLine($"[ChangelogService] Settings path: {settingsPath}");
                Console.WriteLine($"[ChangelogService] Settings file exists: {File.Exists(settingsPath)}");
                
                // Brand new user - no settings file exists at all
                if (!File.Exists(settingsPath))
                {
                    Console.WriteLine("[ChangelogService] Brand new user - no settings file exists, skipping changelog");
                    return (false, null);
                }
                
                // Check if user has already seen this version
                string changelogSeenPath = GetChangelogSeenFilePath();
                Console.WriteLine($"[ChangelogService] Changelog seen path: {changelogSeenPath}");
                Console.WriteLine($"[ChangelogService] Changelog seen file exists: {File.Exists(changelogSeenPath)}");
                
                if (File.Exists(changelogSeenPath))
                {
                    try
                    {
                        string seenVersion = await File.ReadAllTextAsync(changelogSeenPath);
                        seenVersion = seenVersion.Trim();
                        
                        Console.WriteLine($"[ChangelogService] Seen version: {seenVersion}");
                        Console.WriteLine($"[ChangelogService] Current version: {CurrentVersion}");
                        
                        if (seenVersion == CurrentVersion)
                        {
                            Console.WriteLine($"[ChangelogService] User already seen version {CurrentVersion} - no changelog needed");
                            return (false, null);
                        }
                        else
                        {
                            Console.WriteLine($"[ChangelogService] Version changed from {seenVersion} to {CurrentVersion}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ChangelogService] Error reading changelog-seen file: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[ChangelogService] Existing user without changelog tracking - showing changelog");
                }
                
                // Load and show changelog
                Console.WriteLine("[ChangelogService] Loading changelog data...");
                var changelogData = await LoadChangelogAsync(CurrentVersion);
                if (changelogData != null)
                {
                    Console.WriteLine($"[ChangelogService] ✅ Changelog loaded successfully");
                    Console.WriteLine($"[ChangelogService] HeaderImage: {changelogData.HeaderImage}");
                    return (true, changelogData);
                }
                else
                {
                    Console.WriteLine($"[ChangelogService] ❌ Failed to load changelog");
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChangelogService] Error checking changelog: {ex.Message}");
                return (false, null);
            }
        }

        public static async Task<ChangelogData?> LoadChangelogAsync(string version)
        {
            try
            {
                string changelogPath;
                
                // On macOS, check if we're in an app bundle and look in Resources first
                if (OperatingSystem.IsMacOS())
                {
                    var resourcesPath = Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Changelogs", $"{version}.json");
                    if (File.Exists(resourcesPath))
                    {
                        changelogPath = resourcesPath;
                    }
                    else
                    {
                        // Fallback to the old location
                        changelogPath = Path.Combine(AppContext.BaseDirectory, "Changelogs", $"{version}.json");
                    }
                }
                else
                {
                    // Windows/Linux - use the standard location
                    changelogPath = Path.Combine(AppContext.BaseDirectory, "Changelogs", $"{version}.json");
                }
                
                if (!File.Exists(changelogPath))
                {
                    Debug.WriteLine($"[ChangelogService] Changelog file not found: {changelogPath}");
                    return null;
                }

                var json = await File.ReadAllTextAsync(changelogPath);
                var data = JsonSerializer.Deserialize<ChangelogData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChangelogService] Error loading changelog: {ex.Message}");
                return null;
            }
        }

        public static async Task MarkChangelogAsSeenAsync()
        {
            try
            {
                Debug.WriteLine($"[ChangelogService] ========== MarkChangelogAsSeenAsync called ==========");
                
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolderPath = Path.Combine(appDataPath, "AkademiTrack");
                Directory.CreateDirectory(appFolderPath);
                
                string changelogSeenPath = GetChangelogSeenFilePath();
                Debug.WriteLine($"[ChangelogService] Writing version {CurrentVersion} to: {changelogSeenPath}");
                
                await File.WriteAllTextAsync(changelogSeenPath, CurrentVersion);
                
                Debug.WriteLine($"[ChangelogService] ✅ Successfully marked version {CurrentVersion} as seen");
                
                // Verify it was written
                if (File.Exists(changelogSeenPath))
                {
                    string written = await File.ReadAllTextAsync(changelogSeenPath);
                    Debug.WriteLine($"[ChangelogService] ✅ Verified file contents: {written}");
                }
                else
                {
                    Debug.WriteLine($"[ChangelogService] ❌ File does not exist after write!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChangelogService] ❌ Error marking changelog as seen: {ex.Message}");
                Debug.WriteLine($"[ChangelogService] Stack trace: {ex.StackTrace}");
            }
        }
    }
}
