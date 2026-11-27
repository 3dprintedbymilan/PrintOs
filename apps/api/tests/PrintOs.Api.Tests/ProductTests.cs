using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;

namespace PrintOs.Api.Tests;

public class ProductTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Products"), Token);
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

    private static Task<T> PostAsync<T>(HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.Created) =>
        SendAsync<T>(client, HttpMethod.Post, url, body, expected);

    private static async Task<(PrinterResponse Printer, MaterialResponse Material, SpoolResponse Spool)> SeedInventoryAsync(HttpClient client)
    {
        var printer = await PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"));
        var spool = await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "White", null, 20m, 1000));
        return (printer, material, spool);
    }

    private static Task<ProductResponse> CreateProductAsync(HttpClient client, string name = "Vase", string? sku = null) =>
        PostAsync<ProductResponse>(client, "/api/products", new ProductRequest(name, "Desc", sku));

    private static async Task<VariantResponse> CreateVariantAsync(HttpClient client, Guid productId, string name = "Large", decimal price = 20m, string? sku = null) =>
        await PostAsync<VariantResponse>(client, $"/api/products/{productId}/variants", new VariantRequest(name, sku, price, 40m, 0m));

    private static BomLineRequest Filament(Guid spoolId, decimal grams = 100m) =>
        new(BomLineKinds.Filament, null, null, spoolId, grams, null, null, null, null);

    private static BomLineRequest PrintLine(Guid printerId, int minutes = 120, Guid? modelId = null) =>
        new(BomLineKinds.Print, null, null, null, null, printerId, modelId, minutes, null);

    private static BomLineRequest LabourLine(int minutes = 15) => new(BomLineKinds.Labour, null, null, null, null, null, null, minutes, null);

    private static Task<VariantResponse> AddLineAsync(HttpClient client, Guid productId, Guid variantId, BomLineRequest line) =>
        PostAsync<VariantResponse>(client, $"/api/products/{productId}/variants/{variantId}/bom-lines", line);

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
    }

    [Fact]
    public async Task Products_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/products", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/products/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/products/{id}/variants/{id}/cost", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/products/{id}/variants/{id}/cost-snapshots", new { }, Token)).StatusCode);
    }

    [Fact]
    public async Task Product_CanBeCreatedUpdatedArchivedAndDeleted()
    {
        using var client = await SignedInClientAsync();

        var created = await CreateProductAsync(client, "  Vase  ", " V-1 ");
        Assert.Equal("Vase", created.Name);
        Assert.Equal("V-1", created.Sku);
        Assert.Equal("EUR", created.Currency);

        var updated = await SendAsync<ProductResponse>(client, HttpMethod.Put, $"/api/products/{created.Id}", new ProductRequest("Tall vase", null, null));
        Assert.Equal("Tall vase", updated.Name);
        Assert.Null(updated.Sku);

        var archived = await PostAsync<ProductResponse>(client, $"/api/products/{created.Id}/archive", new { }, HttpStatusCode.OK);
        Assert.True(archived.IsArchived);
        Assert.Empty((await client.GetFromJsonAsync<List<ProductSummaryResponse>>("/api/products", Token))!);
        Assert.Single((await client.GetFromJsonAsync<List<ProductSummaryResponse>>("/api/products?includeArchived=true", Token))!);

        var restored = await PostAsync<ProductResponse>(client, $"/api/products/{created.Id}/restore", new { }, HttpStatusCode.OK);
        Assert.False(restored.IsArchived);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{created.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{created.Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task Product_ValidatesNameDescriptionAndUniqueSku()
    {
        using var client = await SignedInClientAsync();
        await CreateProductAsync(client, "First", "SKU-1");

        var invalid = await client.PostAsJsonAsync("/api/products", new ProductRequest(" ", new string('x', 2001), "sku-1"), Token);
        var errors = await ErrorsAsync(invalid);

        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("description", out _));
        Assert.True(errors.TryGetProperty("sku", out _));
    }

    [Fact]
    public async Task Products_ListSupportsSearchAndShowsPriceRange()
    {
        using var client = await SignedInClientAsync();
        var vase = await CreateProductAsync(client, "Spiral vase", "VASE-1");
        await CreateProductAsync(client, "Keychain", "KEY-1");
        await CreateVariantAsync(client, vase.Id, "Small", 10m);
        await CreateVariantAsync(client, vase.Id, "Large", 25m);

        var byName = await client.GetFromJsonAsync<List<ProductSummaryResponse>>("/api/products?search=spir", Token);
        var bySku = await client.GetFromJsonAsync<List<ProductSummaryResponse>>("/api/products?search=key-", Token);

        var found = Assert.Single(byName!);
        Assert.Equal(2, found.VariantCount);
        Assert.Equal("EUR", found.Currency);
        Assert.Equal(10m, found.LowestPrice);
        Assert.Equal(25m, found.HighestPrice);
        Assert.Equal("Keychain", Assert.Single(bySku!).Name);
    }

    [Fact]
    public async Task Variant_ValidatesPricingAndUniqueSku()
    {
        using var client = await SignedInClientAsync();
        var product = await CreateProductAsync(client);
        await CreateVariantAsync(client, product.Id, "Red", 10m, "RED-1");

        var invalid = await client.PostAsJsonAsync($"/api/products/{product.Id}/variants", new VariantRequest("", "red-1", -1m, 100m, 0m), Token);
        var errors = await ErrorsAsync(invalid);

        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("sku", out _));
        Assert.True(errors.TryGetProperty("price", out _));
        Assert.True(errors.TryGetProperty("targetMarginPercent", out _));
    }

    [Fact]
    public async Task Variant_CanBeUpdatedAndDeleted()
    {
        using var client = await SignedInClientAsync();
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id);

        var updated = await SendAsync<VariantResponse>(client, HttpMethod.Put, $"/api/products/{product.Id}/variants/{variant.Id}", new VariantRequest("XL", "XL-1", 30m, 50m, 5m));
        Assert.Equal("XL", updated.Name);
        Assert.Equal(30m, updated.Price);
        Assert.Equal(5m, updated.FeePercent);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{product.Id}/variants/{variant.Id}", Token)).StatusCode);
        var detail = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}", Token);
        Assert.Empty(detail!.Variants);
    }

    [Fact]
    public async Task Bom_ComputesCostAndMarginWithTheCostEngine()
    {
        using var client = await SignedInClientAsync();
        var (printer, _, spool) = await SeedInventoryAsync(client);
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id, price: 20m);

        await AddLineAsync(client, product.Id, variant.Id, Filament(spool.Id));
        await AddLineAsync(client, product.Id, variant.Id, PrintLine(printer.Id));
        await AddLineAsync(client, product.Id, variant.Id, LabourLine());
        var final = await AddLineAsync(client, product.Id, variant.Id, new BomLineRequest(BomLineKinds.Extra, "Magnet", null, null, null, null, null, null, 0.25m));

        Assert.Equal(4, final.BomLines.Count);
        Assert.Equal(9.55m, final.Cost.TotalCost);
        Assert.Equal(20m, final.Cost.Price);
        Assert.Equal(10.45m, final.Cost.Profit);
        Assert.Equal(52.25m, final.Cost.MarginPercent);
        Assert.Equal("EUR", final.Cost.Currency);
        Assert.Equal(["material", "electricity", "machine", "labour", "extra"], final.Cost.Lines.Select(line => line.Kind).ToArray());

        var cost = await client.GetFromJsonAsync<VariantCostResponse>($"/api/products/{product.Id}/variants/{variant.Id}/cost", Token);
        Assert.Equal(final.Cost.TotalCost, cost!.TotalCost);
        Assert.Equal(final.Cost.SuggestedPrice, cost.SuggestedPrice);
    }

    [Fact]
    public async Task Bom_MaterialLineUsesTheAverageSpoolCost()
    {
        using var client = await SignedInClientAsync();
        var (printer, material, _) = await SeedInventoryAsync(client);
        await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "Black", null, 40m, 1000));
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id);

        var result = await AddLineAsync(client, product.Id, variant.Id, new BomLineRequest(BomLineKinds.Filament, null, material.Id, null, 100m, null, null, null, null));

        Assert.Equal(3.15m, result.Cost.Lines.Single(line => line.Kind == "material").Amount);
        Assert.Empty(result.Cost.UnpricedMaterials);
        Assert.Equal(material.Name, result.BomLines.Single().MaterialName);
        _ = printer;
    }

    [Fact]
    public async Task Bom_FlagsMaterialsWithoutSpoolsAsUnpriced()
    {
        using var client = await SignedInClientAsync();
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PETG {Guid.NewGuid():N}", "PETG"));
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id);

        var result = await AddLineAsync(client, product.Id, variant.Id, new BomLineRequest(BomLineKinds.Filament, null, material.Id, null, 50m, null, null, null, null));

        Assert.Equal([material.Name], result.Cost.UnpricedMaterials);
    }

    [Fact]
    public async Task Bom_LinksAnOptionalModelFromTheLibrary()
    {
        using var client = await SignedInClientAsync();
        var (printer, _, _) = await SeedInventoryAsync(client);
        var stl = new byte[84 + 50];
        BitConverter.GetBytes((uint)1).CopyTo(stl, 80);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(stl), "file", "vase.stl" }, { new StringContent("Vase model"), "name" } };
        var modelResponse = await client.PostAsync("/api/models", form, Token);
        Assert.Equal(HttpStatusCode.Created, modelResponse.StatusCode);
        var model = (await modelResponse.Content.ReadFromJsonAsync<ModelDetailResponse>(Token))!;
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id);

        var result = await AddLineAsync(client, product.Id, variant.Id, PrintLine(printer.Id, 60, model.Id));

        var line = Assert.Single(result.BomLines);
        Assert.Equal(model.Id, line.ModelId);
        Assert.Equal("Vase model", line.ModelName);
    }

    [Fact]
    public async Task Bom_RejectsInvalidLines()
    {
        using var client = await SignedInClientAsync();
        var (printer, material, spool) = await SeedInventoryAsync(client);
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id);
        var url = $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines";

        var unknownKind = await ErrorsAsync(await client.PostAsJsonAsync(url, new BomLineRequest("magic", null, null, null, null, null, null, null, null), Token));
        var noSource = await ErrorsAsync(await client.PostAsJsonAsync(url, new BomLineRequest(BomLineKinds.Filament, null, null, null, 0m, null, null, null, null), Token));
        var bothSources = await ErrorsAsync(await client.PostAsJsonAsync(url, new BomLineRequest(BomLineKinds.Filament, null, material.Id, spool.Id, 10m, null, null, null, null), Token));
        var noPrinter = await ErrorsAsync(await client.PostAsJsonAsync(url, new BomLineRequest(BomLineKinds.Print, null, null, null, null, null, null, 0, null), Token));
        var noLabel = await ErrorsAsync(await client.PostAsJsonAsync(url, new BomLineRequest(BomLineKinds.Extra, " ", null, null, null, null, null, null, -1m), Token));

        Assert.True(unknownKind.TryGetProperty("kind", out _));
        Assert.True(noSource.TryGetProperty("spoolId", out _));
        Assert.True(noSource.TryGetProperty("grams", out _));
        Assert.True(bothSources.TryGetProperty("spoolId", out _));
        Assert.True(noPrinter.TryGetProperty("printerId", out _));
        Assert.True(noPrinter.TryGetProperty("minutes", out _));
        Assert.True(noLabel.TryGetProperty("label", out _));
        Assert.True(noLabel.TryGetProperty("amount", out _));

        await AddLineAsync(client, product.Id, variant.Id, PrintLine(printer.Id));
        var second = await ErrorsAsync(await client.PostAsJsonAsync(url, PrintLine(printer.Id), Token));
        Assert.True(second.TryGetProperty("kind", out _));
    }

    [Fact]
    public async Task Bom_LinesCanBeUpdatedAndRemoved()
    {
        using var client = await SignedInClientAsync();
        var (_, _, spool) = await SeedInventoryAsync(client);
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id);
        var withLine = await AddLineAsync(client, product.Id, variant.Id, Filament(spool.Id, 50m));
        var lineId = withLine.BomLines.Single().Id;
        var url = $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines/{lineId}";

        var updated = await SendAsync<VariantResponse>(client, HttpMethod.Put, url, Filament(spool.Id, 80m));
        Assert.Equal(80m, updated.BomLines.Single().Grams);

        var kindChange = await client.PutAsJsonAsync(url, LabourLine(), Token);
        Assert.Equal(HttpStatusCode.BadRequest, kindChange.StatusCode);

        var removed = await SendAsync<VariantResponse>(client, HttpMethod.Delete, url);
        Assert.Empty(removed.BomLines);
    }

    [Fact]
    public async Task Snapshots_StoreTheCostAndStayImmutable()
    {
        using var client = await SignedInClientAsync();
        var (printer, _, spool) = await SeedInventoryAsync(client);
        var product = await CreateProductAsync(client);
        var variant = await CreateVariantAsync(client, product.Id, price: 20m);
        await AddLineAsync(client, product.Id, variant.Id, Filament(spool.Id));
        await AddLineAsync(client, product.Id, variant.Id, PrintLine(printer.Id));
        var url = $"/api/products/{product.Id}/variants/{variant.Id}/cost-snapshots";

        var snapshot = await PostAsync<VariantCostSnapshotResponse>(client, url, new { });
        Assert.Equal(4.30m, snapshot.TotalCost);
        Assert.Equal(20m, snapshot.Price);
        Assert.Equal(snapshot.TotalCost, snapshot.Breakdown.TotalCost);

        (await client.PutAsJsonAsync("/api/workspace/settings", new UpdateWorkspaceSettingsRequest("Shop", "EUR", 0.30m, 100m, 1m, 5m, "nl"), Token)).EnsureSuccessStatusCode();
        await SendAsync<VariantResponse>(client, HttpMethod.Put, $"/api/products/{product.Id}/variants/{variant.Id}", new VariantRequest("Large", null, 30m, 40m, 0m));

        var history = await client.GetFromJsonAsync<List<VariantCostSnapshotResponse>>(url, Token);
        var stored = Assert.Single(history!);
        Assert.Equal(4.30m, stored.TotalCost);
        Assert.Equal(20m, stored.Price);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync($"{url}/{snapshot.Id}", new { }, Token)).StatusCode);
    }

    [Fact]
    public async Task Products_AreIsolatedBetweenWorkspaces()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var (printer, _, spool) = await SeedInventoryAsync(owner);
        var product = await CreateProductAsync(owner, "Secret", "SECRET-1");
        var variant = await CreateVariantAsync(owner, product.Id, sku: "SECRET-V");
        var withLine = await AddLineAsync(owner, product.Id, variant.Id, Filament(spool.Id));
        var lineId = withLine.BomLines.Single().Id;
        var variantUrl = $"/api/products/{product.Id}/variants/{variant.Id}";

        Assert.Empty((await stranger.GetFromJsonAsync<List<ProductSummaryResponse>>("/api/products?includeArchived=true", Token))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/products/{product.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/api/products/{product.Id}", new ProductRequest("x", null, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/products/{product.Id}/archive", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/products/{product.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/products/{product.Id}/variants", new VariantRequest("x", null, 1m, null, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{variantUrl}/cost", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"{variantUrl}/cost-snapshots", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{variantUrl}/cost-snapshots", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"{variantUrl}/bom-lines", LabourLine(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"{variantUrl}/bom-lines/{lineId}", Token)).StatusCode);

        var strangerProduct = await CreateProductAsync(stranger, "Mine", "SECRET-1");
        var strangerVariant = await CreateVariantAsync(stranger, strangerProduct.Id, sku: "SECRET-V");
        var foreignSpool = await stranger.PostAsJsonAsync($"/api/products/{strangerProduct.Id}/variants/{strangerVariant.Id}/bom-lines", Filament(spool.Id), Token);
        var foreignPrinter = await stranger.PostAsJsonAsync($"/api/products/{strangerProduct.Id}/variants/{strangerVariant.Id}/bom-lines", PrintLine(printer.Id), Token);
        Assert.Equal(HttpStatusCode.NotFound, foreignSpool.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignPrinter.StatusCode);
    }
}
