using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class ProductionQueueRulesTests
{
    [Theory]
    [InlineData(5, 2, new[] { 2, 2, 1 })]
    [InlineData(5, null, new[] { 5 })]
    [InlineData(3, 10, new[] { 3 })]
    [InlineData(0, 2, new int[0])]
    public void SplitUnits_ChunksTheQuantity(int units, int? perJob, int[] expected)
    {
        Assert.Equal(expected, ProductionQueueRules.SplitUnits(units, perJob));
    }

    [Fact]
    public void Production_CountsOnlyDoneJobsAsProduced()
    {
        var line = new OrderLine { Quantity = 10 };
        var jobs = new[]
        {
            new PrintJob { Status = PrintJobStatus.Done, FinishedUnits = 4 },
            new PrintJob { Status = PrintJobStatus.Failed, PlannedUnits = 3, FinishedUnits = null },
            new PrintJob { Status = PrintJobStatus.Cancelled, PlannedUnits = 3 },
            new PrintJob { Status = PrintJobStatus.Planned, PlannedUnits = 2 },
            new PrintJob { Status = PrintJobStatus.Printing, PlannedUnits = 1 },
        };

        var production = ProductionQueueRules.ProductionOf(line, jobs);

        Assert.Equal(4, production.Produced);
        Assert.Equal(3, production.Queued);
        Assert.Equal(3, production.Remaining);
        Assert.False(production.Complete);
    }

    [Fact]
    public void OrderPlanned_SortsByManualPositionThenDueDateThenOrderNumber()
    {
        var early = new PrintJob { OrderLineId = Guid.NewGuid() };
        var late = new PrintJob { OrderLineId = Guid.NewGuid() };
        var none = new PrintJob();
        var manual = new PrintJob { QueuePosition = 1 };
        var orders = new Dictionary<Guid, QueueOrderInfo>
        {
            [early.OrderLineId!.Value] = new(early.OrderLineId.Value, Guid.NewGuid(), 7, OrderStatus.Confirmed, new DateOnly(2027, 1, 1), null, "A", 1),
            [late.OrderLineId!.Value] = new(late.OrderLineId.Value, Guid.NewGuid(), 3, OrderStatus.Confirmed, new DateOnly(2027, 2, 1), null, "B", 1),
        };

        var ordered = ProductionQueueRules.OrderPlanned([none, late, early, manual], orders);

        Assert.Equal([manual, early, late, none], ordered);
    }
}

public class ProductionQueueTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Queue"), Token);
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
        Assert.True(response.StatusCode == expected, $"{method} {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static Task<T> PostAsync<T>(HttpClient client, string url, object? body, HttpStatusCode expected = HttpStatusCode.Created) =>
        SendAsync<T>(client, HttpMethod.Post, url, body, expected);

    private static Task<PrinterResponse> CreatePrinterAsync(HttpClient client, string name = "A1 #1") =>
        PostAsync<PrinterResponse>(client, "/api/printers", new PrinterRequest(name, "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000));

    private static async Task<SpoolResponse> CreateSpoolAsync(HttpClient client, int weight = 1000)
    {
        var material = await PostAsync<MaterialResponse>(client, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"));
        return await PostAsync<SpoolResponse>(client, "/api/spools", new SpoolRequest(material.Id, null, "Jade White", "#F4F4F0", 20m, weight));
    }

    private static async Task<OrderResponse> ConfirmedOrderAsync(HttpClient client, DateOnly? dueDate, params int[] quantities)
    {
        var lines = quantities.Select((quantity, index) => new OrderLineRequest(null, null, $"Part {index + 1}", quantity, 5m, null, null)).ToList();
        var order = await PostAsync<OrderResponse>(client, "/api/orders", new OrderRequest(null, null, dueDate, null, null, null, lines, null));
        return await PostAsync<OrderResponse>(client, $"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", null), HttpStatusCode.OK);
    }

    private static Task<OrderResponse> GetOrderAsync(HttpClient client, Guid id) => SendAsync<OrderResponse>(client, HttpMethod.Get, $"/api/orders/{id}");

    private static Task<OrderProductionResponse> ProgressAsync(HttpClient client, Guid orderId) =>
        SendAsync<OrderProductionResponse>(client, HttpMethod.Get, $"/api/production/orders/{orderId}");

    private static Task<QueueResponse> QueueAsync(HttpClient client) => SendAsync<QueueResponse>(client, HttpMethod.Get, "/api/production/queue");

    private static async Task<List<PrintJobResponse>> CreateJobsAsync(HttpClient client, Guid orderId, CreateOrderJobsRequest? request = null) =>
        await PostAsync<List<PrintJobResponse>>(client, $"/api/production/orders/{orderId}/jobs", request ?? new CreateOrderJobsRequest(null, null, null));

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/production/queue", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/production/suggestions", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/production/orders/{id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/production/orders/{id}/jobs", new CreateOrderJobsRequest(null, null, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/production/queue/{id}/move", new QueueMoveRequest("up"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/print-jobs/{id}/requeue", new { }, Token)).StatusCode);
    }

    [Fact]
    public async Task CreateJobs_SplitsLinesIntoJobsAndOnlySuggestsWhatIsLeft()
    {
        using var client = await SignedInClientAsync();
        await CreatePrinterAsync(client);
        var order = await ConfirmedOrderAsync(client, null, 5, 3);

        var suggestions = await SendAsync<List<ProductionSuggestionResponse>>(client, HttpMethod.Get, "/api/production/suggestions");
        var suggestion = Assert.Single(suggestions);
        Assert.Equal([5, 3], suggestion.Lines.Select(line => line.Remaining));

        var created = await CreateJobsAsync(client, order.Id, new CreateOrderJobsRequest(order.Lines[0].Id, null, 2));
        Assert.Equal([2, 2, 1], created.Select(job => job.PlannedUnits));
        Assert.All(created, job => Assert.Equal("Planned", job.Status));
        Assert.All(created, job => Assert.Equal(order.Lines[0].Id, job.OrderLineId));
        Assert.Contains("#" + order.Number, created[0].Name);

        var remaining = Assert.Single(await SendAsync<List<ProductionSuggestionResponse>>(client, HttpMethod.Get, "/api/production/suggestions"));
        Assert.Equal(order.Lines[1].Id, Assert.Single(remaining.Lines).LineId);

        await CreateJobsAsync(client, order.Id);
        Assert.Empty(await SendAsync<List<ProductionSuggestionResponse>>(client, HttpMethod.Get, "/api/production/suggestions"));
        var nothingLeft = await client.PostAsJsonAsync($"/api/production/orders/{order.Id}/jobs", new CreateOrderJobsRequest(null, null, null), Token);
        Assert.Equal(HttpStatusCode.BadRequest, nothingLeft.StatusCode);
    }

    [Fact]
    public async Task CreateJobs_NeedsAConfirmedOrderAndAPrinter()
    {
        using var client = await SignedInClientAsync();
        var draft = await PostAsync<OrderResponse>(client, "/api/orders", new OrderRequest(null, null, null, null, null, null, [new OrderLineRequest(null, null, "Part", 2, 5m, null, null)], null));

        var notConfirmed = await client.PostAsJsonAsync($"/api/production/orders/{draft.Id}/jobs", new CreateOrderJobsRequest(null, null, null), Token);
        Assert.Equal(HttpStatusCode.BadRequest, notConfirmed.StatusCode);
        Assert.Contains("status", await notConfirmed.Content.ReadAsStringAsync(Token));

        var confirmed = await PostAsync<OrderResponse>(client, $"/api/orders/{draft.Id}/status", new OrderStatusRequest("Confirmed", null), HttpStatusCode.OK);
        var noPrinter = await client.PostAsJsonAsync($"/api/production/orders/{confirmed.Id}/jobs", new CreateOrderJobsRequest(null, null, null), Token);
        Assert.Equal(HttpStatusCode.BadRequest, noPrinter.StatusCode);
        Assert.Contains("printerId", await noPrinter.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task StartingAndFinishingJobs_DriveTheOrderStatus()
    {
        using var client = await SignedInClientAsync();
        await CreatePrinterAsync(client);
        var order = await ConfirmedOrderAsync(client, null, 4);
        var jobs = await CreateJobsAsync(client, order.Id, new CreateOrderJobsRequest(null, null, 2));
        Assert.Equal("Confirmed", (await GetOrderAsync(client, order.Id)).Status);

        await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{jobs[0].Id}/start", null, HttpStatusCode.OK);
        Assert.Equal("InProduction", (await GetOrderAsync(client, order.Id)).Status);

        var done = await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{jobs[0].Id}/finish", new FinishPrintJobRequest("Done", null, 30, null, null, null), HttpStatusCode.OK);
        Assert.Equal(2, done.FinishedUnits);
        Assert.Equal("InProduction", (await GetOrderAsync(client, order.Id)).Status);
        var halfway = await ProgressAsync(client, order.Id);
        Assert.Equal((4, 2, 50), (halfway.Ordered, halfway.Produced, halfway.Percent));

        await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{jobs[1].Id}/finish", new FinishPrintJobRequest("Done", null, 30, null, 2, null), HttpStatusCode.OK);
        Assert.Equal("Ready", (await GetOrderAsync(client, order.Id)).Status);
        var progress = await ProgressAsync(client, order.Id);
        Assert.True(progress.Complete);
        Assert.Equal(100, progress.Percent);
        Assert.Empty((await QueueAsync(client)).Items);
    }

    [Fact]
    public async Task FailedJobs_DoNotCountAndCanBeRequeuedOnce()
    {
        using var client = await SignedInClientAsync();
        await CreatePrinterAsync(client);
        var order = await ConfirmedOrderAsync(client, null, 3);
        var job = (await CreateJobsAsync(client, order.Id))[0];
        await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{job.Id}/start", null, HttpStatusCode.OK);

        var failed = await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{job.Id}/finish", new FinishPrintJobRequest("Failed", null, 10, null, null, null), HttpStatusCode.OK);
        var progress = await ProgressAsync(client, order.Id);
        Assert.Equal(0, progress.Produced);
        Assert.Equal(1, progress.FailedJobs);
        Assert.Equal(3, progress.Lines[0].Remaining);
        Assert.Equal("InProduction", (await GetOrderAsync(client, order.Id)).Status);

        var copy = await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{failed.Id}/requeue", null);
        Assert.Equal("Planned", copy.Status);
        Assert.Equal(failed.Id, copy.RequeuedFromId);
        Assert.Equal(3, copy.PlannedUnits);
        Assert.Equal(order.Lines[0].Id, copy.OrderLineId);
        Assert.Equal(0, (await ProgressAsync(client, order.Id)).FailedJobs);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/print-jobs/{failed.Id}/requeue", new { }, Token)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/print-jobs/{copy.Id}/requeue", new { }, Token)).StatusCode);
    }

    [Fact]
    public async Task Queue_OrdersByDueDateAndSupportsManualMoves()
    {
        using var client = await SignedInClientAsync();
        await CreatePrinterAsync(client);
        var later = await ConfirmedOrderAsync(client, new DateOnly(2027, 3, 1), 1);
        var sooner = await ConfirmedOrderAsync(client, new DateOnly(2027, 1, 1), 1);
        var undated = await ConfirmedOrderAsync(client, null, 1);
        await CreateJobsAsync(client, later.Id);
        await CreateJobsAsync(client, undated.Id);
        await CreateJobsAsync(client, sooner.Id);

        var queue = await QueueAsync(client);
        Assert.Equal([sooner.Number, later.Number, undated.Number], queue.Items.Select(item => item.OrderNumber!.Value));
        Assert.False(queue.ManualOrder);
        Assert.False(queue.Items[0].CanMoveUp);
        Assert.True(queue.Items[0].CanMoveDown);

        queue = await PostAsync<QueueResponse>(client, $"/api/production/queue/{queue.Items[2].Id}/move", new QueueMoveRequest("top"), HttpStatusCode.OK);
        Assert.Equal([undated.Number, sooner.Number, later.Number], queue.Items.Select(item => item.OrderNumber!.Value));
        Assert.True(queue.ManualOrder);

        queue = await PostAsync<QueueResponse>(client, $"/api/production/queue/{queue.Items[0].Id}/move", new QueueMoveRequest("down"), HttpStatusCode.OK);
        Assert.Equal([sooner.Number, undated.Number, later.Number], queue.Items.Select(item => item.OrderNumber!.Value));
        Assert.Equal([sooner.Number, undated.Number, later.Number], (await QueueAsync(client)).Items.Select(item => item.OrderNumber!.Value));

        queue = await PostAsync<QueueResponse>(client, "/api/production/queue/reset", null, HttpStatusCode.OK);
        Assert.Equal([sooner.Number, later.Number, undated.Number], queue.Items.Select(item => item.OrderNumber!.Value));
        Assert.False(queue.ManualOrder);

        var bad = await client.PostAsJsonAsync($"/api/production/queue/{queue.Items[0].Id}/move", new QueueMoveRequest("sideways"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Queue_WarnsWhenTheSpoolCannotCoverTheQueuedJobs()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var spool = await CreateSpoolAsync(client, 100);
        var first = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(printer.Id, "First", "Planned", null, null, 60, null, null, null, null, [new PrintJobFilamentRequest(spool.Id, 70, 0)]));
        var second = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(printer.Id, "Second", "Planned", null, null, null, null, null, null, null, [new PrintJobFilamentRequest(spool.Id, 50, 0)]));
        var bare = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(printer.Id, "Bare", "Planned", null, null, null, null, null, null, null, []));

        var queue = await QueueAsync(client);

        Assert.Equal([first.Id, second.Id, bare.Id], queue.Items.Select(item => item.Id));
        Assert.False(queue.Items[0].FilamentShort);
        Assert.True(queue.Items[1].FilamentShort);
        Assert.Equal(100, queue.Items[1].Filaments[0].AvailableGrams);
        Assert.False(queue.Items[2].HasFilament);
        Assert.Equal(60, queue.EstimatedMinutes);
        Assert.Equal(2, queue.WithoutEstimate);
    }

    [Fact]
    public async Task QueuedJob_CanChangePrinterDurationAndUnits()
    {
        using var client = await SignedInClientAsync();
        var first = await CreatePrinterAsync(client);
        var second = await CreatePrinterAsync(client, "A1 #2");
        var job = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(first.Id, "Part", "Planned", null, null, null, null, null, null, null, []));

        var queue = await SendAsync<QueueResponse>(client, HttpMethod.Patch, $"/api/production/queue/{job.Id}", new QueueUpdateRequest(second.Id, 90, 6));

        var item = Assert.Single(queue.Items);
        Assert.Equal((second.Id, 90, 6), (item.PrinterId, item.DurationMinutes, item.PlannedUnits));
        var rejected = await client.PatchAsJsonAsync($"/api/production/queue/{job.Id}", new QueueUpdateRequest(Guid.NewGuid(), null, 0), Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task LinkingAnImportedDoneJob_CountsItsUnitsAndCompletesTheOrder()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var order = await ConfirmedOrderAsync(client, null, 2);
        var imported = await PostAsync<PrintJobResponse>(client, "/api/print-jobs", new PrintJobRequest(printer.Id, "Bambu task", "Done", null, null, 45, null, null, "bambu-cloud", "task-1", []));

        var missingUnits = await client.PostAsJsonAsync($"/api/print-jobs/{imported.Id}/order-line", new LinkOrderLineRequest(order.Lines[0].Id, null), Token);
        Assert.Equal(HttpStatusCode.BadRequest, missingUnits.StatusCode);

        var linked = await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{imported.Id}/order-line", new LinkOrderLineRequest(order.Lines[0].Id, 2), HttpStatusCode.OK);
        Assert.Equal(2, linked.FinishedUnits);
        Assert.Equal(order.Number, linked.OrderNumber);
        Assert.Equal("Ready", (await GetOrderAsync(client, order.Id)).Status);

        var unlinked = await PostAsync<PrintJobResponse>(client, $"/api/print-jobs/{imported.Id}/order-line", new LinkOrderLineRequest(null, null), HttpStatusCode.OK);
        Assert.Null(unlinked.OrderLineId);
        Assert.Equal(0, (await ProgressAsync(client, order.Id)).Produced);
    }

    [Fact]
    public async Task EditingAPlannedJob_KeepsItsOrderLinkWhenTheBodyOmitsIt()
    {
        using var client = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(client);
        var order = await ConfirmedOrderAsync(client, null, 2);
        var job = (await CreateJobsAsync(client, order.Id))[0];

        var edited = await SendAsync<PrintJobResponse>(client, HttpMethod.Put, $"/api/print-jobs/{job.Id}", new PrintJobRequest(printer.Id, "Renamed", "Planned", null, null, null, null, null, null, null, []), HttpStatusCode.OK);

        Assert.Equal(order.Lines[0].Id, edited.OrderLineId);
        Assert.Equal(2, edited.PlannedUnits);
    }

    [Fact]
    public async Task OtherWorkspaces_CannotSeeOrTouchTheQueue()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var printer = await CreatePrinterAsync(owner);
        var order = await ConfirmedOrderAsync(owner, null, 2);
        var job = (await CreateJobsAsync(owner, order.Id))[0];
        var strangerPrinter = await CreatePrinterAsync(stranger);
        var strangerJob = await PostAsync<PrintJobResponse>(stranger, "/api/print-jobs", new PrintJobRequest(strangerPrinter.Id, "Mine", "Failed", null, null, null, null, null, null, null, []));

        Assert.Empty((await QueueAsync(stranger)).Items);
        Assert.Empty(await SendAsync<List<ProductionSuggestionResponse>>(stranger, HttpMethod.Get, "/api/production/suggestions"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/production/orders/{order.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/production/orders/{order.Id}/jobs", new CreateOrderJobsRequest(null, null, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/production/queue/{job.Id}/move", new QueueMoveRequest("up"), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/print-jobs/{job.Id}/requeue", new { }, Token)).StatusCode);

        var link = await stranger.PostAsJsonAsync($"/api/print-jobs/{strangerJob.Id}/order-line", new LinkOrderLineRequest(order.Lines[0].Id, 1), Token);
        Assert.Equal(HttpStatusCode.BadRequest, link.StatusCode);
        Assert.Equal(printer.Id, (await owner.GetFromJsonAsync<PrintJobResponse>($"/api/print-jobs/{job.Id}", Token))!.PrinterId);
    }
}
