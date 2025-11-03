// Services/KeychainService.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public static class KeychainService
    {
        private const string ServiceName = "AkademiTrack";

        public static async Task SaveToKeychain(string key, string value)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                throw new PlatformNotSupportedException("Keychain is only available on macOS");

            await DeleteFromKeychain(key); // Unngå duplikat

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/security",
                    Arguments = $"add-generic-password -a \"{Escape(key)}\" -s \"{ServiceName}\" -w \"{Escape(value)}\" -U",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Keychain save failed: {error}");
        }

        public static async Task<string?> LoadFromKeychain(string key)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                throw new PlatformNotSupportedException("Keychain is only available on macOS");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/security",
                    Arguments = $"find-generic-password -a \"{Escape(key)}\" -s \"{ServiceName}\" -w",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                var trimmed = output.Trim();
                
                // ONLY FIX: Check if it's hex (for cookies JSON) and decode it
                if (key == "cookies" && IsHexString(trimmed))
                {
                    try
                    {
                        return HexToString(trimmed);
                    }
                    catch
                    {
                        // If hex decode fails, return as-is
                        return trimmed;
                    }
                }
                
                // For credentials (email, password, school), return as-is
                return trimmed;
            }

            if (error.Contains("could not be found"))
                return null;

            throw new InvalidOperationException($"Keychain load failed: {error}");
        }

        public static async Task DeleteFromKeychain(string key)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/security",
                    Arguments = $"delete-generic-password -a \"{Escape(key)}\" -s \"{ServiceName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            // Ignorer feil hvis ikke finnes
        }

        private static string Escape(string input)
        {
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("$", "\\$")
                .Replace("`", "\\`");
        }

        private static bool IsHexString(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 10)
                return false;

            // Check if all characters are hex digits
            foreach (char c in input)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }

            return true;
        }

        private static string HexToString(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }
}