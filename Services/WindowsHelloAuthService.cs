using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class WindowsHelloAuthService : ISecureAuthService
    {
        public async Task<bool> AuthenticateAsync(string reason)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            var binaryPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Helpers", "WinHelloAuth.exe");

            if (!File.Exists(binaryPath))
                return false;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
    }
}
