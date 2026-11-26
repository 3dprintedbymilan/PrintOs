using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Tests;

public class WorkspaceSettingsTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static readonly UpdateWorkspaceSettingsRequest ValidUpdate = new("Milan 3D", "USD", 0.25m, 35m, 2.5m, 8m, "en");

    private async Task<HttpClient> SignedInClientAsync(string workspaceName = "Milan Prints")
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        var signUp = await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, workspaceName), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);

        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Settings_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var read = await client.GetAsync("/api/workspace/settings", cancellationToken);
        var write = await client.PutAsJsonAsync("/api/workspace/settings", ValidUpdate, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task Settings_StartWithDefaultsAfterSignUp()
    {
        using var client = await SignedInClientAsync("Defaults Workshop");

        var settings = await client.GetFromJsonAsync<WorkspaceSettingsResponse>("/api/workspace/settings", TestContext.Current.CancellationToken);

        Assert.Equal(new WorkspaceSettingsResponse("Defaults Workshop", "EUR", 0.30m, 20m, 1m, 5m, "nl"), settings);
    }

    [Fact]
    public async Task Settings_UpdatePersistsAndIsReturned()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        var update = await client.PutAsJsonAsync("/api/workspace/settings", ValidUpdate with { Name = "  Milan 3D  " }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var reloaded = await client.GetFromJsonAsync<WorkspaceSettingsResponse>("/api/workspace/settings", cancellationToken);
        Assert.Equal(new WorkspaceSettingsResponse("Milan 3D", "USD", 0.25m, 35m, 2.5m, 8m, "en"), reloaded);

        var summary = await client.GetFromJsonAsync<WorkspaceSummary>("/api/workspace", cancellationToken);
        Assert.Equal("Milan 3D", summary!.Name);
    }

    [Theory]
    [InlineData("", "EUR", 0.3, 20, 1, 5, "nl", "name")]
    [InlineData("Shop", "JPY", 0.3, 20, 1, 5, "nl", "currency")]
    [InlineData("Shop", "EUR", 0.3, 20, 1, 5, "fr", "language")]
    [InlineData("Shop", "EUR", -0.1, 20, 1, 5, "nl", "electricityPricePerKwh")]
    [InlineData("Shop", "EUR", 0.3, -1, 1, 5, "nl", "labourRatePerHour")]
    [InlineData("Shop", "EUR", 0.3, 20, 10001, 5, "nl", "machineRatePerHour")]
    [InlineData("Shop", "EUR", 0.3, 20, 1, 101, "nl", "wastePercent")]
    public async Task Settings_RejectInvalidValues(string name, string currency, double electricity, double labour, double machine, double waste, string language, string failingField)
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new UpdateWorkspaceSettingsRequest(name, currency, (decimal)electricity, (decimal)labour, (decimal)machine, (decimal)waste, language);

        var response = await client.PutAsJsonAsync("/api/workspace/settings", request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(failingField, out _));

        var unchanged = await client.GetFromJsonAsync<WorkspaceSettingsResponse>("/api/workspace/settings", cancellationToken);
        Assert.Equal("EUR", unchanged!.Currency);
    }

    [Fact]
    public async Task Settings_AreIsolatedBetweenWorkspaces()
    {
        using var firstClient = await SignedInClientAsync("First");
        using var secondClient = await SignedInClientAsync("Second");
        var cancellationToken = TestContext.Current.CancellationToken;

        await firstClient.PutAsJsonAsync("/api/workspace/settings", ValidUpdate, cancellationToken);

        var second = await secondClient.GetFromJsonAsync<WorkspaceSettingsResponse>("/api/workspace/settings", cancellationToken);
        Assert.Equal(new WorkspaceSettingsResponse("Second", "EUR", 0.30m, 20m, 1m, 5m, "nl"), second);
    }
}
