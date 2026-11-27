using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Account;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Licensing;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Tests;

public class AccountDataTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";
    private const string SecretToken = "SECRET-BAMBU-TOKEN-VALUE";
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private sealed record Session(HttpClient Client, string Email, string RefreshToken, Guid WorkspaceId, string WorkspaceName);

    private static async Task<Session> SignInAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var workspaceName = $"Shop {Guid.NewGuid():N}"[..14];
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, workspaceName), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        var workspace = await client.GetFromJsonAsync<WorkspaceSummary>("/api/workspace", cancellationToken);
        return new Session(client, email, tokens.GetProperty("refreshToken").GetString()!, workspace!.Id, workspaceName);
    }

    private static byte[] BinaryStl(int triangles)
    {
        var bytes = new byte[84 + (50 * triangles)];
        BitConverter.GetBytes((uint)triangles).CopyTo(bytes, 80);
        bytes[100] = 7;
        return bytes;
    }

    private static async Task<Guid> CreateModelAsync(HttpClient client, string name)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(BinaryStl(3)), "file", "model.stl" },
            { new ByteArrayContent(PngBytes), "thumbnail", "thumb.png" },
            { new StringContent(name), "name" }
        };
        var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private static async Task SeedWorkspaceAsync(Session session, string modelName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateModelAsync(session.Client, modelName);

        var printer = new PrinterRequest("Bambu A1 " + modelName, "A1", 256, 256, 256, 0.4m, 150, 400m, 5000);
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync("/api/printers", printer, cancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync("/api/customers", new PrintOs.Api.Modules.Sales.CustomerRequest("Customer " + modelName, "klant@example.com", null, null, null, null), cancellationToken)).StatusCode);

        var license = await session.Client.PostAsJsonAsync("/api/licenses", new LicenseRequest("Lic " + modelName, "Other", "Commercial", false, null, null, null, null, null, null), cancellationToken);
        var licenseId = (await license.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();
        using var proof = new MultipartFormDataContent { { new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 proof")), "file", "proof.pdf" } };
        Assert.Equal(HttpStatusCode.OK, (await session.Client.PostAsync($"/api/licenses/{licenseId}/proof", proof, cancellationToken)).StatusCode);
    }

    private async Task SeedBambuAsync(Guid workspaceId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrintOsDbContext>();
        var connection = new BambuConnection { WorkspaceId = workspaceId, EncryptedAccessToken = SecretToken, AccountUid = "uid-1", AccountName = "Private Name" };
        dbContext.Set<BambuConnection>().Add(connection);
        dbContext.Set<BambuDevice>().Add(new BambuDevice { WorkspaceId = workspaceId, ConnectionId = connection.Id, Serial = "SERIAL-123", Name = "A1", Model = "A1" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<(HttpResponseMessage Response, Dictionary<string, byte[]> Entries)> ExportAsync(Session session, string password = StrongPassword)
    {
        var response = await session.Client.PostAsJsonAsync("/api/account/export", new ExportAccountRequest(password), TestContext.Current.CancellationToken);
        var entries = new Dictionary<string, byte[]>();
        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                using var buffer = new MemoryStream();
                await using var stream = await entry.OpenAsync(TestContext.Current.CancellationToken);
                await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);
                entries[entry.FullName] = buffer.ToArray();
            }
        }

        return (response, entries);
    }

    private static string Text(Dictionary<string, byte[]> entries, string path) => Encoding.UTF8.GetString(entries[path]);

    [Fact]
    public async Task AccountEndpoints_RequireAuthentication()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/account/export", new ExportAccountRequest(StrongPassword), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(StrongPassword, "x"), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Export_WithWrongPassword_IsRejectedWithoutData()
    {
        var session = await SignInAsync(factory);

        var (response, entries) = await ExportAsync(session, "Wrong-Password-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(entries);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Export_ContainsDataAndFiles_AndNoSecrets()
    {
        var session = await SignInAsync(factory);
        await SeedWorkspaceAsync(session, "Benchy");
        await SeedBambuAsync(session.WorkspaceId);

        var (response, entries) = await ExportAsync(session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains("README.md", entries.Keys);
        Assert.Contains("PrintModel", Text(entries, "README.md"));
        Assert.Contains("Benchy", Text(entries, "data/PrintModel.json"));
        Assert.Contains("Bambu A1 Benchy", Text(entries, "data/Printer.json"));
        Assert.Contains("klant@example.com", Text(entries, "data/Customer.json"));
        Assert.Contains(session.Email, Text(entries, "data/Account.json"));
        Assert.Contains(session.WorkspaceName, Text(entries, "data/Workspace.json"));
        Assert.Contains("AuditEntry", string.Join(",", entries.Keys));

        var modelFile = Assert.Single(entries, pair => pair.Key.StartsWith("files/PrintModelVersion/", StringComparison.Ordinal) && pair.Key.EndsWith(".stl", StringComparison.Ordinal));
        Assert.Equal(BinaryStl(3), modelFile.Value);
        Assert.Equal(PngBytes, Assert.Single(entries, pair => pair.Key.StartsWith("files/PrintModel/", StringComparison.Ordinal)).Value);
        Assert.Equal("%PDF-1.4 proof", Encoding.ASCII.GetString(Assert.Single(entries, pair => pair.Key.StartsWith("files/License/", StringComparison.Ordinal)).Value));
        Assert.Contains(modelFile.Key, Text(entries, "data/PrintModelVersion.json"));

        var bambu = JsonDocument.Parse(entries["data/Bambu.json"]).RootElement;
        Assert.True(bambu.GetProperty("connected").GetBoolean());
        Assert.Equal("SERIAL-123", bambu.GetProperty("deviceSerials")[0].GetString());

        var everything = string.Join("\n", entries.Where(pair => pair.Key.EndsWith(".json", StringComparison.Ordinal) || pair.Key.EndsWith(".md", StringComparison.Ordinal)).Select(pair => Encoding.UTF8.GetString(pair.Value)));
        Assert.DoesNotContain(SecretToken, everything);
        Assert.DoesNotContain("Private Name", everything);
        Assert.DoesNotContain("BambuConnection.json", string.Join(",", entries.Keys));
        foreach (var secret in new[] { "PasswordHash", "passwordHash", "SecurityStamp", "securityStamp", "ConcurrencyStamp", "encryptedAccessToken", "\"storageKey\"", "DataProtection" })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }

    [Fact]
    public async Task Export_OnlyContainsTheOwnWorkspace()
    {
        var first = await SignInAsync(factory);
        var second = await SignInAsync(factory);
        await SeedWorkspaceAsync(first, "FirstOnlyModel");
        await SeedWorkspaceAsync(second, "SecondOnlyModel");

        var (_, entries) = await ExportAsync(first);

        var everything = string.Join("\n", entries.Where(pair => pair.Key.EndsWith(".json", StringComparison.Ordinal)).Select(pair => Encoding.UTF8.GetString(pair.Value)));
        Assert.Contains("FirstOnlyModel", everything);
        Assert.DoesNotContain("SecondOnlyModel", everything);
        Assert.DoesNotContain(second.Email, everything);
        Assert.Single(entries, pair => pair.Key.StartsWith("files/PrintModelVersion/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_IsRateLimited()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Account:ExportsPerWindow"] = "1" })));
        var session = await SignInAsync(limited);

        var (first, _) = await ExportAsync(session);
        var second = await session.Client.PostAsJsonAsync("/api/account/export", new ExportAccountRequest(StrongPassword), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Delete_WithWrongPasswordOrName_ChangesNothing()
    {
        var session = await SignInAsync(factory);
        await SeedWorkspaceAsync(session, "Keeper");

        var wrongPassword = await session.Client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("Wrong-Password-1", session.WorkspaceName), TestContext.Current.CancellationToken);
        var wrongName = await session.Client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(StrongPassword, "Another name"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongName.StatusCode);
        Assert.True((await wrongName.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("errors").TryGetProperty("workspaceName", out _));
        var models = await session.Client.GetFromJsonAsync<JsonElement>("/api/models", TestContext.Current.CancellationToken);
        Assert.NotEqual(0, models.GetArrayLength());
    }

    [Fact]
    public async Task Delete_RemovesEveryRowFileAndSignIn_AndLeavesOtherWorkspacesAlone()
    {
        var doomed = await SignInAsync(factory);
        var survivor = await SignInAsync(factory);
        await SeedWorkspaceAsync(doomed, "Doomed");
        await SeedWorkspaceAsync(survivor, "Survivor");
        await SeedBambuAsync(doomed.WorkspaceId);
        var storage = factory.Services.GetRequiredService<InMemoryFileStorage>();
        var doomedPrefix = doomed.WorkspaceId.ToString("N");
        var survivorPrefix = survivor.WorkspaceId.ToString("N");
        Assert.Contains(storage.Keys, key => key.StartsWith(doomedPrefix, StringComparison.Ordinal));

        var response = await doomed.Client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(StrongPassword, "  " + doomed.WorkspaceName.ToUpperInvariant() + " "), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PrintOsDbContext>();
            foreach (var entityType in WorkspaceDataMap.ScopedEntityTypes(dbContext.Model))
            {
#pragma warning disable EF1002
                var count = await dbContext.Database
                    .SqlQueryRaw<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"{entityType.GetTableName()}\" WHERE \"WorkspaceId\" = {{0}}", doomed.WorkspaceId)
                    .SingleAsync(TestContext.Current.CancellationToken);
#pragma warning restore EF1002
                Assert.True(count == 0, $"{entityType.ClrType.Name} still has {count} rows");
            }

            Assert.False(await dbContext.Users.AnyAsync(user => user.Email == doomed.Email, TestContext.Current.CancellationToken));
            Assert.False(await dbContext.Set<Workspace>().AnyAsync(workspace => workspace.Id == doomed.WorkspaceId, TestContext.Current.CancellationToken));
            Assert.True(await dbContext.Users.AnyAsync(user => user.Email == survivor.Email, TestContext.Current.CancellationToken));
        }

        Assert.DoesNotContain(storage.Keys, key => key.StartsWith(doomedPrefix, StringComparison.Ordinal));
        Assert.Contains(storage.Keys, key => key.StartsWith(survivorPrefix, StringComparison.Ordinal));

        var refresh = await doomed.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(doomed.RefreshToken), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var signIn = await factory.CreateClient().PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(doomed.Email, StrongPassword), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        var reuse = await doomed.Client.PostAsJsonAsync("/api/account/export", new ExportAccountRequest(StrongPassword), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var (_, survivorEntries) = await ExportAsync(survivor);
        Assert.Contains("Survivor", Text(survivorEntries, "data/PrintModel.json"));
    }

    [Fact]
    public async Task Delete_StopsTheBambuWorkerForTheWorkspace()
    {
        var session = await SignInAsync(factory);
        await SeedBambuAsync(session.WorkspaceId);
        var worker = factory.Services.GetRequiredService<BambuTelemetryWorker>();

        var response = await session.Client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(StrongPassword, session.WorkspaceName), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await worker.ReconcileAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, worker.ActiveSubscriptions);
    }
}
