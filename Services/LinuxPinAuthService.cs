using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class LinuxPinAuthService : ISecureAuthService
    {
        public async Task<bool> AuthenticateAsync(string reason)
        {
            await Task.Delay(100);
            return true;
        }
    }
}
