using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


namespace AkademiTrack.Services
{
    public static class SecureCredentialStorage
    {
        private const string ServiceName = "AkademiTrack";

        public static async Task<bool> SaveCredentialAsync(string key, string value)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return await SaveToMacOSKeychainAsync(key, value);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return SaveToWindowsCredentialManager(key, value);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return await SaveToLinuxSecretServiceAsync(key, value);

                Debug.WriteLine("Unsupported platform for secure storage");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving credential: {ex.Message}");
                return false;
            }
        }

        public static async Task<string?> GetCredentialAsync(string key)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return await GetFromMacOSKeychainAsync(key);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return GetFromWindowsCredentialManager(key);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return await GetFromLinuxSecretServiceAsync(key);

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error retrieving credential: {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> DeleteCredentialAsync(string key)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return await DeleteFromMacOSKeychainAsync(key);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return DeleteFromWindowsCredentialManager(key);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return await DeleteFromLinuxSecretServiceAsync(key);

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting credential: {ex.Message}");
                return false;
            }
        }

        #region macOS Keychain (via /usr/bin/security)

        private static async Task<bool> SaveToMacOSKeychainAsync(string key, string value)
        {
            try
            {
                await KeychainService.SaveToKeychain(key, value);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Keychain save error ({key}): {ex.Message}");
                return false;
            }
        }

        private static async Task<string?> GetFromMacOSKeychainAsync(string key)
        {
            try
            {
                return await KeychainService.LoadFromKeychain(key);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Keychain load error ({key}): {ex.Message}");
                return null;
            }
        }

        private static async Task<bool> DeleteFromMacOSKeychainAsync(string key)
        {
            try
            {
                await KeychainService.DeleteFromKeychain(key);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Keychain delete error ({key}): {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Windows Credential Manager (DPAPI)

        private static bool SaveToWindowsCredentialManager(string key, string value)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine("⚠️ SaveToWindowsCredentialManager skipped: not running on Windows.");
                return false;
            }

            Debug.WriteLine($"🔐 Saving credential: {key}");

            try
            {
                byte[] userData = Encoding.UTF8.GetBytes(value);
                byte[] entropy = Encoding.UTF8.GetBytes("AkademiTrackEntropy");
                byte[] encryptedData = ProtectedData.Protect(userData, entropy, DataProtectionScope.CurrentUser);

                using var regKey = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\AkademiTrack\Credentials");
                if (regKey == null)
                {
                    Debug.WriteLine("❌ Failed to create registry path.");
                    return false;
                }

                regKey.SetValue(key, Convert.ToBase64String(encryptedData));
                Debug.WriteLine("✅ Credential saved successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Windows credential save error: {ex.Message}");
                return false;
            }
        }

        private static string? GetFromWindowsCredentialManager(string key)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine("⚠️ GetFromWindowsCredentialManager skipped: not running on Windows.");
                return null;
            }

            Debug.WriteLine($"🔍 Retrieving credential: {key}");

            try
            {
                using var regKey = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\AkademiTrack\Credentials");
                if (regKey == null)
                {
                    Debug.WriteLine("❌ Failed to open or create registry path.");
                    return null;
                }

                var base64 = regKey.GetValue(key) as string;
                if (string.IsNullOrEmpty(base64))
                {
                    Debug.WriteLine("⚠️ No credential found for key.");
                    return null;
                }

                byte[] encryptedData = Convert.FromBase64String(base64);
                byte[] entropy = Encoding.UTF8.GetBytes("AkademiTrackEntropy");
                byte[] decryptedData = ProtectedData.Unprotect(encryptedData, entropy, DataProtectionScope.CurrentUser);

                string result = Encoding.UTF8.GetString(decryptedData);
                Debug.WriteLine("✅ Credential retrieved successfully.");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Windows credential get error: {ex.Message}");
                return null;
            }
        }

        private static bool DeleteFromWindowsCredentialManager(string key)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debug.WriteLine("⚠️ DeleteFromWindowsCredentialManager skipped: not running on Windows.");
                return false;
            }

            Debug.WriteLine($"🗑️ Deleting credential: {key}");

            try
            {
                using var regKey = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\AkademiTrack\Credentials");
                if (regKey == null)
                {
                    Debug.WriteLine("❌ Failed to open or create registry path.");
                    return false;
                }

                regKey.DeleteValue(key, false);
                Debug.WriteLine("✅ Credential deleted.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Windows credential delete error: {ex.Message}");
                return false;
            }
        }

        public static async Task SaveCookiesAsync(Cookie[] cookies)
        {
            string json = JsonSerializer.Serialize(cookies);
            await SaveCredentialAsync("cookies", json);
        }

        public static async Task<Dictionary<string, string>> LoadCookiesAsync()
        {
            var json = await GetCredentialAsync("cookies");

            if (string.IsNullOrEmpty(json))
                return null!;

            var cookieArray = JsonSerializer.Deserialize<Cookie[]>(json);
            return cookieArray?.ToDictionary(c => c.Name ?? "", c => c.Value ?? "") ?? null!;
        }

        public static async Task DeleteCookiesAsync()
        {
            await DeleteCredentialAsync("cookies");
        }

        #endregion

        #region Linux Secret Service + Fallback

        private static readonly string FallbackPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".akademitrack", "creds.json");

        private static async Task<bool> SaveToLinuxSecretServiceAsync(string key, string value)
        {
            if (!File.Exists("/usr/bin/secret-tool"))
            {
                Console.WriteLine("⚠️ secret-tool not found. Using fallback.");
                return SaveToFallbackFile(key, value);
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "secret-tool",
                        Arguments = $"store --label=\"{ServiceName} - {key}\" service {ServiceName} account {key}",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.StandardInput.WriteLineAsync(value);
                process.StandardInput.Close();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine("⚠️ secret-tool failed. Using fallback.");
                    return SaveToFallbackFile(key, value);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ secret-tool error: {ex.Message}");
                return SaveToFallbackFile(key, value);
            }
        }

        private static async Task<string?> GetFromLinuxSecretServiceAsync(string key)
        {
            if (!File.Exists("/usr/bin/secret-tool"))
            {
                Console.WriteLine("⚠️ secret-tool not found. Using fallback.");
                return GetFromFallbackFile(key);
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "secret-tool",
                        Arguments = $"lookup service {ServiceName} account {key}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine("⚠️ secret-tool failed. Using fallback.");
                    return GetFromFallbackFile(key);
                }

                return output.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ secret-tool error: {ex.Message}");
                return GetFromFallbackFile(key);
            }
        }

        private static async Task<bool> DeleteFromLinuxSecretServiceAsync(string key)
        {
            if (!File.Exists("/usr/bin/secret-tool"))
            {
                Console.WriteLine("⚠️ secret-tool not found. Using fallback.");
                return DeleteFromFallbackFile(key);
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "secret-tool",
                        Arguments = $"clear service {ServiceName} account {key}",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine("⚠️ secret-tool failed. Using fallback.");
                    return DeleteFromFallbackFile(key);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ secret-tool error: {ex.Message}");
                return DeleteFromFallbackFile(key);
            }
        }

        #endregion

        #region Fallback File Storage (AES)

        private static byte[] Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            byte[] result = new byte[aes.IV.Length + encrypted.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
            return result;
        }

        private static string Decrypt(byte[] encryptedData)
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKey();

            byte[] iv = new byte[aes.BlockSize / 8];
            byte[] cipher = new byte[encryptedData.Length - iv.Length];

            Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(encryptedData, iv.Length, cipher, 0, cipher.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            byte[] decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(decrypted);
        }

        private static byte[] DeriveKey()
        {
            string baseKey = Environment.MachineName + "-AkademiTrack";
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(baseKey));
        }

        private static bool SaveToFallbackFile(string key, string value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FallbackPath)!);
                var dict = File.Exists(FallbackPath)
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FallbackPath)) ?? new()
                    : new Dictionary<string, string>();

                dict[key] = Convert.ToBase64String(Encrypt(value));
                File.WriteAllText(FallbackPath, System.Text.Json.JsonSerializer.Serialize(dict));
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fallback save error: {ex.Message}");
                return false;
            }
        }

        private static string? GetFromFallbackFile(string key)
        {
            try
            {
                if (!File.Exists(FallbackPath)) return null;

                string jsonContent = File.ReadAllText(FallbackPath);

                if (string.IsNullOrWhiteSpace(jsonContent))
                    return null;

                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
                if (dict == null || !dict.ContainsKey(key)) return null;

                return Decrypt(Convert.FromBase64String(dict[key]));
            }
            catch (System.Text.Json.JsonException ex)
            {
                Console.WriteLine($"❌ Corrupted credentials file. Backing up and creating fresh file.");
                Console.WriteLine($"   Error: {ex.Message}");

                if (File.Exists(FallbackPath))
                {
                    string backupPath = FallbackPath + ".corrupted." + DateTime.Now.Ticks;
                    File.Move(FallbackPath, backupPath);
                    Console.WriteLine($"   Old file backed up to: {backupPath}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fallback get error: {ex.Message}");
                return null;
            }
        }

        private static bool DeleteFromFallbackFile(string key)
        {
            try
            {
                if (!File.Exists(FallbackPath)) return false;

                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FallbackPath));
                if (dict == null || !dict.ContainsKey(key)) return false;

                dict.Remove(key);
                File.WriteAllText(FallbackPath, System.Text.Json.JsonSerializer.Serialize(dict));
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fallback delete error: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
