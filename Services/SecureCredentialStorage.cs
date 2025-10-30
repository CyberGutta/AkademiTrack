using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

#if WINDOWS
using Microsoft.Win32;
using System.Security.Cryptography;
#endif

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

#if WINDOWS
        private static bool SaveToWindowsCredentialManager(string key, string value)
        {
            try
            {
                byte[] userData = Encoding.UTF8.GetBytes(value);
                byte[] entropy = Encoding.UTF8.GetBytes("AkademiTrackEntropy");
                byte[] encryptedData = ProtectedData.Protect(userData, entropy, DataProtectionScope.CurrentUser);

                using var regKey = Registry.CurrentUser.CreateSubKey(
                    $@"SOFTWARE\{ServiceName}\Credentials");

                regKey?.SetValue(key, Convert.ToBase64String(encryptedData));
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows credential save error: {ex.Message}");
                return false;
            }
        }

        private static string? GetFromWindowsCredentialManager(string key)
        {
            try
            {
                using var regKey = Registry.CurrentUser.OpenSubKey(
                    $@"SOFTWARE\{ServiceName}\Credentials");

                if (regKey == null) return null;

                var base64 = regKey.GetValue(key) as string;
                if (string.IsNullOrEmpty(base64)) return null;

                byte[] encryptedData = Convert.FromBase64String(base64);
                byte[] entropy = Encoding.UTF8.GetBytes("AkademiTrackEntropy");

                byte[] decryptedData = ProtectedData.Unprotect(encryptedData, entropy, DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decryptedData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows credential get error: {ex.Message}");
                return null;
            }
        }

        private static bool DeleteFromWindowsCredentialManager(string key)
        {
            try
            {
                using var regKey = Registry.CurrentUser.OpenSubKey(
                    $@"SOFTWARE\{ServiceName}\Credentials", true);
                
                regKey?.DeleteValue(key, false);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows credential delete error: {ex.Message}");
                return false;
            }
        }
#else
        private static bool SaveToWindowsCredentialManager(string key, string value) => false;
        private static string? GetFromWindowsCredentialManager(string key) => null;
        private static bool DeleteFromWindowsCredentialManager(string key) => false;
#endif

        #endregion

        #region Linux Secret Service

        private static async Task<bool> SaveToLinuxSecretServiceAsync(string key, string value)
        {
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

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Linux Secret Service save error: {ex.Message}");
                Debug.WriteLine("Note: libsecret-tools may not be installed");
                return false;
            }
        }

        private static async Task<string?> GetFromLinuxSecretServiceAsync(string key)
        {
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
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.TrimEnd('\n', '\r');
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Linux Secret Service get error: {ex.Message}");
                return null;
            }
        }

        private static async Task<bool> DeleteFromLinuxSecretServiceAsync(string key)
        {
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

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Linux Secret Service delete error: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}