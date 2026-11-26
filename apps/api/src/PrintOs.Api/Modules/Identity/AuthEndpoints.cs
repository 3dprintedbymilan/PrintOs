using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintOs.Api.Infrastructure.Persistence;

namespace PrintOs.Api.Modules.Identity;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/sign-up", SignUpAsync).AllowAnonymous();
        group.MapPost("/sign-in", SignInAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/sign-out", SignOutAsync).RequireAuthorization();
    }

    private static async Task<IResult> SignUpAsync(
        SignUpRequest request,
        UserManager<ApplicationUser> userManager,
        PrintOsDbContext dbContext)
    {
        var errors = ValidateSignUp(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var workspace = new Workspace { Name = request.WorkspaceName.Trim() };
        dbContext.Set<Workspace>().Add(workspace);
        await dbContext.SaveChangesAsync();

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            WorkspaceId = workspace.Id
        };

        var creation = await userManager.CreateAsync(user, request.Password);
        if (!creation.Succeeded)
        {
            await transaction.RollbackAsync();
            return TypedResults.ValidationProblem(creation.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }

        await transaction.CommitAsync();
        return TypedResults.Created("/api/workspace", new WorkspaceSummary(workspace.Id, workspace.Name));
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        SignInManager<ApplicationUser> signInManager)
    {
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;

        var result = await signInManager.PasswordSignInAsync(
            request.Email.Trim(), request.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return TypedResults.Problem(
                title: "Account temporarily locked after too many failed attempts.",
                statusCode: StatusCodes.Status423Locked);
        }

        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                title: "Invalid email or password.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return TypedResults.Empty;
    }

    private static async Task<IResult> RefreshAsync(
        RefreshTokenRequest request,
        SignInManager<ApplicationUser> signInManager,
        IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
        TimeProvider timeProvider)
    {
        var refreshTokenProtector = bearerTokenOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var refreshTicket = refreshTokenProtector.Unprotect(request.RefreshToken);

        if (refreshTicket?.Properties.ExpiresUtc is not { } expiresUtc
            || timeProvider.GetUtcNow() >= expiresUtc
            || await signInManager.ValidateSecurityStampAsync(refreshTicket.Principal) is not { } user)
        {
            return TypedResults.Problem(
                title: "The refresh token is invalid or expired.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static async Task<IResult> SignOutAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        if (await userManager.GetUserAsync(principal) is { } user)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        return TypedResults.NoContent();
    }

    private static Dictionary<string, string[]> ValidateSignUp(SignUpRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceName))
        {
            errors["workspaceName"] = ["A workspace name is required."];
        }
        else if (request.WorkspaceName.Trim().Length > 200)
        {
            errors["workspaceName"] = ["The workspace name must be 200 characters or fewer."];
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors["password"] = ["A password is required."];
        }

        return errors;
    }
}
