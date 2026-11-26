using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Tests;

public class IdentityAuthTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static string NewEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    private async Task<HttpResponseMessage> SignUpAsync(HttpClient client, string email, string password = StrongPassword, string workspaceName = "Milan Prints") =>
        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, password, workspaceName), TestContext.Current.CancellationToken);

    private static async Task<JsonElement> SignInAsync(HttpClient client, string email, string password = StrongPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, password), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static HttpClient WithToken(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    [Fact]
    public async Task SignUp_CreatesWorkspaceAndOwner()
    {
        using var client = factory.CreateClient();

        var response = await SignUpAsync(client, NewEmail(), workspaceName: "  Milan Prints  ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var workspace = await response.Content.ReadFromJsonAsync<WorkspaceSummary>(TestContext.Current.CancellationToken);
        Assert.Equal("Milan Prints", workspace!.Name);
    }

    [Fact]
    public async Task SignUp_RejectsWeakPassword()
    {
        using var client = factory.CreateClient();

        var response = await SignUpAsync(client, NewEmail(), password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignUp_RejectsDuplicateEmail()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await SignUpAsync(client, email);

        var response = await SignUpAsync(client, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignUp_RejectsMissingWorkspaceNameAndInvalidEmail()
    {
        using var client = factory.CreateClient();

        var response = await SignUpAsync(client, "not-an-email", workspaceName: " ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_ReturnsTokens()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await SignUpAsync(client, email);

        var tokens = await SignInAsync(client, email);

        Assert.False(string.IsNullOrEmpty(tokens.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(tokens.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task SignIn_RejectsWrongPassword()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await SignUpAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, "Wrong-Password-1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_LocksAccountAfterRepeatedFailures()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await SignUpAsync(client, email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, "Wrong-Password-1"), TestContext.Current.CancellationToken);
        }

        var lockedResponse = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Locked, lockedResponse.StatusCode);
    }

    [Fact]
    public async Task Workspace_RequiresAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/workspace", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Workspace_ReturnsOnlyTheCallersWorkspace()
    {
        using var ownerClient = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        var ownerEmail = NewEmail();
        var otherEmail = NewEmail();
        await SignUpAsync(ownerClient, ownerEmail, workspaceName: "Owner Workspace");
        await SignUpAsync(otherClient, otherEmail, workspaceName: "Other Workspace");

        var ownerTokens = await SignInAsync(ownerClient, ownerEmail);
        var otherTokens = await SignInAsync(otherClient, otherEmail);

        var ownerWorkspace = await WithToken(ownerClient, ownerTokens.GetProperty("accessToken").GetString()!)
            .GetFromJsonAsync<WorkspaceSummary>("/api/workspace", TestContext.Current.CancellationToken);
        var otherWorkspace = await WithToken(otherClient, otherTokens.GetProperty("accessToken").GetString()!)
            .GetFromJsonAsync<WorkspaceSummary>("/api/workspace", TestContext.Current.CancellationToken);

        Assert.Equal("Owner Workspace", ownerWorkspace!.Name);
        Assert.Equal("Other Workspace", otherWorkspace!.Name);
        Assert.NotEqual(ownerWorkspace.Id, otherWorkspace.Id);
    }

    [Fact]
    public async Task Refresh_IssuesNewTokens_AndSignOutInvalidatesTheRefreshToken()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await SignUpAsync(client, email);
        var tokens = await SignInAsync(client, email);
        var refreshToken = tokens.GetProperty("refreshToken").GetString()!;

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshToken), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var signOutResponse = await WithToken(factory.CreateClient(), tokens.GetProperty("accessToken").GetString()!)
            .PostAsync("/api/auth/sign-out", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, signOutResponse.StatusCode);

        var afterSignOut = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshToken), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterSignOut.StatusCode);
    }

    [Fact]
    public async Task Refresh_RejectsGarbageToken()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest("not-a-token"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
