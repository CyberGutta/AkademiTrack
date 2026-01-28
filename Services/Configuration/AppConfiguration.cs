using System;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services.Configuration
{
    /// <summary>
    /// Secure application configuration with encrypted storage and environment variable support
    /// </summary>
    public class AppConfiguration
    {
        public string SupabaseUrl { get; set; } = "https://zqqxqyxozyydhotfuurc.supabase.co";
        
        // Never store the actual key in memory as plain text
        private string? _encryptedSupabaseKey;
        
        public int MaxRetries { get; set; } = 3;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MonitoringIntervalSeconds { get; set; } = 30;
        public int WakeDetectionThresholdMinutes { get; set; } = 5;
        public int LogRetentionDays { get; set; } = 7;
        public int MaxLogEntries { get; set; } = 10000;

        private static AppConfiguration? _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the Supabase anonymous key from secure storage
        /// </summary>
        public string? SupabaseAnonKey
        {
            get
            {
                if (string.IsNullOrEmpty(_encryptedSupabaseKey))
                    return null;
                
                try
                {
                    return DecryptString(_encryptedSupabaseKey);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to decrypt Supabase key: {ex.Message}");
                    return null;
                }
            }
            private set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _encryptedSupabaseKey = null;
                    return;
                }
                
                try
                {
                    _encryptedSupabaseKey = EncryptString(value);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to encrypt Supabase key: {ex.Message}");
                    _encryptedSupabaseKey = null;
                }
            }
        }

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

            // 1. Try environment variables first (most secure for production)
            var envKey = Environment.GetEnvironmentVariable("AKADEMITRACK_SUPABASE_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                config.SupabaseAnonKey = envKey;
                System.Diagnostics.Debug.WriteLine("✓ Loaded Supabase key from environment variable");
                return config;
            }

            // 2. Try secure credential storage (keychain/credential manager) - ASYNC SAFE
            try
            {
                var storedKey = Task.Run(async () => await LoadFromSecureStorage()).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(storedKey))
                {
                    config.SupabaseAnonKey = storedKey;
                    System.Diagnostics.Debug.WriteLine("✓ Loaded Supabase key from secure storage");
                    return config;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load from secure storage: {ex.Message}");
            }

            // 3. Try encrypted config file
            try
            {
                var fileKey = LoadFromEncryptedFile();
                if (!string.IsNullOrEmpty(fileKey))
                {
                    config.SupabaseAnonKey = fileKey;
                    System.Diagnostics.Debug.WriteLine("✓ Loaded Supabase key from encrypted config file");
                    return config;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load from encrypted file: {ex.Message}");
            }

            // 4. Last resort: Use a temporary fallback for development
            System.Diagnostics.Debug.WriteLine("   CRITICAL: No Supabase API key found!");
            System.Diagnostics.Debug.WriteLine("   Set AKADEMITRACK_SUPABASE_KEY environment variable");
            System.Diagnostics.Debug.WriteLine("   or configure through settings UI");
            System.Diagnostics.Debug.WriteLine("   Using temporary fallback for development...");
            
            // Temporary fallback to prevent crash - remove in production
            config.SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InpxcXhxeXhvenl5ZGhvdGZ1dXJjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Njc2NzkyNTcsImV4cCI6MjA4MzI1NTI1N30.AeCL4DFJzggZ68JGCgYai7XDniWIAUMt_5zAkjNe6OA";
            System.Diagnostics.Debug.WriteLine("⚠️ WARNING: Using fallback API key - configure proper key for production");
            
            return config;
        }

        private static async System.Threading.Tasks.Task<string?> LoadFromSecureStorage()
        {
            try
            {
                return await SecureCredentialStorage.GetCredentialAsync("supabase_api_key");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Secure storage access failed: {ex.Message}");
                return null;
            }
        }

        private static string? LoadFromEncryptedFile()
        {
            var configPath = GetEncryptedConfigPath();
            if (!File.Exists(configPath))
                return null;

            try
            {
                var encryptedData = File.ReadAllBytes(configPath);
                return DecryptString(Convert.ToBase64String(encryptedData));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to decrypt config file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves the API key to secure storage for future use
        /// </summary>
        public async System.Threading.Tasks.Task<bool> SaveApiKeySecurelyAsync(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return false;

            try
            {
                // Save to secure credential storage
                var success = await SecureCredentialStorage.SaveCredentialAsync("supabase_api_key", apiKey);
                if (success)
                {
                    SupabaseAnonKey = apiKey; // This will encrypt it in memory
                    System.Diagnostics.Debug.WriteLine("✓ API key saved to secure storage");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save to secure storage: {ex.Message}");
            }

            // Fallback: save to encrypted file
            try
            {
                var configPath = GetEncryptedConfigPath();
                var encryptedData = Convert.FromBase64String(EncryptString(apiKey));
                await File.WriteAllBytesAsync(configPath, encryptedData);
                
                SupabaseAnonKey = apiKey;
                System.Diagnostics.Debug.WriteLine("✓ API key saved to encrypted file");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save to encrypted file: {ex.Message}");
                return false;
            }
        }

        private static string EncryptString(string plainText)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use Windows DPAPI
                var data = Encoding.UTF8.GetBytes(plainText);
                var entropy = Encoding.UTF8.GetBytes("AkademiTrack-Config-Salt");
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(data, entropy, 
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            else
            {
                // Use AES for other platforms
                using var aes = Aes.Create();
                aes.Key = DeriveKey();
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                var result = new byte[aes.IV.Length + encrypted.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
                return Convert.ToBase64String(result);
            }
        }

        private static string DecryptString(string encryptedText)
        {
            var encryptedData = Convert.FromBase64String(encryptedText);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use Windows DPAPI
                var entropy = Encoding.UTF8.GetBytes("AkademiTrack-Config-Salt");
                var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(encryptedData, entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            else
            {
                // Use AES for other platforms
                using var aes = Aes.Create();
                aes.Key = DeriveKey();

                var iv = new byte[aes.BlockSize / 8];
                var cipher = new byte[encryptedData.Length - iv.Length];

                Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(encryptedData, iv.Length, cipher, 0, cipher.Length);
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                var decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                return Encoding.UTF8.GetString(decrypted);
            }
        }

        private static byte[] DeriveKey()
        {
            // Create a machine-specific key
            var baseKey = Environment.MachineName + "-AkademiTrack-Config-" + Environment.UserName;
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(baseKey));
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

        private static string GetEncryptedConfigPath()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "secure_config.dat");
        }

        public void SaveToFile()
        {
            try
            {
                var configPath = GetConfigFilePath();
                
                // Create a safe version without sensitive data
                var safeConfig = new
                {
                    SupabaseUrl,
                    MaxRetries,
                    RequestTimeoutSeconds,
                    MonitoringIntervalSeconds,
                    WakeDetectionThresholdMinutes,
                    LogRetentionDays,
                    MaxLogEntries,
                    // Never save the API key to plain text file
                    Note = "API key stored securely in system credential store"
                };
                
                var json = JsonSerializer.Serialize(safeConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates that the configuration is properly set up
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(SupabaseAnonKey) && 
                   !string.IsNullOrEmpty(SupabaseUrl) &&
                   MaxRetries > 0 &&
                   RequestTimeoutSeconds > 0;
        }

        /// <summary>
        /// Clears sensitive data from memory and storage
        /// </summary>
        public async System.Threading.Tasks.Task ClearSensitiveDataAsync()
        {
            try
            {
                // Clear from secure storage
                await SecureCredentialStorage.DeleteCredentialAsync("supabase_api_key");
                
                // Clear encrypted file
                var encryptedPath = GetEncryptedConfigPath();
                if (File.Exists(encryptedPath))
                {
                    File.Delete(encryptedPath);
                }
                
                // Clear from memory
                _encryptedSupabaseKey = null;
                
                System.Diagnostics.Debug.WriteLine("✓ Sensitive configuration data cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear sensitive data: {ex.Message}");
            }
        }
    }
}
