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
            return new WindowsHelloAuthService(); 
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacOSAuthService(); 
        return new LinuxPinAuthService(); 
    }
}