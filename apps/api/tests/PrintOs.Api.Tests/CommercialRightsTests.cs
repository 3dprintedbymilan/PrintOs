using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Licensing;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Tests;

public class CommercialRightsRulesTests
{
    private static CommercialRightsModelInput Input(LicenseStatus status, bool allows, bool attribution = false) =>
        new(Guid.NewGuid(), "Model", "Lic", attribution ? "By me" : null, new LicenseAssessment(status, allows, attribution, null, null, null, false));

    [Fact]
    public void NoModels_IsNeutral()
    {
        Assert.Equal(CommercialRightsStatus.NoModel, CommercialRightsRules.Evaluate([]));
    }

    [Fact]
    public void AllValid_IsCleared()
    {
        var verdict = CommercialRightsRules.Evaluate([Input(LicenseStatus.Valid, true), Input(LicenseStatus.ExpiringSoon, true)]);

        Assert.Equal(CommercialRightsStatus.Cleared, verdict);
    }

    [Theory]
    [InlineData(LicenseStatus.NoLicense)]
    [InlineData(LicenseStatus.Expired)]
    [InlineData(LicenseStatus.PersonalOnly)]
    [InlineData(LicenseStatus.LimitReached)]
    [InlineData(LicenseStatus.NotYetValid)]
    public void AnyUnclearedModel_MakesWorstCaseWin(LicenseStatus status)
    {
        var verdict = CommercialRightsRules.Evaluate([Input(LicenseStatus.Valid, true), Input(status, false)]);

        Assert.Equal(CommercialRightsStatus.NotCleared, verdict);
    }

    [Fact]
    public void Response_ListsAttributionsOnceAndFlagsExpiringSoon()
    {
        var shared = Input(LicenseStatus.ExpiringSoon, true, attribution: true);

        var response = CommercialRightsRules.ToResponse([shared, shared, Input(LicenseStatus.Valid, true)]);

        Assert.True(response.IsCleared);
        Assert.True(response.ExpiringSoon);
        Assert.Equal(2, response.Models.Count);
        Assert.Single(response.Attributions);
    }

    [Theory]
    [InlineData(CommercialRightsStatus.NoModel, CommercialRightsStatus.NoModel, CommercialRightsStatus.NoModel)]
    [InlineData(CommercialRightsStatus.NoModel, CommercialRightsStatus.Cleared, CommercialRightsStatus.Cleared)]
    [InlineData(CommercialRightsStatus.Cleared, CommercialRightsStatus.NotCleared, CommercialRightsStatus.NotCleared)]
    public void Combine_TakesWorstStatus(CommercialRightsStatus first, CommercialRightsStatus second, CommercialRightsStatus expected)
    {
        Assert.Equal(expected, CommercialRightsRules.Combine([first, second]));
    }
}

public class CommercialRightsTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Rights"), Token);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), Token);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request, Token);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static Task<T> PostAsync<T>(HttpClient client, string url, object body) => SendAsync<T>(client, HttpMethod.Post, url, body, HttpStatusCode.Created);

    private static async Task<ModelDetailResponse> CreateModelAsync(HttpClient client, string name)
    {
        var stl = new byte[84 + 50];
        BitConverter.GetBytes(1u).CopyTo(stl, 80);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(stl), "file", "part.stl" }, { new StringContent(name), "name" } };
        var response = await client.PostAsync("/api/models", form, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModelDetailResponse>(Token))!;
    }

    private static async Task<ModelDetailResponse> CreateLicensedModelAsync(HttpClient client, string name, string type, DateOnly? until = null, bool attribution = false)
    {
        var license = await PostAsync<LicenseResponse>(client, "/api/licenses", new LicenseRequest($"{name} license", "Printables", type, attribution, attribution ? "By Alice" : null, null, null, until, null, null));
        var model = await CreateModelAsync(client, name);
        await SendAsync<ModelDetailResponse>(client, HttpMethod.Put, $"/api/models/{model.Id}/license", new ModelLicenseRequest(license.Id));
        return model;
    }

    private static async Task<(ProductResponse Product, VariantResponse Variant, PrinterResponse Printer)> SeedVariantAsync(HttpClient client)
    {
        var printer = await PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var product = await PostAsync<ProductResponse>(client, "/api/products", new ProductRequest("Vase", null, null));
        var variant = await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Large", null, 20m, 40m, 0m));
        return (product, variant, printer);
    }

    private static Task<VariantResponse> AddPrintLineAsync(HttpClient client, ProductResponse product, VariantResponse variant, PrinterResponse printer, Guid? modelId) =>
        PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Print, null, null, null, null, printer.Id, modelId, 60, null));

    [Fact]
    public async Task CommercialRights_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/products/{id}/commercial-rights", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/products/{id}/variants/{id}/commercial-rights", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/products/{id}/variants/{id}/sale-status", new VariantSaleStatusRequest(true, null), Token)).StatusCode);
    }

    [Fact]
    public async Task VariantWithoutModels_IsNeutralAndCanBeMarkedReady()
    {
        using var client = await SignedInClientAsync();
        var (product, variant, _) = await SeedVariantAsync(client);

        Assert.Equal("NoModel", variant.CommercialRights.Status);
        Assert.False(variant.ReadyForSale);

        var ready = await SendAsync<VariantResponse>(client, HttpMethod.Put, $"/api/products/{product.Id}/variants/{variant.Id}/sale-status", new VariantSaleStatusRequest(true, null));

        Assert.True(ready.ReadyForSale);
        Assert.False(ready.SaleRiskAcknowledged);
    }

    [Fact]
    public async Task UnlicensedModel_NotClearedAndBlocksReadyForSale()
    {
        using var client = await SignedInClientAsync();
        var (product, variant, printer) = await SeedVariantAsync(client);
        var model = await CreateModelAsync(client, "Unlicensed");

        var withModel = await AddPrintLineAsync(client, product, variant, printer, model.Id);
        var rights = await client.GetFromJsonAsync<VariantCommercialRightsResponse>($"/api/products/{product.Id}/variants/{variant.Id}/commercial-rights", Token);

        Assert.Equal("NotCleared", withModel.CommercialRights.Status);
        Assert.Equal("NotCleared", rights!.CommercialRights.Status);
        Assert.Equal("NoLicense", rights.CommercialRights.Models.Single().LicenseStatus);

        var blocked = await client.PutAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/sale-status", new VariantSaleStatusRequest(true, false), Token);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var errors = (await blocked.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("commercialRights", out _));

        var detail = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}", Token);
        Assert.False(detail!.Variants.Single().ReadyForSale);
        Assert.Equal("NotCleared", detail.CommercialRightsStatus);
    }

    [Fact]
    public async Task AcknowledgedRisk_AllowsReadyForSaleAndIsAudited()
    {
        using var client = await SignedInClientAsync();
        var (product, variant, printer) = await SeedVariantAsync(client);
        var model = await CreateLicensedModelAsync(client, "Personal", "Personal");
        await AddPrintLineAsync(client, product, variant, printer, model.Id);

        var ready = await SendAsync<VariantResponse>(client, HttpMethod.Put, $"/api/products/{product.Id}/variants/{variant.Id}/sale-status", new VariantSaleStatusRequest(true, true));

        Assert.True(ready.ReadyForSale);
        Assert.True(ready.SaleRiskAcknowledged);
        Assert.Equal("PersonalOnly", ready.CommercialRights.Models.Single().LicenseStatus);

        var audit = await client.GetStringAsync("/api/audit?entityType=ProductVariant&pageSize=50", Token);
        Assert.Contains("saleRiskAcknowledgedAt", audit);

        var off = await SendAsync<VariantResponse>(client, HttpMethod.Put, $"/api/products/{product.Id}/variants/{variant.Id}/sale-status", new VariantSaleStatusRequest(false, null));
        Assert.False(off.ReadyForSale);
        Assert.False(off.SaleRiskAcknowledged);
    }

    [Fact]
    public async Task ProductVerdict_IsWorstVariantAndMergesAttributions()
    {
        using var client = await SignedInClientAsync();
        var (product, validVariant, printer) = await SeedVariantAsync(client);
        var expiringVariant = await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Medium", null, 15m, 40m, 0m));
        var expiredVariant = await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Small", null, 10m, 40m, 0m));
        var valid = await CreateLicensedModelAsync(client, "Valid", "Commercial", attribution: true);
        var expiring = await CreateLicensedModelAsync(client, "Expiring", "Commercial", LicenseAssessor.Today.AddDays(5));
        var expired = await CreateLicensedModelAsync(client, "Expired", "Commercial", LicenseAssessor.Today.AddDays(-1));

        var cleared = await AddPrintLineAsync(client, product, validVariant, printer, valid.Id);
        var soon = await AddPrintLineAsync(client, product, expiringVariant, printer, expiring.Id);
        var lapsed = await AddPrintLineAsync(client, product, expiredVariant, printer, expired.Id);

        Assert.Equal("Cleared", cleared.CommercialRights.Status);
        Assert.Equal("Cleared", soon.CommercialRights.Status);
        Assert.True(soon.CommercialRights.ExpiringSoon);
        Assert.Equal("NotCleared", lapsed.CommercialRights.Status);
        Assert.Equal("Expired", lapsed.CommercialRights.Models.Single().LicenseStatus);

        var rights = await SendAsync<ProductCommercialRightsResponse>(client, HttpMethod.Get, $"/api/products/{product.Id}/commercial-rights");
        Assert.Equal("NotCleared", rights.Status);
        Assert.Equal(1, rights.NotClearedVariantCount);
        Assert.Equal("By Alice", rights.Attributions.Single().Text);
    }

    [Fact]
    public async Task ProductList_ShowsWorstStatus()
    {
        using var client = await SignedInClientAsync();
        var (product, variant, printer) = await SeedVariantAsync(client);
        await AddPrintLineAsync(client, product, variant, printer, (await CreateModelAsync(client, "Nolic")).Id);

        var list = await client.GetFromJsonAsync<List<ProductSummaryResponse>>("/api/products", Token);

        var row = list!.Single(item => item.Id == product.Id);
        Assert.Equal("NotCleared", row.CommercialRightsStatus);
        Assert.Equal(1, row.NotClearedVariantCount);
    }

    [Fact]
    public async Task OtherWorkspace_GetsNotFound()
    {
        using var owner = await SignedInClientAsync();
        using var intruder = await SignedInClientAsync();
        var (product, variant, _) = await SeedVariantAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/products/{product.Id}/commercial-rights", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/products/{product.Id}/variants/{variant.Id}/commercial-rights", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PutAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/sale-status", new VariantSaleStatusRequest(true, true), Token)).StatusCode);
    }

    [Fact]
    public async Task PrintJob_AcceptsOwnModelAndRejectsForeignOrMissingModel()
    {
        using var owner = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var printer = await PostAsync<PrinterResponse>(owner, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var model = await CreateModelAsync(owner, "Mine");
        var foreign = await CreateModelAsync(other, "Theirs");

        var job = await PostAsync<PrintJobResponse>(owner, "/api/print-jobs", new PrintJobRequest(printer.Id, "Run", "Planned", null, null, null, null, null, null, null, [], model.Id));
        Assert.Equal(model.Id, job.ModelId);

        var updated = await SendAsync<PrintJobResponse>(owner, HttpMethod.Put, $"/api/print-jobs/{job.Id}", new PrintJobRequest(printer.Id, "Run", "Planned", null, null, null, null, null, null, null, [], null));
        Assert.Null(updated.ModelId);

        foreach (var badId in new[] { foreign.Id, Guid.NewGuid() })
        {
            var rejected = await owner.PostAsJsonAsync("/api/print-jobs", new PrintJobRequest(printer.Id, "Run", "Planned", null, null, null, null, null, null, null, [], badId), Token);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.True((await rejected.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty("modelId", out _));
        }
    }

    [Fact]
    public async Task FinishingJob_KeepsModelAndCountsUnitsTowardLicense()
    {
        using var client = await SignedInClientAsync();
        var printer = await PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var model = await CreateLicensedModelAsync(client, "Limited", "Commercial");
        var job = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(printer.Id, "Run", "Planned", null, null, null, null, null, null, null, [], model.Id));

        var finished = await SendAsync<PrintJobResponse>(client, HttpMethod.Post, $"/api/print-jobs/{job.Id}/finish", new FinishPrintJobRequest("Done", null, 30, null, 3, null));

        Assert.Equal(model.Id, finished.ModelId);
    }
}
