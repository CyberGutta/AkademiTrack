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

            Console.WriteLine("🔐 Dette er en terminalbasert PIN-sjekk.");
            Console.WriteLine("💡 For en ekte grafisk passordopplevelse, installer zenity og bruk GUI-modus.");
            Console.WriteLine("   👉 sudo apt install zenity");

            if (!File.Exists(fullPath))
            {
                Console.WriteLine("🔐 Første gang? Sett en PIN for å beskytte tilgang.");
                Console.Write("Ny PIN: ");
                string newPin = ReadHiddenInput();

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

            Console.WriteLine($"🔐 {reason}");
            Console.Write("Enter PIN: ");
            string inputPin = ReadHiddenInput();

            string storedHash = File.ReadAllText(fullPath);
            if (Hash(inputPin) == storedHash)
            {
                return true;
            }

            Console.WriteLine("❌ Feil PIN. Tilgang nektet.");
            Console.WriteLine("💣 For å tilbakestille PIN, slett: " + fullPath);
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

        private string Hash(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
