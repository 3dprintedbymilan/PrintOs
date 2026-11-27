using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Account;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/account")
            .WithTags("Account")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);

        group.MapPost("/export", ExportAsync);
        group.MapPost("/delete", DeleteAsync);
    }

    private static async Task<IResult> ExportAsync(
        ExportAccountRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ICurrentWorkspace currentWorkspace,
        ExportRateLimiter rateLimiter,
        WorkspaceExporter exporter,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        var failure = await ReconfirmPasswordAsync(userManager, principal, workspaceId, request.Password);
        if (failure is not null || workspaceId is null)
        {
            return failure ?? TypedResults.Forbid();
        }

        if (!rateLimiter.TryAcquire(workspaceId.Value, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return TypedResults.Problem(
                title: "Too many exports. Try again later.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var response = httpContext.Response;
        response.ContentType = "application/zip";
        response.Headers.ContentDisposition = $"attachment; filename=\"printos-export-{timeProvider.GetUtcNow():yyyyMMdd-HHmm}.zip\"";
        response.Headers.CacheControl = "no-store";
        httpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>()?.AllowSynchronousIO = true;
        await exporter.WriteZipAsync(response.Body, cancellationToken);
        return TypedResults.Empty;
    }

    private static async Task<IResult> DeleteAsync(
        DeleteAccountRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        ICurrentWorkspace currentWorkspace,
        PrintOsDbContext dbContext,
        WorkspaceEraser eraser,
        CancellationToken cancellationToken)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        if (workspaceId is null)
        {
            return TypedResults.Forbid();
        }

        var workspaceName = await dbContext.Set<Workspace>()
            .Where(candidate => candidate.Id == workspaceId)
            .Select(candidate => candidate.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (workspaceName is null)
        {
            return TypedResults.NotFound();
        }

        if (!string.Equals(request.WorkspaceName?.Trim(), workspaceName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["workspaceName"] = ["The name does not match this workspace."]
            });
        }

        var failure = await ReconfirmPasswordAsync(userManager, principal, workspaceId, request.Password);
        if (failure is not null)
        {
            return failure;
        }

        await eraser.EraseAsync(workspaceId.Value, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult?> ReconfirmPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal,
        Guid? workspaceId,
        string? password)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || user.WorkspaceId != workspaceId)
        {
            return TypedResults.Problem(title: "The account no longer exists.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return TypedResults.Problem(
                title: "Account temporarily locked after too many failed attempts.",
                statusCode: StatusCodes.Status423Locked);
        }

        if (string.IsNullOrEmpty(password) || !await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["The password is incorrect."]
            });
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return null;
    }
}
