using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Services
{
    public class ChangelogData
    {
        public required string Version { get; set; } = string.Empty;
        public required string Title { get; set; } = string.Empty;
        public required string ReleaseDate { get; set; } = string.Empty;
        public string? HeaderImage { get; set; } // Optional header image path
        public string? Description { get; set; } // Optional description text
        public required ChangeCategory[] Changes { get; set; } = Array.Empty<ChangeCategory>();
    }

    public class ChangeCategory
    {
        public required string Category { get; set; } = string.Empty;
        public string? Icon { get; set; } // Optional icon/emoji for the category
        public string? Image { get; set; } // Optional image for this category
        public required string[] Items { get; set; } = Array.Empty<string>();
    }

    public static class ChangelogService
    {
        private static readonly SemaphoreSlim _changelogFileSemaphore = new SemaphoreSlim(1, 1);
        private static readonly string CurrentVersion = 
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

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
                Debug.WriteLine("[ChangelogService] ========== ShouldShowChangelogAsync called ==========");
                
                // Check if user has any app data (existing user vs brand new user)
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                string settingsPath = Path.Combine(appDataDir, "settings.json");
                
                Debug.WriteLine($"[ChangelogService] Settings path: {settingsPath}");
                Debug.WriteLine($"[ChangelogService] Settings file exists: {File.Exists(settingsPath)}");
                
                // Brand new user - no settings file exists at all
                if (!File.Exists(settingsPath))
                {
                    Debug.WriteLine("[ChangelogService] Brand new user - no settings file exists, skipping changelog");
                    return (false, null);
                }
                
                // Check if user has already seen this version
                string changelogSeenPath = GetChangelogSeenFilePath();
                Debug.WriteLine($"[ChangelogService] Changelog seen path: {changelogSeenPath}");
                Debug.WriteLine($"[ChangelogService] Changelog seen file exists: {File.Exists(changelogSeenPath)}");
                
                if (File.Exists(changelogSeenPath))
                {
                    try
                    {
                        string seenVersion = await File.ReadAllTextAsync(changelogSeenPath);
                        seenVersion = seenVersion.Trim();
                        
                        Debug.WriteLine($"[ChangelogService] Seen version: {seenVersion}");
                        Debug.WriteLine($"[ChangelogService] Current version: {CurrentVersion}");
                        
                        if (seenVersion == CurrentVersion)
                        {
                            Debug.WriteLine($"[ChangelogService] User has already seen version {CurrentVersion} - no changelog needed");
                            return (false, null);
                        }
                        else
                        {
                            Debug.WriteLine($"[ChangelogService] Version changed from {seenVersion} to {CurrentVersion}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChangelogService] Error reading changelog-seen file: {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine("[ChangelogService] Existing user without changelog tracking - showing changelog");
                }
                
                // Load and show changelog
                Debug.WriteLine("[ChangelogService] Loading changelog data...");
                var changelogData = await LoadChangelogAsync(CurrentVersion);
                if (changelogData != null)
                {
                    Debug.WriteLine($"[ChangelogService] ✅ Changelog loaded successfully");
                    Debug.WriteLine($"[ChangelogService] HeaderImage: {changelogData.HeaderImage}");
                    return (true, changelogData);
                }
                else
                {
                    Debug.WriteLine($"[ChangelogService] ❌ Failed to load changelog");
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChangelogService] Error checking changelog: {ex.Message}");
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
                    // Attempt to resolve the macOS .app bundle structure robustly
                    var baseDir = AppContext.BaseDirectory;
                    var currentDir = new DirectoryInfo(baseDir);
                    DirectoryInfo? contentsDir = null;
                    
                    // Walk up the directory tree to find "Contents" within a ".app" bundle
                    while (currentDir != null)
                    {
                        if (string.Equals(currentDir.Name, "Contents", StringComparison.OrdinalIgnoreCase) &&
                            currentDir.Parent != null &&
                            currentDir.Parent.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                        {
                            contentsDir = currentDir;
                            break;
                        }
                        currentDir = currentDir.Parent;
                    }
                    
                    if (contentsDir != null)
                    {
                        changelogPath = Path.Combine(contentsDir.FullName, "Resources", "Changelogs", $"{version}.json");
                    }
                    else
                    {
                        // If we cannot detect the bundle layout, fall back to the standard location
                        changelogPath = Path.Combine(baseDir, "Changelogs", $"{version}.json");
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

                if (data is null)
                {
                    Debug.WriteLine($"[ChangelogService] ❌ Failed to deserialize changelog from file: {changelogPath}");
                    return null;
                }

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
                
                await _changelogFileSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    await File.WriteAllTextAsync(changelogSeenPath, CurrentVersion).ConfigureAwait(false);
                    
                    Debug.WriteLine($"[ChangelogService] ✅ Successfully marked version {CurrentVersion} as seen");
                    
                    // Verify it was written
                    if (File.Exists(changelogSeenPath))
                    {
                        string written = await File.ReadAllTextAsync(changelogSeenPath).ConfigureAwait(false);
                        Debug.WriteLine($"[ChangelogService] ✅ Verified file contents: {written}");
                    }
                    else
                    {
                        Debug.WriteLine($"[ChangelogService] ❌ File does not exist after write!");
                    }
                }
                finally
                {
                    _changelogFileSemaphore.Release();
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
