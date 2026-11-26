using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;

namespace PrintOs.Api.Tests;

public class AuditLogTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private async Task<(HttpClient Client, string Email)> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Audit"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return (client, email);
    }

    private static async Task<JsonElement> GetAuditAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/audit{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Audit_RequiresAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/audit", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SettingsChange_RecordsActorAndBeforeAfterValues()
    {
        var (client, email) = await SignedInClientAsync();
        using var disposableClient = client;
        var cancellationToken = TestContext.Current.CancellationToken;
        var update = new UpdateWorkspaceSettingsRequest("Audit", "EUR", 0.45m, 20m, 1m, 5m, "nl");

        var put = await client.PutAsJsonAsync("/api/workspace/settings", update, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var page = await GetAuditAsync(client, "?entityType=Workspace");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        var updated = Assert.Single(items, item => item.GetProperty("action").GetString() == "Updated");
        Assert.Equal(email, updated.GetProperty("actorEmail").GetString());
        var change = updated.GetProperty("changes").GetProperty("electricityPricePerKwh");
        Assert.Equal(0.30m, change.GetProperty("old").GetDecimal());
        Assert.Equal(0.45m, change.GetProperty("new").GetDecimal());
        Assert.False(updated.GetProperty("changes").TryGetProperty("labourRatePerHour", out _));
    }

    [Fact]
    public async Task StockChanges_AreAuditedAndFilterableByEntity()
    {
        var (client, _) = await SignedInClientAsync();
        using var disposableClient = client;
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = (await (await client.PostAsJsonAsync("/api/materials", new MaterialRequest("PLA", "PLA"), cancellationToken))
            .Content.ReadFromJsonAsync<MaterialResponse>(cancellationToken))!;
        var spool = (await (await client.PostAsJsonAsync("/api/spools", new SpoolRequest(material.Id, null, "White", null, 20m, 1000), cancellationToken))
            .Content.ReadFromJsonAsync<SpoolResponse>(cancellationToken))!;

        var spoolEntries = await GetAuditAsync(client, $"?entityType=FilamentSpool&entityId={spool.Id}");
        var created = Assert.Single(spoolEntries.GetProperty("items").EnumerateArray());
        Assert.Equal("Created", created.GetProperty("action").GetString());
        Assert.Equal("White", created.GetProperty("changes").GetProperty("colorName").GetProperty("new").GetString());

        var movementEntries = await GetAuditAsync(client, "?entityType=MaterialMovement");
        Assert.NotEmpty(movementEntries.GetProperty("items").EnumerateArray());

        var types = await client.GetFromJsonAsync<List<string>>("/api/audit/entity-types", cancellationToken);
        Assert.Contains("FilamentSpool", types!);
        Assert.DoesNotContain("AuditEntry", types!);
    }

    [Fact]
    public async Task Audit_IsPagedNewestFirst()
    {
        var (client, _) = await SignedInClientAsync();
        using var disposableClient = client;
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var index = 0; index < 3; index++)
        {
            await client.PostAsJsonAsync("/api/materials", new MaterialRequest($"Material {index}", "PLA"), cancellationToken);
        }

        var first = await GetAuditAsync(client, "?entityType=Material&pageSize=2&page=1");
        var second = await GetAuditAsync(client, "?entityType=Material&pageSize=2&page=2");

        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.Equal("Material 2", first.GetProperty("items")[0].GetProperty("changes").GetProperty("name").GetProperty("new").GetString());

        var invalid = await client.GetAsync("/api/audit?pageSize=0", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Audit_IsIsolatedBetweenWorkspaces()
    {
        var (owner, _) = await SignedInClientAsync();
        var (stranger, _) = await SignedInClientAsync();
        using var _owner = owner;
        using var _stranger = stranger;
        var cancellationToken = TestContext.Current.CancellationToken;
        await owner.PostAsJsonAsync("/api/materials", new MaterialRequest("Secret PLA", "PLA"), cancellationToken);

        var strangerView = await GetAuditAsync(stranger, "?entityType=Material");

        Assert.Equal(0, strangerView.GetProperty("total").GetInt32());
        Assert.DoesNotContain("Secret PLA", strangerView.GetRawText());
    }

    [Fact]
    public async Task Audit_NeverContainsPasswordsOrHashes()
    {
        var (client, _) = await SignedInClientAsync();
        using var disposableClient = client;

        var page = await GetAuditAsync(client, "?pageSize=100");
        var text = page.GetRawText();

        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StrongPassword, text);
    }
}
