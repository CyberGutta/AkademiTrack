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
                var check = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "zenity",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(check);
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

                using var process = Process.Start(psi);
                if (process == null)
                    return "";

                string pin = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return pin.Trim();
            }
            catch
            {
                Console.WriteLine("⚠️ Zenity GUI mislyktes. Bruker terminal i stedet.");
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
