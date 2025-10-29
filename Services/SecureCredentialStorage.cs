using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    /// <summary>
    /// Secure credential storage using platform-specific APIs:
    /// - macOS: Keychain
    /// - Windows: DPAPI (Data Protection API)
    /// - Linux: Secret Service API (libsecret)
    /// </summary>
    public static class SecureCredentialStorage
    {
        private const string ServiceName = "AkademiTrack";

        /// <summary>
        /// Stores a credential securely using the platform's credential manager
        /// </summary>
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

        /// <summary>
        /// Deletes a credential from the platform's credential manager
        /// </summary>
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

        #region macOS Keychain

        private static async Task<bool> SaveToMacOSKeychainAsync(string key, string value)
        {
            try
            {
                await DeleteFromMacOSKeychainAsync(key);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "security",
                        Arguments = $"add-generic-password -s \"{ServiceName}\" -a \"{key}\" -w \"{value}\" -U",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                var success = process.ExitCode == 0;
                if (!success)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    Debug.WriteLine($"Keychain save failed: {error}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"macOS Keychain save error: {ex.Message}");
                return false;
            }
        }

        private static async Task<string?> GetFromMacOSKeychainAsync(string key)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "security",
                        Arguments = $"find-generic-password -s \"{ServiceName}\" -a \"{key}\" -w",
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
                    return output.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"macOS Keychain get error: {ex.Message}");
                return null;
            }
        }

        private static async Task<bool> DeleteFromMacOSKeychainAsync(string key)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "security",
                        Arguments = $"delete-generic-password -s \"{ServiceName}\" -a \"{key}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                return process.ExitCode == 0 || process.ExitCode == 44;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"macOS Keychain delete error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Windows Credential Manager (DPAPI)

#if WINDOWS
        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn,
            string? szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            ref DATA_BLOB pDataOut);

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            StringBuilder? szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            ref DATA_BLOB pDataOut);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }
#endif

        private static bool SaveToWindowsCredentialManager(string key, string value)
        {
#if WINDOWS
            try
            {
                var valueBytes = Encoding.UTF8.GetBytes(value);
                var dataIn = new DATA_BLOB();
                var dataOut = new DATA_BLOB();

                var handle = GCHandle.Alloc(valueBytes, GCHandleType.Pinned);
                try
                {
                    dataIn.pbData = handle.AddrOfPinnedObject();
                    dataIn.cbData = valueBytes.Length;

                    bool success = CryptProtectData(
                        ref dataIn,
                        $"{ServiceName}:{key}",
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        0,
                        ref dataOut);

                    if (success && dataOut.pbData != IntPtr.Zero)
                    {
                        var encryptedBytes = new byte[dataOut.cbData];
                        Marshal.Copy(dataOut.pbData, encryptedBytes, 0, dataOut.cbData);
                        Marshal.FreeHGlobal(dataOut.pbData);

                        // Store in registry
                        using var regKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                            $@"SOFTWARE\{ServiceName}\Credentials");
                        
                        regKey?.SetValue(key, Convert.ToBase64String(encryptedBytes));
                        return true;
                    }

                    return false;
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows credential save error: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        private static string? GetFromWindowsCredentialManager(string key)
        {
#if WINDOWS
            try
            {
                using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    $@"SOFTWARE\{ServiceName}\Credentials");
                
                var encryptedBase64 = regKey?.GetValue(key) as string;
                if (string.IsNullOrEmpty(encryptedBase64))
                    return null;

                var encryptedBytes = Convert.FromBase64String(encryptedBase64);
                var dataIn = new DATA_BLOB();
                var dataOut = new DATA_BLOB();

                var handle = GCHandle.Alloc(encryptedBytes, GCHandleType.Pinned);
                try
                {
                    dataIn.pbData = handle.AddrOfPinnedObject();
                    dataIn.cbData = encryptedBytes.Length;

                    bool success = CryptUnprotectData(
                        ref dataIn,
                        null,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        0,
                        ref dataOut);

                    if (success && dataOut.pbData != IntPtr.Zero)
                    {
                        var decryptedBytes = new byte[dataOut.cbData];
                        Marshal.Copy(dataOut.pbData, decryptedBytes, 0, dataOut.cbData);
                        Marshal.FreeHGlobal(dataOut.pbData);

                        return Encoding.UTF8.GetString(decryptedBytes);
                    }

                    return null;
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows credential get error: {ex.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        private static bool DeleteFromWindowsCredentialManager(string key)
        {
#if WINDOWS
            try
            {
                using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    $@"SOFTWARE\{ServiceName}\Credentials", true);
                
                regKey?.DeleteValue(key, false);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows credential delete error: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        #endregion

        #region Linux Secret Service

        private static async Task<bool> SaveToLinuxSecretServiceAsync(string key, string value)
        {
            try
            {
                // Using secret-tool (part of libsecret)
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