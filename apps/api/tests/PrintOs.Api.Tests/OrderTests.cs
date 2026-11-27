using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class OrderRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Draft, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Draft, OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.InProduction, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Ready, true)]
    [InlineData(OrderStatus.InProduction, OrderStatus.Ready, true)]
    [InlineData(OrderStatus.Ready, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Ready, OrderStatus.Draft, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Draft, true)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, false)]
    public void Transitions_FollowTheWorkflow(OrderStatus from, OrderStatus to, bool allowed)
    {
        Assert.Equal(allowed, OrderRules.CanTransition(from, to));
    }

    [Fact]
    public void EveryStatusHasADefinedTransitionList()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            Assert.NotNull(OrderRules.AllowedTransitions(status));
        }
    }

    [Theory]
    [InlineData(0, 10, OrderPaymentStatus.Unpaid)]
    [InlineData(0, 0, OrderPaymentStatus.Paid)]
    [InlineData(1, 10, OrderPaymentStatus.Partial)]
    [InlineData(10, 10, OrderPaymentStatus.Paid)]
    [InlineData(12, 10, OrderPaymentStatus.Paid)]
    public void PaymentStatus_IsDerivedFromTotalAndPaid(double paid, double total, OrderPaymentStatus expected)
    {
        Assert.Equal(expected, OrderRules.PaymentStatusOf((decimal)total, (decimal)paid));
    }

    [Fact]
    public void Recalculate_AppliesLineDiscountOrderDiscountAndCostSnapshots()
    {
        var order = new Order { DiscountAmount = 5m };
        order.Lines.Add(new OrderLine { Quantity = 3, UnitPrice = 10m, DiscountPercent = 10m, UnitCost = 4m });
        order.Lines.Add(new OrderLine { Quantity = 1, UnitPrice = 9.99m, UnitCost = null });
        order.Payments.Add(new OrderPayment { Amount = 7.5m });

        OrderRules.Recalculate(order);

        Assert.Equal(27m, order.Lines[0].LineTotal);
        Assert.Equal(36.99m, order.Subtotal);
        Assert.Equal(31.99m, order.TotalAmount);
        Assert.Equal(12m, order.TotalCost);
        Assert.False(order.CostComplete);
        Assert.Equal(7.5m, order.AmountPaid);
        Assert.Equal(OrderPaymentStatus.Partial, order.PaymentStatus);
    }
}

public class OrderTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Orders"), Token);
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

    private static async Task<JsonElement> ExpectBadRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
    }

    private static OrderRequest Draft(Guid? customerId = null, params OrderLineRequest[] lines) =>
        new(customerId, null, null, null, null, null, lines.ToList(), null);

    private static OrderLineRequest VariantLine(Guid variantId, int quantity = 1, decimal? unitPrice = null, decimal? discountPercent = null, Guid? id = null) =>
        new(id, variantId, null, quantity, unitPrice, null, discountPercent);

    private static async Task<(ProductResponse Product, VariantResponse Variant)> SeedVariantAsync(HttpClient client, string name = "Vase", decimal price = 20m, decimal packaging = 3.5m)
    {
        var product = await PostAsync<ProductResponse>(client, "/api/products", new ProductRequest(name, null, null));
        var variant = await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants", new VariantRequest("Large", null, price, 40m, 0m));
        await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Packaging, "Box", null, null, null, null, null, null, packaging));
        return (product, variant);
    }

    private static Task<CustomerResponse> CreateCustomerAsync(HttpClient client, string name = "Jan Peeters") =>
        PostAsync<CustomerResponse>(client, "/api/customers", new CustomerRequest(name, null, null, null, null, null));

    private static Task<OrderResponse> CreateOrderAsync(HttpClient client, OrderRequest request) => PostAsync<OrderResponse>(client, "/api/orders", request);

    private static Task<OrderResponse> MoveAsync(HttpClient client, Guid id, string status, bool? acknowledge = null) =>
        SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{id}/status", new OrderStatusRequest(status, acknowledge));

    private static async Task<ModelDetailResponse> CreateUnlicensedModelAsync(HttpClient client, string name)
    {
        var stl = new byte[84 + 50];
        BitConverter.GetBytes(1u).CopyTo(stl, 80);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(stl), "file", "part.stl" }, { new StringContent(name), "name" } };
        var response = await client.PostAsync("/api/models", form, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModelDetailResponse>(Token))!;
    }

    private static async Task AttachUnlicensedModelAsync(HttpClient client, ProductResponse product, VariantResponse variant)
    {
        var printer = await PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest("A1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));
        var model = await CreateUnlicensedModelAsync(client, "Unlicensed");
        await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Print, null, null, null, null, printer.Id, model.Id, 60, null));
    }

    [Fact]
    public async Task Orders_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/orders/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/orders", Draft(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Confirmed", null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/orders/{id}/payments", new OrderPaymentRequest(1m, null, null, null), Token)).StatusCode);
    }

    [Fact]
    public async Task Create_SnapshotsPriceAndCostAndNumbersOrdersPerWorkspace()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);
        var customer = await CreateCustomerAsync(client);

        var first = await CreateOrderAsync(client, Draft(customer.Id, VariantLine(variant.Id, 2)));
        var second = await CreateOrderAsync(client, Draft());

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.Equal("Draft", first.Status);
        Assert.Equal("Unpaid", first.PaymentStatus);
        Assert.Equal("Jan Peeters", first.CustomerName);
        Assert.Equal("EUR", first.Currency);
        var line = Assert.Single(first.Lines);
        Assert.Equal("Vase - Large", line.Description);
        Assert.Equal(20m, line.ListUnitPrice);
        Assert.Equal(20m, line.UnitPrice);
        Assert.Equal(3.5m, line.UnitCost);
        Assert.Equal(40m, line.LineTotal);
        Assert.Equal(40m, first.TotalAmount);
        Assert.Equal(7m, first.TotalCost);
        Assert.Equal(33m, first.Profit);
        Assert.Equal(82.5m, first.MarginPercent);
        Assert.True(first.LinesEditable);
        Assert.Equal(["Confirmed", "Cancelled"], first.AllowedTransitions);
    }

    [Fact]
    public async Task Snapshots_SurviveVariantPriceAndCostChangesAndVariantDeletion()
    {
        using var client = await SignedInClientAsync();
        var (product, variant) = await SeedVariantAsync(client);
        var order = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id, 1)));
        var cancellation = Token;

        await SendAsync<VariantResponse>(client, HttpMethod.Put, $"/api/products/{product.Id}/variants/{variant.Id}", new VariantRequest("Large", null, 99m, 40m, 0m));
        await PostAsync<VariantResponse>(client, $"/api/products/{product.Id}/variants/{variant.Id}/bom-lines", new BomLineRequest(BomLineKinds.Extra, "Gold", null, null, null, null, null, null, 50m));

        var unchanged = await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}", cancellation);
        Assert.Equal(20m, unchanged!.Lines[0].UnitPrice);
        Assert.Equal(3.5m, unchanged.Lines[0].UnitCost);

        var resaved = await SendAsync<OrderResponse>(client, HttpMethod.Put, $"/api/orders/{order.Id}",
            Draft(null, VariantLine(variant.Id, 3, unitPrice: 20m, id: unchanged.Lines[0].Id)) with { Notes = "Changed quantity" });
        Assert.Equal(20m, resaved.Lines[0].UnitPrice);
        Assert.Equal(20m, resaved.Lines[0].ListUnitPrice);
        Assert.Equal(3.5m, resaved.Lines[0].UnitCost);
        Assert.Equal(60m, resaved.TotalAmount);

        var deleted = await client.DeleteAsync($"/api/products/{product.Id}/variants/{variant.Id}", cancellation);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var afterDelete = await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}", cancellation);
        Assert.Null(afterDelete!.Lines[0].VariantId);
        Assert.Equal("Vase - Large", afterDelete.Lines[0].Description);
        Assert.Equal(60m, afterDelete.TotalAmount);
    }

    [Fact]
    public async Task PriceOverrideDiscountsAndFreeTextLines_AreCalculated()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);

        var order = await CreateOrderAsync(client, new OrderRequest(null, "market", new DateOnly(2027, 1, 5), null, 2m, "Markt Gent", [
            VariantLine(variant.Id, 2, unitPrice: 18m, discountPercent: 10m),
            new OrderLineRequest(null, null, "Custom nameplate", 1, 12.5m, 1.2m, null),
        ], null));

        Assert.Equal("market", order.Channel);
        Assert.Equal(new DateOnly(2027, 1, 5), order.DueDate);
        Assert.Equal(18m, order.Lines[0].UnitPrice);
        Assert.Equal(20m, order.Lines[0].ListUnitPrice);
        Assert.Equal(32.4m, order.Lines[0].LineTotal);
        Assert.Null(order.Lines[1].VariantId);
        Assert.Null(order.Lines[1].CommercialRightsStatus);
        Assert.Equal(44.9m, order.Subtotal);
        Assert.Equal(42.9m, order.TotalAmount);
        Assert.Equal(8.2m, order.TotalCost);
        Assert.True(order.CostComplete);
    }

    [Fact]
    public async Task Create_RejectsInvalidInput()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);
        using var other = await SignedInClientAsync();
        var (_, foreignVariant) = await SeedVariantAsync(other, "Other");
        var foreignCustomer = await CreateCustomerAsync(other);

        var quantity = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(null, VariantLine(variant.Id, 0)), Token));
        Assert.True(quantity.TryGetProperty("lines[0].quantity", out _));

        var percent = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(null, VariantLine(variant.Id, 1, discountPercent: 120m)), Token));
        Assert.True(percent.TryGetProperty("lines[0].discountPercent", out _));

        var unknownVariant = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(null, VariantLine(foreignVariant.Id)), Token));
        Assert.True(unknownVariant.TryGetProperty("lines[0].variantId", out _));

        var freeText = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(null, new OrderLineRequest(null, null, " ", 1, null, null, null)), Token));
        Assert.True(freeText.TryGetProperty("lines[0].description", out _));
        Assert.True(freeText.TryGetProperty("lines[0].unitPrice", out _));

        var customer = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(foreignCustomer.Id), Token));
        Assert.True(customer.TryGetProperty("customerId", out _));

        var channel = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft() with { Channel = "carrier-pigeon" }, Token));
        Assert.True(channel.TryGetProperty("channel", out _));

        var discount = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(null, VariantLine(variant.Id)) with { DiscountAmount = 50m }, Token));
        Assert.True(discount.TryGetProperty("discountAmount", out _));

        Assert.Empty((await client.GetFromJsonAsync<List<OrderSummaryResponse>>("/api/orders", Token))!);
    }

    [Fact]
    public async Task ArchivedCustomersAndProducts_CannotBeAddedToNewOrders()
    {
        using var client = await SignedInClientAsync();
        var (product, variant) = await SeedVariantAsync(client);
        var customer = await CreateCustomerAsync(client);
        await client.PostAsync($"/api/customers/{customer.Id}/archive", null, Token);
        await client.PostAsync($"/api/products/{product.Id}/archive", null, Token);

        var archivedCustomer = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(customer.Id), Token));
        var archivedProduct = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(null, VariantLine(variant.Id)), Token));

        Assert.True(archivedCustomer.TryGetProperty("customerId", out _));
        Assert.True(archivedProduct.TryGetProperty("lines[0].variantId", out _));
    }

    [Fact]
    public async Task StatusWorkflow_ValidatesTransitionsAndStampsDates()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);
        var empty = await CreateOrderAsync(client, Draft());
        var order = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)));

        var noLines = await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/orders/{empty.Id}/status", new OrderStatusRequest("Confirmed", null), Token));
        Assert.True(noLines.TryGetProperty("lines", out _));

        var skip = await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Delivered", null), Token));
        Assert.True(skip.TryGetProperty("status", out _));
        var unknown = await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Shipped", null), Token));
        Assert.True(unknown.TryGetProperty("status", out _));

        var confirmed = await MoveAsync(client, order.Id, "confirmed");
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.True(confirmed.LinesEditable);

        var production = await MoveAsync(client, order.Id, "InProduction");
        Assert.False(production.LinesEditable);
        Assert.True(production.HeaderEditable);
        var locked = await ExpectBadRequestAsync(await client.PutAsJsonAsync($"/api/orders/{order.Id}", Draft(null, VariantLine(variant.Id, 5, id: production.Lines[0].Id)), Token));
        Assert.True(locked.TryGetProperty("lines", out _));

        await MoveAsync(client, order.Id, "Ready");
        var delivered = await MoveAsync(client, order.Id, "Delivered");
        Assert.NotNull(delivered.DeliveredAt);
        Assert.Empty(delivered.AllowedTransitions);
        Assert.False(delivered.HeaderEditable);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{order.Id}", Draft(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", null), Token)).StatusCode);
    }

    [Fact]
    public async Task Cancel_AndReopenReturnsTheOrderToDraft()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);
        var order = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)));
        await MoveAsync(client, order.Id, "Confirmed");

        var cancelled = await MoveAsync(client, order.Id, "Cancelled");
        Assert.NotNull(cancelled.CancelledAt);
        Assert.False(cancelled.PaymentsEditable);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/payments", new OrderPaymentRequest(1m, null, null, null), Token)).StatusCode);

        var reopened = await MoveAsync(client, order.Id, "Draft");
        Assert.Equal("Draft", reopened.Status);
        Assert.Null(reopened.CancelledAt);
    }

    [Fact]
    public async Task Payments_UpdatePaymentStatusAndRejectOverpayment()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);
        var order = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id, 2)));

        var partial = await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{order.Id}/payments", new OrderPaymentRequest(15m, null, "cash", "Deposit"));
        Assert.Equal("Partial", partial.PaymentStatus);
        Assert.Equal(15m, partial.AmountPaid);
        Assert.Equal(25m, partial.AmountDue);
        Assert.Equal("cash", Assert.Single(partial.Payments).Method);

        var tooMuch = await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/orders/{order.Id}/payments", new OrderPaymentRequest(25.01m, null, null, null), Token));
        Assert.True(tooMuch.TryGetProperty("amount", out _));
        var zero = await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/orders/{order.Id}/payments", new OrderPaymentRequest(0m, null, null, null), Token));
        Assert.True(zero.TryGetProperty("amount", out _));
        var method = await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/orders/{order.Id}/payments", new OrderPaymentRequest(1m, null, "bitcoin", null), Token));
        Assert.True(method.TryGetProperty("method", out _));

        var paid = await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{order.Id}/payments", new OrderPaymentRequest(25m, null, "transfer", null));
        Assert.Equal("Paid", paid.PaymentStatus);
        Assert.Equal(0m, paid.AmountDue);

        var removed = await SendAsync<OrderResponse>(client, HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{partial.Payments[0].Id}");
        Assert.Equal("Partial", removed.PaymentStatus);
        Assert.Equal(25m, removed.AmountPaid);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/orders/{order.Id}/payments/{Guid.NewGuid()}", Token)).StatusCode);
    }

    [Fact]
    public async Task List_FiltersByStatusPaymentCustomerAndSearch()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client, "Dragon");
        var jan = await CreateCustomerAsync(client, "Jan Peeters");
        var fatima = await CreateCustomerAsync(client, "Fatima");
        var draft = await CreateOrderAsync(client, Draft(jan.Id, VariantLine(variant.Id)));
        var paid = await CreateOrderAsync(client, Draft(fatima.Id, VariantLine(variant.Id)));
        await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{paid.Id}/payments", new OrderPaymentRequest(20m, null, null, null));
        await MoveAsync(client, paid.Id, "Confirmed");
        await MoveAsync(client, paid.Id, "Ready");
        await MoveAsync(client, paid.Id, "Delivered");
        var cancelled = await CreateOrderAsync(client, Draft(jan.Id, VariantLine(variant.Id)));
        await MoveAsync(client, cancelled.Id, "Cancelled");

        Task<List<OrderSummaryResponse>> ListAsync(string query) => client.GetFromJsonAsync<List<OrderSummaryResponse>>($"/api/orders{query}", Token)!;

        Assert.Equal(3, (await ListAsync(string.Empty)).Count);
        Assert.Equal(draft.Id, Assert.Single(await ListAsync("?status=active")).Id);
        Assert.Equal(paid.Id, Assert.Single(await ListAsync("?status=Delivered")).Id);
        Assert.Equal(2, (await ListAsync("?status=Draft,Cancelled")).Count);
        Assert.Equal(paid.Id, Assert.Single(await ListAsync("?payment=paid")).Id);
        Assert.Equal(2, (await ListAsync("?payment=unpaid")).Count);
        Assert.Equal(2, (await ListAsync($"?customerId={jan.Id}")).Count);
        Assert.Equal(paid.Id, Assert.Single(await ListAsync("?q=fatima")).Id);
        Assert.Equal(3, (await ListAsync("?q=dragon")).Count);
        Assert.Equal(cancelled.Id, Assert.Single(await ListAsync($"?q=%23{cancelled.Number}")).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?status=nonsense", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?payment=nonsense", Token)).StatusCode);

        var summary = Assert.Single(await ListAsync("?status=Delivered"));
        Assert.Equal("Fatima", summary.CustomerName);
        Assert.Equal("Paid", summary.PaymentStatus);
        Assert.Equal(1, summary.LineCount);
    }

    [Fact]
    public async Task Overdue_IsFlaggedOnlyForOpenOrders()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);
        var late = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)) with { DueDate = new DateOnly(2020, 1, 1) });
        var done = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)) with { DueDate = new DateOnly(2020, 1, 1) });
        await MoveAsync(client, done.Id, "Cancelled");

        Assert.True(late.IsOverdue);
        var list = await client.GetFromJsonAsync<List<OrderSummaryResponse>>("/api/orders", Token);
        Assert.True(list!.Single(order => order.Id == late.Id).IsOverdue);
        Assert.False(list!.Single(order => order.Id == done.Id).IsOverdue);
    }

    [Fact]
    public async Task Delete_OnlyForDraftsWithoutPayments()
    {
        using var client = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(client);
        var draft = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)));
        var paidDraft = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)));
        await SendAsync<OrderResponse>(client, HttpMethod.Post, $"/api/orders/{paidDraft.Id}/payments", new OrderPaymentRequest(5m, null, null, null));
        var confirmed = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)));
        await MoveAsync(client, confirmed.Id, "Confirmed");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/orders/{draft.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{draft.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/orders/{paidDraft.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/orders/{confirmed.Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task CustomerWithOrders_CannotBeDeletedButCanBeArchived()
    {
        using var client = await SignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        await CreateOrderAsync(client, Draft(customer.Id));

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/customers/{customer.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/customers/{customer.Id}/archive", null, Token)).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<OrderSummaryResponse>>($"/api/orders?customerId={customer.Id}", Token))!);
    }

    [Fact]
    public async Task UnclearedCommercialRights_RequireAcknowledgementOnTheLineAndAreAudited()
    {
        using var client = await SignedInClientAsync();
        var (product, variant) = await SeedVariantAsync(client);
        await AttachUnlicensedModelAsync(client, product, variant);

        var blocked = await ExpectBadRequestAsync(await client.PostAsJsonAsync("/api/orders", Draft(null, VariantLine(variant.Id)), Token));
        Assert.True(blocked.TryGetProperty("lines[0].commercialRights", out var message));
        Assert.Contains("Unlicensed", message[0].GetString());
        Assert.Empty((await client.GetFromJsonAsync<List<OrderSummaryResponse>>("/api/orders", Token))!);

        var order = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)) with { AcknowledgeRisk = true });

        var line = Assert.Single(order.Lines);
        Assert.Equal("NotCleared", line.CommercialRightsStatus);
        Assert.Equal(["Unlicensed"], line.CommercialRightsBlockers);
        Assert.True(line.RiskAcknowledged);
        Assert.True(order.HasRightsWarning);

        var confirmed = await MoveAsync(client, order.Id, "Confirmed");
        Assert.Equal("Confirmed", confirmed.Status);

        var audit = await client.GetStringAsync("/api/audit?entityType=OrderLine&pageSize=50", Token);
        Assert.Contains("riskAcknowledgedAt", audit);
    }

    [Fact]
    public async Task RightsLostAfterAddingTheLine_AreCheckedAgainWhenConfirming()
    {
        using var client = await SignedInClientAsync();
        var (product, variant) = await SeedVariantAsync(client);
        var order = await CreateOrderAsync(client, Draft(null, VariantLine(variant.Id)));
        Assert.False(order.HasRightsWarning);
        await AttachUnlicensedModelAsync(client, product, variant);

        var refreshed = await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}", Token);
        Assert.True(refreshed!.HasRightsWarning);
        Assert.False(refreshed.Lines[0].RiskAcknowledged);

        var blocked = await ExpectBadRequestAsync(await client.PostAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null), Token));
        Assert.True(blocked.TryGetProperty("commercialRights", out _));

        var confirmed = await MoveAsync(client, order.Id, "Confirmed", acknowledge: true);
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.True(confirmed.Lines[0].RiskAcknowledged);
    }

    [Fact]
    public async Task Orders_AreIsolatedPerWorkspace()
    {
        using var owner = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var (_, variant) = await SeedVariantAsync(owner);
        var order = await CreateOrderAsync(owner, Draft(null, VariantLine(variant.Id)));

        Assert.Empty((await other.GetFromJsonAsync<List<OrderSummaryResponse>>("/api/orders", Token))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{order.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/orders/{order.Id}", Draft(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/orders/{order.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/orders/{order.Id}/payments", new OrderPaymentRequest(1m, null, null, null), Token)).StatusCode);

        var second = await CreateOrderAsync(other, Draft());
        Assert.Equal(1, second.Number);
    }
}
