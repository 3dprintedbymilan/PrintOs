using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace PrintOs.Api.Modules.Bambu;

public sealed class BambuTokenProtector(IDataProtectionProvider provider) : IBambuTokenProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("PrintOs.Bambu.AccessToken.v1");

    public string Protect(string accessToken) => protector.Protect(accessToken);

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
