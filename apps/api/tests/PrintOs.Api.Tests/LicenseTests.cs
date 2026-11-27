using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Licensing;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Tests;

public class LicenseRulesTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private static License Make(LicenseType type, DateOnly? from = null, DateOnly? until = null, int? limit = null) =>
        new() { Name = "L", Type = type, ValidFrom = from, ValidUntil = until, UnitsLimit = limit };

    [Fact]
    public void PersonalLicense_IsPersonalOnlyAndBlocksSale()
    {
        var assessment = LicenseRules.Assess(Make(LicenseType.Personal), Today, 0);

        Assert.Equal(LicenseStatus.PersonalOnly, assessment.Status);
        Assert.False(assessment.AllowsCommercialSale);
    }

    [Fact]
    public void CommercialLicenseWithoutExpiry_IsValid()
    {
        var assessment = LicenseRules.Assess(Make(LicenseType.Commercial), Today, 0);

        Assert.Equal(LicenseStatus.Valid, assessment.Status);
        Assert.True(assessment.AllowsCommercialSale);
        Assert.Null(assessment.DaysUntilExpiry);
    }

    [Theory]
    [InlineData(30, LicenseStatus.ExpiringSoon, true)]
    [InlineData(31, LicenseStatus.Valid, true)]
    [InlineData(0, LicenseStatus.ExpiringSoon, true)]
    [InlineData(-1, LicenseStatus.Expired, false)]
    public void Expiry_DrivesStatus(int daysFromToday, LicenseStatus expected, bool allowsSale)
    {
        var assessment = LicenseRules.Assess(Make(LicenseType.Commercial, until: Today.AddDays(daysFromToday)), Today, 0);

        Assert.Equal(expected, assessment.Status);
        Assert.Equal(allowsSale, assessment.AllowsCommercialSale);
        Assert.Equal(daysFromToday, assessment.DaysUntilExpiry);
    }

    [Fact]
    public void FutureStart_IsNotYetValid()
    {
        var assessment = LicenseRules.Assess(Make(LicenseType.Commercial, from: Today.AddDays(2)), Today, 0);

        Assert.Equal(LicenseStatus.NotYetValid, assessment.Status);
        Assert.False(assessment.AllowsCommercialSale);
    }

    [Fact]
    public void UnitsLimit_WarnsNearLimitAndBlocksAtLimit()
    {
        var license = Make(LicenseType.CommercialWithLimits, limit: 100);

        var near = LicenseRules.Assess(license, Today, 90);
        var reached = LicenseRules.Assess(license, Today, 100);

        Assert.Equal(LicenseStatus.Valid, near.Status);
        Assert.True(near.LimitNearlyReached);
        Assert.Equal(10, near.UnitsRemaining);
        Assert.Equal(LicenseStatus.LimitReached, reached.Status);
        Assert.False(reached.AllowsCommercialSale);
        Assert.Equal(0, reached.UnitsRemaining);
    }

    [Fact]
    public void ExpiredPersonalLicense_ReportsExpired()
    {
        var assessment = LicenseRules.Assess(Make(LicenseType.Personal, until: Today.AddDays(-5)), Today, 0);

        Assert.Equal(LicenseStatus.Expired, assessment.Status);
    }
}

public class LicenseTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static async Task<HttpClient> SignedInClientAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Licenses"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private Task<HttpClient> SignedInClientAsync() => SignedInClientAsync(factory);

    private static LicenseRequest Request(
        string name = "Benchy CC",
        string type = "Commercial",
        string source = "Printables",
        DateOnly? until = null,
        int? limit = null) =>
        new(name, source, type, true, "By Creative Tools", limit, null, until, "note", "https://example.com/license");

    private static async Task<LicenseResponse> CreateAsync(HttpClient client, LicenseRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/licenses", request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LicenseResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<ModelDetailResponse> CreateModelAsync(HttpClient client, string name = "Benchy")
    {
        var stl = new byte[84 + 50];
        BitConverter.GetBytes(1u).CopyTo(stl, 80);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(stl), "file", "benchy.stl" }, { new StringContent(name), "name" } };
        var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModelDetailResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<ModelDetailResponse> LinkAsync(HttpClient client, Guid modelId, Guid? licenseId)
    {
        var response = await client.PutAsJsonAsync($"/api/models/{modelId}/license", new ModelLicenseRequest(licenseId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModelDetailResponse>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Licenses_RequireAuthentication()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/licenses", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/licenses", Request(), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsAssessmentAndTrimmedFields()
    {
        using var client = await SignedInClientAsync();

        var license = await CreateAsync(client, Request("  Benchy CC  ", until: LicenseAssessor.Today.AddDays(10)));

        Assert.Equal("Benchy CC", license.Name);
        Assert.Equal("Commercial", license.Type);
        Assert.Equal("Printables", license.Source);
        Assert.Equal("ExpiringSoon", license.Assessment.Status);
        Assert.True(license.Assessment.AllowsCommercialSale);
        Assert.Equal(10, license.Assessment.DaysUntilExpiry);
        Assert.False(license.HasProofFile);
    }

    [Fact]
    public async Task Create_RejectsInvalidInput()
    {
        using var client = await SignedInClientAsync();
        var bad = new LicenseRequest(" ", "Nowhere", "Free", false, new string('x', 501), 5, new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 1), null, "ftp://x");

        var response = await client.PostAsJsonAsync("/api/licenses", bad, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var errors = problem.GetProperty("errors");
        foreach (var field in new[] { "name", "source", "type", "attributionText", "validUntil", "proofUrl" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task Create_RejectsUnitsLimitOnNonLimitedType()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/licenses", Request(type: "Commercial", limit: 10), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesStatus()
    {
        using var client = await SignedInClientAsync();
        var license = await CreateAsync(client, Request(type: "Personal"));
        Assert.Equal("PersonalOnly", license.Assessment.Status);

        var response = await client.PutAsJsonAsync($"/api/licenses/{license.Id}", Request(type: "Commercial", until: LicenseAssessor.Today.AddDays(-1)), TestContext.Current.CancellationToken);
        var updated = await response.Content.ReadFromJsonAsync<LicenseResponse>(TestContext.Current.CancellationToken);

        Assert.Equal("Expired", updated!.Assessment.Status);
        Assert.False(updated.Assessment.AllowsCommercialSale);
    }

    [Fact]
    public async Task List_ReturnsOwnLicensesWithModelCountAndIsolatedPerWorkspace()
    {
        using var client = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var license = await CreateAsync(client, Request("Mine"));
        var model = await CreateModelAsync(client);
        await LinkAsync(client, model.Id, license.Id);
        await CreateAsync(other, Request("Theirs"));

        var mine = (await client.GetFromJsonAsync<List<LicenseResponse>>("/api/licenses", TestContext.Current.CancellationToken))!;

        var single = Assert.Single(mine);
        Assert.Equal("Mine", single.Name);
        Assert.Equal(1, single.ModelCount);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/licenses/{license.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/licenses/{license.Id}", Request(), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/licenses/{license.Id}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Detail_ListsLinkedModels()
    {
        using var client = await SignedInClientAsync();
        var license = await CreateAsync(client, Request());
        var model = await CreateModelAsync(client, "Vase");
        await LinkAsync(client, model.Id, license.Id);

        var detail = (await client.GetFromJsonAsync<LicenseDetailResponse>($"/api/licenses/{license.Id}", TestContext.Current.CancellationToken))!;

        Assert.Equal("Vase", Assert.Single(detail.Models).Name);
    }

    [Fact]
    public async Task ModelLink_ShowsLicenseOnModelAndAllowsClearing()
    {
        using var client = await SignedInClientAsync();
        var license = await CreateAsync(client, Request(type: "Personal"));
        var model = await CreateModelAsync(client);
        Assert.Null(model.LicenseId);

        var linked = await LinkAsync(client, model.Id, license.Id);

        Assert.Equal(license.Id, linked.LicenseId);
        Assert.Equal("PersonalOnly", linked.License!.Assessment.Status);
        var fetched = (await client.GetFromJsonAsync<ModelDetailResponse>($"/api/models/{model.Id}", TestContext.Current.CancellationToken))!;
        Assert.Equal(license.Id, fetched.License!.Id);
        var listed = (await client.GetFromJsonAsync<List<ModelSummaryResponse>>("/api/models", TestContext.Current.CancellationToken))!;
        Assert.Equal("PersonalOnly", Assert.Single(listed).LicenseStatus);

        var cleared = await LinkAsync(client, model.Id, null);
        Assert.Null(cleared.License);
    }

    [Fact]
    public async Task ModelUpdate_KeepsLicenseLink()
    {
        using var client = await SignedInClientAsync();
        var license = await CreateAsync(client, Request());
        var model = await CreateModelAsync(client);
        await LinkAsync(client, model.Id, license.Id);

        var response = await client.PutAsJsonAsync($"/api/models/{model.Id}", new ModelUpdateRequest("Renamed", null, null, null), TestContext.Current.CancellationToken);
        var updated = await response.Content.ReadFromJsonAsync<ModelDetailResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(license.Id, updated!.LicenseId);
    }

    [Fact]
    public async Task ModelLink_RejectsForeignLicenseAndUnknownModel()
    {
        using var client = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var foreign = await CreateAsync(other, Request());
        var model = await CreateModelAsync(client);

        var rejected = await client.PutAsJsonAsync($"/api/models/{model.Id}/license", new ModelLicenseRequest(foreign.Id), TestContext.Current.CancellationToken);
        var missing = await client.PutAsJsonAsync($"/api/models/{Guid.NewGuid()}/license", new ModelLicenseRequest(null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Delete_UnlinksModelsAndRemovesProofFile()
    {
        using var client = await SignedInClientAsync();
        var license = await CreateAsync(client, Request());
        var model = await CreateModelAsync(client);
        await LinkAsync(client, model.Id, license.Id);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 proof")), "file", "proof.pdf" } };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/licenses/{license.Id}/proof", form, TestContext.Current.CancellationToken)).StatusCode);
        var storage = factory.Services.GetRequiredService<InMemoryFileStorage>();
        Assert.Contains(storage.Keys, key => key.Contains(license.Id.ToString("N")));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/licenses/{license.Id}", TestContext.Current.CancellationToken)).StatusCode);

        var fetched = (await client.GetFromJsonAsync<ModelDetailResponse>($"/api/models/{model.Id}", TestContext.Current.CancellationToken))!;
        Assert.Null(fetched.LicenseId);
        Assert.DoesNotContain(storage.Keys, key => key.Contains(license.Id.ToString("N")));
    }

    [Fact]
    public async Task Proof_UploadDownloadReplaceAndRemove()
    {
        using var client = await SignedInClientAsync();
        var license = await CreateAsync(client, Request());
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 hello");

        using var form = new MultipartFormDataContent { { new ByteArrayContent(pdf), "file", "licence.pdf" } };
        var uploaded = await (await client.PostAsync($"/api/licenses/{license.Id}/proof", form, TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<LicenseResponse>(TestContext.Current.CancellationToken);
        Assert.True(uploaded!.HasProofFile);
        Assert.Equal("licence.pdf", uploaded.ProofFileName);

        var urlResponse = (await client.GetFromJsonAsync<LicenseProofUrlResponse>($"/api/licenses/{license.Id}/proof-url", TestContext.Current.CancellationToken))!;
        using var anonymous = factory.CreateClient();
        var download = await anonymous.GetAsync(urlResponse.Url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        var removed = await client.DeleteAsync($"/api/licenses/{license.Id}/proof", TestContext.Current.CancellationToken);
        Assert.False((await removed.Content.ReadFromJsonAsync<LicenseResponse>(TestContext.Current.CancellationToken))!.HasProofFile);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/licenses/{license.Id}/proof-url", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(urlResponse.Url, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Proof_RejectsOtherFileTypesAndForeignWorkspace()
    {
        using var client = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var license = await CreateAsync(client, Request());

        using var exe = new MultipartFormDataContent { { new ByteArrayContent([0x4D, 0x5A, 1, 2, 3, 4, 5, 6]), "file", "x.exe" } };
        using var pdf = new MultipartFormDataContent { { new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4")), "file", "x.pdf" } };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/licenses/{license.Id}/proof", exe, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/licenses/{license.Id}/proof", pdf, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CommercialUse_ReflectsLinkedLicenseAndUnitsLimit()
    {
        using var client = await SignedInClientAsync();
        var model = await CreateModelAsync(client);
        var unlicensed = (await client.GetFromJsonAsync<LicenseAssessmentResponse>($"/api/models/{model.Id}/commercial-use", TestContext.Current.CancellationToken))!;
        Assert.Equal("NoLicense", unlicensed.Status);
        Assert.False(unlicensed.AllowsCommercialSale);

        var license = await CreateAsync(client, Request(type: "CommercialWithLimits", limit: 10));
        await LinkAsync(client, model.Id, license.Id);
        var allowed = (await client.GetFromJsonAsync<LicenseAssessmentResponse>($"/api/models/{model.Id}/commercial-use", TestContext.Current.CancellationToken))!;
        Assert.True(allowed.AllowsCommercialSale);
        Assert.Equal(10, allowed.UnitsRemaining);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PrintOsDbContext>();
            var workspaceId = (await dbContext.Set<PrintModel>().IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == model.Id, TestContext.Current.CancellationToken)).WorkspaceId;
            var printer = new Printer { WorkspaceId = workspaceId, Name = "A1" };
            dbContext.Set<Printer>().Add(printer);
            dbContext.Set<PrintJob>().Add(new PrintJob { WorkspaceId = workspaceId, PrinterId = printer.Id, Name = "Run", Status = PrintJobStatus.Done, ModelId = model.Id, FinishedUnits = 10 });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var blocked = (await client.GetFromJsonAsync<LicenseAssessmentResponse>($"/api/models/{model.Id}/commercial-use", TestContext.Current.CancellationToken))!;
        Assert.Equal("LimitReached", blocked.Status);
        Assert.False(blocked.AllowsCommercialSale);
        Assert.Equal(10, blocked.UnitsUsed);
    }
}
