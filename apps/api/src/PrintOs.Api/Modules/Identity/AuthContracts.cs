namespace PrintOs.Api.Modules.Identity;

public sealed record SignUpRequest(string Email, string Password, string WorkspaceName);

public sealed record SignInRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record WorkspaceSummary(Guid Id, string Name);
