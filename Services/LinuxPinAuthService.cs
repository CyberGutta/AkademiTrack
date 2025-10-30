using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class LinuxPinAuthService : ISecureAuthService
    {
        public async Task<bool> AuthenticateAsync(string reason)
        {
            try
            {
                // Try zenity first
                var psi = new ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = $"--entry --hide-text --title=\"AkademiTrack\" --text=\"{reason}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string pin = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return !string.IsNullOrWhiteSpace(pin);
                }
            }
            catch
            {
                // Fallback to terminal prompt
                try
                {
                    Console.WriteLine($"🔐 {reason}");
                    Console.Write("Enter PIN: ");
                    string pin = ReadHiddenInput();
                    return !string.IsNullOrWhiteSpace(pin);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private string ReadHiddenInput()
        {
            var input = string.Empty;
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                    break;

                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input = input[..^1];
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    input += key.KeyChar;
                }
            } while (true);

            Console.WriteLine();
            return input;
        }
    }
}
