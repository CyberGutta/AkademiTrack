using System;
using System.IO;
using System.Text.Json;

namespace AkademiTrack.Services.Configuration
{
    /// <summary>
    /// Application configuration loaded from environment variables and config files
    /// </summary>
    public class AppConfiguration
    {
        public string SupabaseUrl { get; set; } = "https://zqqxqyxozyydhotfuurc.supabase.co";
        public string? SupabaseAnonKey { get; set; }
        public int MaxRetries { get; set; } = 3;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MonitoringIntervalSeconds { get; set; } = 30;
        public int WakeDetectionThresholdMinutes { get; set; } = 5;
        public int LogRetentionDays { get; set; } = 7;
        public int MaxLogEntries { get; set; } = 10000;

        private static AppConfiguration? _instance;
        private static readonly object _lock = new object();

        public static AppConfiguration Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = LoadConfiguration();
                        }
                    }
                }
                return _instance;
            }
        }

        private static AppConfiguration LoadConfiguration()
        {
            var config = new AppConfiguration();

            // Try to load from environment variables first (most secure)
            config.SupabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");

            // If not in environment, try config file
            if (string.IsNullOrEmpty(config.SupabaseAnonKey))
            {
                var configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                {
                    try
                    {
                        var json = File.ReadAllText(configPath);
                        var fileConfig = JsonSerializer.Deserialize<AppConfiguration>(json);
                        if (fileConfig != null)
                        {
                            config = fileConfig;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load config file: {ex.Message}");
                    }
                }
            }

            // Fallback to hardcoded key (not ideal, but ensures app works)
            if (string.IsNullOrEmpty(config.SupabaseAnonKey))
            {
                System.Diagnostics.Debug.WriteLine("⚠️ WARNING: Using fallback Supabase key. Set SUPABASE_ANON_KEY environment variable for better security.");
                config.SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InpxcXhxeXhvenl5ZGhvdGZ1dXJjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Njc2NzkyNTcsImV4cCI6MjA4MzI1NTI1N30.AeCL4DFJzggZ68JGCgYai7XDniWIAUMt_5zAkjNe6OA";
            }

            return config;
        }

        private static string GetConfigFilePath()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "app_config.json");
        }

        public void SaveToFile()
        {
            try
            {
                var configPath = GetConfigFilePath();
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
            }
        }
    }
}
