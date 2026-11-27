using Microsoft.AspNetCore.DataProtection;

namespace PrintOs.Api.Modules.ModelLibrary;

public enum SignedFileKind
{
    ModelVersion,
    Thumbnail,
    LicenseProof,
}

public sealed record SignedFileClaim(SignedFileKind Kind, Guid WorkspaceId, Guid Id);

public sealed class ModelFileSigner(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector("PrintOs.ModelFiles").ToTimeLimitedDataProtector();

    public string Sign(SignedFileKind kind, Guid workspaceId, Guid id, TimeSpan lifetime) =>
        protector.Protect($"{(int)kind}|{workspaceId}|{id}", lifetime);

    public SignedFileClaim? Verify(string token)
    {
        try
        {
            var parts = protector.Unprotect(token).Split('|');
            if (parts.Length == 3
                && int.TryParse(parts[0], out var kind)
                && Enum.IsDefined((SignedFileKind)kind)
                && Guid.TryParse(parts[1], out var workspaceId)
                && Guid.TryParse(parts[2], out var id))
            {
                return new SignedFileClaim((SignedFileKind)kind, workspaceId, id);
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
        }

        return null;
    }
}
