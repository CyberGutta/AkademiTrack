using AkademiTrack.Services;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public interface ISecureAuthService
{
    Task<bool> AuthenticateAsync(string reason);
}

public static class PlatformAuthFactory
{
    public static ISecureAuthService Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsHelloAuthService(); // Implement this
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacOSAuthService(); // Implement this
        return new LinuxPinAuthService(); // Implement this
    }
}