using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Purchases;

namespace PrintOs.Api.Tests;

public class PurchaseTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Purchases"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body)
    {
        var response = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private static Task<MaterialResponse> NewMaterialAsync(HttpClient client) =>
        PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"));

    private static Task<SupplierResponse> NewSupplierAsync(HttpClient client) =>
        PostAsync<SupplierResponse>(client, "/api/suppliers", new SupplierRequest($"Shop {Guid.NewGuid():N}", null));

    private static PurchaseRequest Request(Guid supplierId, Guid materialId, int quantity = 2, decimal unitPrice = 20m, decimal shipping = 0m, decimal discount = 0m, DateOnly? date = null, bool receive = false) =>
        new(supplierId, date ?? new DateOnly(2026, 11, 1), "INV-1", null, shipping, discount,
            [new PurchaseLineRequest(materialId, "Jade White", "#F4F4F0", quantity, 1000, unitPrice)], receive);

    private static async Task<List<SpoolResponse>> SpoolsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<SpoolResponse>>("/api/spools", TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task Purchases_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/purchases", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/price-history", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/purchases/{Guid.NewGuid()}/receive", null, cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Create_ComputesTotalsAndAllocatesShippingAndDiscount()
    {
        using var client = await SignedInClientAsync();
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);

        var purchase = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id, quantity: 2, unitPrice: 20m, shipping: 6m, discount: 2m));

        Assert.Equal("Ordered", purchase.Status);
        Assert.Equal(40m, purchase.Subtotal);
        Assert.Equal(44m, purchase.Total);
        Assert.Equal("EUR", purchase.Currency);
        var line = Assert.Single(purchase.Lines);
        Assert.Equal(22m, line.LandedUnitCost);
        Assert.Equal(22m, line.CostPerKg);
        Assert.Empty(await SpoolsAsync(client));
    }

    [Fact]
    public async Task Receive_CreatesSpoolsAndInitialMovementsThroughTheLedger()
    {
        using var client = await SignedInClientAsync();
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);
        var purchase = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id, quantity: 3, unitPrice: 20m, shipping: 3m));

        var received = await PostAsync<PurchaseDetailResponse>(client, $"/api/purchases/{purchase.Id}/receive", null);

        Assert.Equal("Received", received.Status);
        Assert.Equal(3, received.Lines[0].ReceivedQuantity);
        var spools = await SpoolsAsync(client);
        Assert.Equal(3, spools.Count);
        Assert.All(spools, spool =>
        {
            Assert.Equal(supplier.Id, spool.SupplierId);
            Assert.Equal(1000, spool.RemainingWeightGrams);
            Assert.Equal(21m, spool.Cost);
        });
        var movements = await client.GetFromJsonAsync<List<MovementResponse>>($"/api/spools/{spools[0].Id}/movements", TestContext.Current.CancellationToken);
        var initial = Assert.Single(movements!);
        Assert.Equal("Initial", initial.Kind);
        Assert.Equal(1000, initial.BalanceAfterGrams);
        Assert.Contains("INV-1", initial.Reason);
    }

    [Fact]
    public async Task Receive_SupportsPartialReceiptAndRefusesOverReceiving()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);
        var purchase = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id, quantity: 5));
        var lineId = purchase.Lines[0].Id;

        var first = await PostAsync<PurchaseDetailResponse>(client, $"/api/purchases/{purchase.Id}/receive", new ReceivePurchaseRequest([new ReceiveLineRequest(lineId, 2)]));
        Assert.Equal("PartiallyReceived", first.Status);
        Assert.Equal(2, (await SpoolsAsync(client)).Count);

        var tooMany = await client.PostAsJsonAsync($"/api/purchases/{purchase.Id}/receive", new ReceivePurchaseRequest([new ReceiveLineRequest(lineId, 4)]), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("lines[0].quantity", await tooMany.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(2, (await SpoolsAsync(client)).Count);

        var rest = await PostAsync<PurchaseDetailResponse>(client, $"/api/purchases/{purchase.Id}/receive", null);
        Assert.Equal("Received", rest.Status);
        Assert.Equal(5, (await SpoolsAsync(client)).Count);

        var again = await client.PostAsync($"/api/purchases/{purchase.Id}/receive", null, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(5, (await SpoolsAsync(client)).Count);
    }

    [Fact]
    public async Task Create_ReceiveImmediately_ReceivesEverything()
    {
        using var client = await SignedInClientAsync();
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);

        var purchase = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id, quantity: 2, receive: true));

        Assert.Equal("Received", purchase.Status);
        Assert.Equal(2, (await SpoolsAsync(client)).Count);
    }

    [Fact]
    public async Task Cancel_OnlyWithoutReceivedSpools_AndDeleteRules()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);
        var open = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id));
        var partial = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id, quantity: 2));
        await PostAsync<PurchaseDetailResponse>(client, $"/api/purchases/{partial.Id}/receive", new ReceivePurchaseRequest([new ReceiveLineRequest(partial.Lines[0].Id, 1)]));

        var cancelled = await PostAsync<PurchaseDetailResponse>(client, $"/api/purchases/{open.Id}/cancel", null);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/purchases/{open.Id}/receive", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/purchases/{partial.Id}/cancel", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/purchases/{partial.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/purchases/{open.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/purchases/{open.Id}", cancellationToken)).StatusCode);
        Assert.Single(await SpoolsAsync(client));
    }

    [Fact]
    public async Task Create_ValidatesEverythingWithFieldKeys()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var bad = new PurchaseRequest(
            Guid.NewGuid(), new DateOnly(2999, 1, 1), new string('x', 101), new string('n', 1001), -1m, 200_000m,
            [new PurchaseLineRequest(Guid.NewGuid(), " ", "red", 0, 0, -1m)], false);

        var response = await client.PostAsJsonAsync("/api/purchases", bad, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("errors");
        foreach (var field in new[] { "supplierId", "purchasedOn", "invoiceReference", "notes", "shippingCost", "discount", "lines[0].materialId", "lines[0].colorName", "lines[0].colorHex", "lines[0].quantity", "lines[0].weightGramsPerSpool", "lines[0].unitPrice" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"missing error for {field}");
        }

        var empty = await client.PostAsJsonAsync("/api/purchases", new PurchaseRequest(Guid.NewGuid(), null, null, null, 0m, 0m, [], false), cancellationToken);
        Assert.Contains("lines", await empty.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_RejectsDiscountLargerThanTotal()
    {
        using var client = await SignedInClientAsync();
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);

        var response = await client.PostAsJsonAsync("/api/purchases", Request(supplier.Id, material.Id, quantity: 1, unitPrice: 10m, discount: 50m), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OtherWorkspace_CannotSeeOrUsePurchases()
    {
        using var client = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);
        var purchase = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/purchases/{purchase.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/purchases/{purchase.Id}/receive", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/purchases/{purchase.Id}/cancel", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/purchases/{purchase.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/materials/{material.Id}/price-history", cancellationToken)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<PurchaseSummaryResponse>>("/api/purchases", cancellationToken))!);
        Assert.Empty((await other.GetFromJsonAsync<List<MaterialPriceHistoryResponse>>("/api/price-history", cancellationToken))!);

        var useForeign = await other.PostAsJsonAsync("/api/purchases", Request(supplier.Id, material.Id), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, useForeign.StatusCode);
    }

    [Fact]
    public async Task Receipt_UploadDownloadAndRemove()
    {
        using var client = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);
        var purchase = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id));
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 invoice");

        using var form = new MultipartFormDataContent { { new ByteArrayContent(pdf), "file", "invoice.pdf" } };
        var uploaded = await (await client.PostAsync($"/api/purchases/{purchase.Id}/receipt", form, cancellationToken)).Content.ReadFromJsonAsync<PurchaseDetailResponse>(cancellationToken);
        Assert.True(uploaded!.HasReceiptFile);
        Assert.Equal("invoice.pdf", uploaded.ReceiptFileName);

        var link = (await client.GetFromJsonAsync<ReceiptUrlResponse>($"/api/purchases/{purchase.Id}/receipt-url", cancellationToken))!;
        using var anonymous = factory.CreateClient();
        var download = await anonymous.GetAsync(link.Url, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync(cancellationToken));

        using var exe = new MultipartFormDataContent { { new ByteArrayContent(Encoding.ASCII.GetBytes("MZ-not-a-pdf")), "file", "x.exe" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/purchases/{purchase.Id}/receipt", exe, cancellationToken)).StatusCode);
        using var foreign = new MultipartFormDataContent { { new ByteArrayContent(pdf), "file", "invoice.pdf" } };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/purchases/{purchase.Id}/receipt", foreign, cancellationToken)).StatusCode);

        var removed = await (await client.DeleteAsync($"/api/purchases/{purchase.Id}/receipt", cancellationToken)).Content.ReadFromJsonAsync<PurchaseDetailResponse>(cancellationToken);
        Assert.False(removed!.HasReceiptFile);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(link.Url, cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PriceHistory_TracksCostPerKilogramLastPriceChangeAndCheapestSupplier()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await NewMaterialAsync(client);
        var cheap = await NewSupplierAsync(client);
        var pricey = await NewSupplierAsync(client);
        await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(cheap.Id, material.Id, unitPrice: 20m, date: new DateOnly(2026, 9, 1)));
        await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(pricey.Id, material.Id, unitPrice: 30m, date: new DateOnly(2026, 10, 1)));
        await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(cheap.Id, material.Id, unitPrice: 22m, date: new DateOnly(2026, 11, 1)));
        var cancelled = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(cheap.Id, material.Id, unitPrice: 1m, date: new DateOnly(2026, 11, 2)));
        await PostAsync<PurchaseDetailResponse>(client, $"/api/purchases/{cancelled.Id}/cancel", null);

        var history = (await client.GetFromJsonAsync<MaterialPriceHistoryResponse>($"/api/materials/{material.Id}/price-history", cancellationToken))!;

        Assert.Equal([20m, 30m, 22m], history.Points.Select(point => point.CostPerKg));
        Assert.Equal(22m, history.Latest!.CostPerKg);
        Assert.Equal(30m, history.PreviousCostPerKg);
        Assert.Equal(-26.7m, history.ChangePercent);
        Assert.Equal(cheap.Id, history.Cheapest!.SupplierId);
        Assert.Equal(2, history.Suppliers.Count);
        var cheapRow = history.Suppliers.Single(supplier => supplier.SupplierId == cheap.Id);
        Assert.Equal(10m, cheapRow.ChangePercent);
        Assert.True(cheapRow.IsCheapest);

        var all = (await client.GetFromJsonAsync<List<MaterialPriceHistoryResponse>>("/api/price-history", cancellationToken))!;
        Assert.Equal(material.Id, Assert.Single(all).MaterialId);
    }

    [Fact]
    public async Task PurchaseChanges_AreAudited()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var material = await NewMaterialAsync(client);
        var supplier = await NewSupplierAsync(client);
        var purchase = await PostAsync<PurchaseDetailResponse>(client, "/api/purchases", Request(supplier.Id, material.Id));
        await PostAsync<PurchaseDetailResponse>(client, $"/api/purchases/{purchase.Id}/cancel", null);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/audit?entityType=Purchase", cancellationToken);

        var actions = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("action").GetString()).ToList();
        Assert.Contains("Created", actions);
        Assert.Contains("Updated", actions);
    }
}
