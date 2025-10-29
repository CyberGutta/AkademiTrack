using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;
using System;

namespace AkademiTrack.Services
{
    public class MacOSAuthService : ISecureAuthService
    {
        public async Task<bool> AuthenticateAsync(string reason)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return false;

            var helperPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Helpers", "AkademiAuth");

            if (!File.Exists(helperPath))
                return false;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = helperPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
    }
}
