using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class LinuxPinAuthService : ISecureAuthService
    {
        private const string ConfigPath = ".akademitrack/pin.cfg";

        public async Task<bool> AuthenticateAsync(string reason)
        {
            string fullPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ConfigPath);
            bool zenityAvailable = IsZenityInstalled();

            if (!File.Exists(fullPath))
            {
                string newPin = zenityAvailable
                    ? await PromptWithZenity("Sett en ny PIN for AkademiTrack")
                    : PromptInTerminal("🔐 Første gang? Sett en PIN for å beskytte tilgang.");

                if (string.IsNullOrWhiteSpace(newPin))
                {
                    Console.WriteLine("❌ PIN kan ikke være tom.");
                    return false;
                }

                string hashed = Hash(newPin);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, hashed);
                Console.WriteLine("✅ PIN lagret. Neste gang må du bekrefte identitet.");
                return true;
            }

            string inputPin = zenityAvailable
                ? await PromptWithZenity(reason)
                : PromptInTerminal($"🔐 {reason}");

            string storedHash = File.ReadAllText(fullPath);
            if (Hash(inputPin) == storedHash)
            {
                return true;
            }

            Console.WriteLine("❌ Feil PIN. Tilgang nektet.");
            Console.WriteLine("💣 For å tilbakestille PIN, slett: " + fullPath);
            return false;
        }

        private bool IsZenityInstalled()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = "-c \"command -v zenity\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                string result = process?.StandardOutput.ReadToEnd().Trim() ?? "";
                return !string.IsNullOrEmpty(result);
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> PromptWithZenity(string message)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = $"--entry --hide-text --title=\"AkademiTrack\" --text=\"{message}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                psi.EnvironmentVariables["DISPLAY"] = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
                psi.EnvironmentVariables["XDG_RUNTIME_DIR"] = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/run/user/1000";

                using var process = Process.Start(psi);
                if (process == null)
                    throw new Exception("Zenity process failed to start.");

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    throw new Exception($"Zenity exited with code {process.ExitCode}: {error}");

                if (string.IsNullOrWhiteSpace(output))
                    throw new Exception("Zenity returned empty input.");

                return output.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Zenity GUI mislyktes: {ex.Message}");
                Console.WriteLine("💡 Bruker terminal i stedet.");
                return PromptInTerminal(message);
            }
        }

        private string PromptInTerminal(string message)
        {
            Console.WriteLine(message);
            Console.WriteLine("💡 For grafisk PIN-dialog, installer zenity: sudo apt install zenity");
            Console.Write("Enter PIN: ");
            return ReadHiddenInput();
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

        private string Hash(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
