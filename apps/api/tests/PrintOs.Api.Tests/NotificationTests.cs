using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Licensing;
using PrintOs.Api.Modules.Notifications;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class NotificationRulesTests
{
    [Theory]
    [InlineData(23, 0, 0, true)]
    [InlineData(6, 59, 0, true)]
    [InlineData(7, 0, 0, false)]
    [InlineData(12, 0, 0, false)]
    [InlineData(21, 30, 120, true)]
    [InlineData(21, 30, -120, false)]
    public void QuietHours_WrapMidnightAndUseTheClientOffset(int hour, int minute, int offset, bool expected)
    {
        var settings = new NotificationSettings { QuietHoursEnabled = true, QuietStartMinute = 22 * 60, QuietEndMinute = 7 * 60, UtcOffsetMinutes = offset };

        Assert.Equal(expected, QuietHours.IsQuiet(settings, new DateTimeOffset(2027, 3, 1, hour, minute, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void QuietHours_AreOffWhenDisabledOrEmpty()
    {
        var now = new DateTimeOffset(2027, 3, 1, 23, 0, 0, TimeSpan.Zero);

        Assert.False(QuietHours.IsQuiet(null, now));
        Assert.False(QuietHours.IsQuiet(new NotificationSettings { QuietHoursEnabled = false }, now));
        Assert.False(QuietHours.IsQuiet(new NotificationSettings { QuietHoursEnabled = true, QuietStartMinute = 60, QuietEndMinute = 60 }, now));
    }

    [Fact]
    public void Texts_AreRenderedPerLanguageWithoutLeavingPlaceholders()
    {
        var parameters = new Dictionary<string, string> { ["job"] = "Vase", ["printer"] = "A1" };

        var (nlSubject, _) = NotificationTexts.Render(NotificationTypes.PrintFailed, "nl", parameters);
        var (enSubject, enBody) = NotificationTexts.Render(NotificationTypes.PrintFailed, "en", parameters);

        Assert.Equal("PrintOS: Print mislukt: Vase op A1", nlSubject);
        Assert.Equal("PrintOS: Print failed: Vase on A1", enSubject);
        Assert.DoesNotContain("{", enBody);
        Assert.All(NotificationTypes.All, type => Assert.DoesNotContain("{job}", NotificationTexts.Render(type, "en", new Dictionary<string, string> { ["job"] = "x", ["printer"] = "x", ["material"] = "x", ["license"] = "x", ["number"] = "1" }).Subject));
    }

    [Fact]
    public void Maintenance_IsDueByPrintHoursOrDays()
    {
        var now = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var printer = new Printer { CreatedAt = now.AddDays(-10), MaintenanceEveryHours = 50, MaintenanceEveryDays = 30 };

        Assert.False(PrinterMaintenanceRules.Evaluate(printer, 49 * 60, now).Due);
        Assert.True(PrinterMaintenanceRules.Evaluate(printer, 50 * 60, now).Due);
        printer.MaintenanceEveryHours = null;
        Assert.False(PrinterMaintenanceRules.Evaluate(printer, 5000 * 60, now).Due);
        printer.LastMaintenanceAt = now.AddDays(-31);
        Assert.True(PrinterMaintenanceRules.Evaluate(printer, 0, now).Due);
    }
}

public sealed class RecordingEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = [];

    public bool Fail { get; set; }

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("smtp down");
        }

        Sent.Add((to, subject, body));
        return Task.CompletedTask;
    }
}

public class NotificationTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Noon = DateTimeOffset.UtcNow;

    private sealed record Session(HttpClient Client, Guid WorkspaceId, string Email, string AccessToken);

    private static async Task<Session> SignInAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Notify"), Token);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), Token);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(Token);
        var accessToken = tokens.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var workspace = await ScheduleTests.SendAsync<WorkspaceSummary>(client, HttpMethod.Get, "/api/workspace");
        return new Session(client, workspace.Id, email, accessToken);
    }

    private Task<Session> SignInAsync() => SignInAsync(factory);

    private static Task<NotificationListResponse> ListAsync(Session session, string query = "") =>
        ScheduleTests.SendAsync<NotificationListResponse>(session.Client, HttpMethod.Get, "/api/notifications" + query);

    private static async Task SweepAsync(WebApplicationFactory<Program> app, Session session, DateTimeOffset? now = null)
    {
        var worker = app.Services.GetRequiredService<NotificationWorker>();
        Assert.True(await worker.SweepWorkspaceAsync(session.WorkspaceId, now ?? DateTimeOffset.UtcNow, Token));
    }

    private Task SweepAsync(Session session, DateTimeOffset? now = null) => SweepAsync(factory, session, now);

    private static PrintOsDbContext Db(WebApplicationFactory<Program> app, Guid workspaceId)
    {
        var scope = app.Services.CreateScope();
        return new PrintOsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<PrintOsDbContext>>(), new FixedWorkspace(workspaceId));
    }

    private static async Task<(MaterialResponse Material, SpoolResponse Spool)> MaterialWithSpoolAsync(HttpClient client, int grams, int low, int reorder)
    {
        var material = await ScheduleTests.SendAsync<MaterialResponse>(client, HttpMethod.Post, "/api/materials", new MaterialRequest($"PLA {Guid.NewGuid():N}", "PLA"), HttpStatusCode.Created);
        var spool = await ScheduleTests.SendAsync<SpoolResponse>(client, HttpMethod.Post, "/api/spools", new SpoolRequest(material.Id, null, "White", "#FFFFFF", 20m, grams), HttpStatusCode.Created);
        await ScheduleTests.SendAsync<MaterialResponse>(client, HttpMethod.Put, $"/api/materials/{material.Id}/thresholds", new MaterialThresholdsRequest(low, reorder));
        return (material, spool);
    }

    private static Task AdjustAsync(HttpClient client, Guid spoolId, int delta) =>
        ScheduleTests.SendAsync<JsonElement>(client, HttpMethod.Post, $"/api/spools/{spoolId}/adjustments", new StockAdjustmentRequest(delta, "test"), HttpStatusCode.OK);

    private static async Task<PrintJobResponse> FinishedJobAsync(HttpClient client, Guid printerId, string status, int minutes = 30)
    {
        var job = await ScheduleTests.CreatePlannedJobAsync(client, printerId, $"Job {Guid.NewGuid():N}"[..12], minutes);
        return await ScheduleTests.SendAsync<PrintJobResponse>(client, HttpMethod.Post, $"/api/print-jobs/{job.Id}/finish", new FinishPrintJobRequest(status, null, minutes, null, null, null));
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notifications", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notifications/unread-count", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/notifications/read-all", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/notifications/{id}/read", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notifications/preferences", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/printers/{id}/maintenance", Token)).StatusCode);
    }

    [Fact]
    public async Task LowStock_NotifiesOnTheTransitionOnlyAndEscalatesToLow()
    {
        var session = await SignInAsync();
        var (_, spool) = await MaterialWithSpoolAsync(session.Client, 1000, 100, 300);
        await SweepAsync(session);
        Assert.Empty((await ListAsync(session)).Items);

        await AdjustAsync(session.Client, spool.Id, -750);
        await SweepAsync(session);
        await SweepAsync(session);
        var first = await ListAsync(session);
        var reorder = Assert.Single(first.Items);
        Assert.Equal(NotificationTypes.ReorderSoon, reorder.Type);
        Assert.Equal(1, first.UnreadCount);
        Assert.Equal("PLA", reorder.Parameters["material"][..3]);

        await AdjustAsync(session.Client, spool.Id, -200);
        await SweepAsync(session);
        var second = await ListAsync(session);
        Assert.Equal([NotificationTypes.LowStock, NotificationTypes.ReorderSoon], second.Items.Select(item => item.Type).Order().ToList());
    }

    [Fact]
    public async Task LowStock_FlappingAroundTheThresholdDoesNotSpam()
    {
        var session = await SignInAsync();
        var (_, spool) = await MaterialWithSpoolAsync(session.Client, 1000, 100, 300);

        for (var round = 0; round < 3; round++)
        {
            await AdjustAsync(session.Client, spool.Id, -750);
            await SweepAsync(session);
            await AdjustAsync(session.Client, spool.Id, 750);
            await SweepAsync(session);
        }

        Assert.Single((await ListAsync(session)).Items);

        await AdjustAsync(session.Client, spool.Id, -750);
        await SweepAsync(session, DateTimeOffset.UtcNow.AddHours(7));
        Assert.Equal(2, (await ListAsync(session)).Items.Count);
    }

    [Fact]
    public async Task PrintFailedAndFinished_NotifyOncePerJob()
    {
        var session = await SignInAsync();
        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        await FinishedJobAsync(session.Client, printer.Id, "Failed");
        await FinishedJobAsync(session.Client, printer.Id, "Done");

        await SweepAsync(session);
        await SweepAsync(session);

        var items = (await ListAsync(session)).Items;
        Assert.Equal([NotificationTypes.PrintFailed, NotificationTypes.PrintFinished], items.Select(item => item.Type).Order().ToList());
        Assert.All(items, item => Assert.StartsWith("/print-jobs/", item.LinkPath));
    }

    [Fact]
    public async Task Maintenance_IsDueFromPrintHoursAndResetsWhenDone()
    {
        var session = await SignInAsync();
        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        var configured = await ScheduleTests.SendAsync<PrinterMaintenanceResponse>(session.Client, HttpMethod.Put, $"/api/printers/{printer.Id}/maintenance", new PrinterMaintenanceRequest(2, null));
        Assert.False(configured.Due);
        await FinishedJobAsync(session.Client, printer.Id, "Done", 150);

        var due = await ScheduleTests.SendAsync<PrinterMaintenanceResponse>(session.Client, HttpMethod.Get, $"/api/printers/{printer.Id}/maintenance");
        Assert.True(due.Due);
        await SweepAsync(session);
        await SweepAsync(session);
        Assert.Single((await ListAsync(session)).Items, item => item.Type == NotificationTypes.MaintenanceDue);

        var done = await ScheduleTests.SendAsync<PrinterMaintenanceResponse>(session.Client, HttpMethod.Post, $"/api/printers/{printer.Id}/maintenance/done");
        Assert.False(done.Due);
        Assert.Equal(0, done.PrintMinutesSince);
        await SweepAsync(session);
        Assert.Single((await ListAsync(session)).Items, item => item.Type == NotificationTypes.MaintenanceDue);
    }

    [Fact]
    public async Task Maintenance_ValidatesAndIsIsolatedPerWorkspace()
    {
        var mine = await SignInAsync();
        var other = await SignInAsync();
        var printer = await ScheduleTests.CreatePrinterAsync(mine.Client, "A1");

        var bad = await mine.Client.PutAsJsonAsync($"/api/printers/{printer.Id}/maintenance", new PrinterMaintenanceRequest(0, 99999), Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync(Token);
        Assert.Contains("everyHours", body);
        Assert.Contains("everyDays", body);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/printers/{printer.Id}/maintenance", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/printers/{printer.Id}/maintenance/done", null, Token)).StatusCode);
    }

    [Fact]
    public async Task OrdersAndLicenses_NotifyWhenTheyBecomeDueOrExpire()
    {
        var session = await SignInAsync();
        var overdue = await ScheduleTests.SendAsync<OrderResponse>(session.Client, HttpMethod.Post, "/api/orders", new OrderRequest(null, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), null, null, null, [new OrderLineRequest(null, null, "Part", 1, 5m, null, null)], null), HttpStatusCode.Created);
        await ScheduleTests.SendAsync<OrderResponse>(session.Client, HttpMethod.Post, $"/api/orders/{overdue.Id}/status", new OrderStatusRequest("Confirmed", null));
        var soon = await ScheduleTests.SendAsync<OrderResponse>(session.Client, HttpMethod.Post, "/api/orders", new OrderRequest(null, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null, null, null, [new OrderLineRequest(null, null, "Part", 1, 5m, null, null)], null), HttpStatusCode.Created);
        await ScheduleTests.SendAsync<OrderResponse>(session.Client, HttpMethod.Post, $"/api/orders/{soon.Id}/status", new OrderStatusRequest("Confirmed", null));
        await using (var dbContext = Db(factory, session.WorkspaceId))
        {
            dbContext.Set<License>().Add(new License { Name = "Dragon", ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) });
            dbContext.Set<License>().Add(new License { Name = "Far away", ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(400)) });
            await dbContext.SaveChangesAsync(Token);
        }

        await SweepAsync(session);
        await SweepAsync(session);

        var types = (await ListAsync(session)).Items.Select(item => item.Type).Order().ToList();
        Assert.Equal([NotificationTypes.LicenseExpiring, NotificationTypes.OrderDueSoon, NotificationTypes.OrderOverdue], types);
    }

    [Fact]
    public async Task PrinterOffline_NeedsTheConfiguredMinutesAndIgnoresFlapping()
    {
        var session = await SignInAsync();
        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        Guid deviceId;
        await using (var dbContext = Db(factory, session.WorkspaceId))
        {
            var connection = new BambuConnection { WorkspaceId = session.WorkspaceId, EncryptedAccessToken = "x", AccountUid = "1" };
            dbContext.Set<BambuConnection>().Add(connection);
            var device = new BambuDevice { WorkspaceId = session.WorkspaceId, ConnectionId = connection.Id, Serial = "S1", Name = "A1", PrinterId = printer.Id, Online = false, LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-3) };
            dbContext.Set<BambuDevice>().Add(device);
            await dbContext.SaveChangesAsync(Token);
            deviceId = device.Id;
        }

        await SweepAsync(session);
        Assert.Empty((await ListAsync(session)).Items);

        await SweepAsync(session, DateTimeOffset.UtcNow.AddMinutes(20));
        await SweepAsync(session, DateTimeOffset.UtcNow.AddMinutes(21));
        Assert.Single((await ListAsync(session)).Items, item => item.Type == NotificationTypes.PrinterOffline);

        var start = DateTimeOffset.UtcNow;
        for (var round = 0; round < 3; round++)
        {
            var back = start.AddMinutes(25 + (round * 10));
            await SetDeviceAsync(session.WorkspaceId, deviceId, online: true, lastSeen: back.AddMinutes(-5));
            await SweepAsync(session, back.AddMinutes(-4));
            var gone = back.AddMinutes(8);
            await SetDeviceAsync(session.WorkspaceId, deviceId, online: false, lastSeen: gone.AddMinutes(-11));
            await SweepAsync(session, gone);
        }

        Assert.Single((await ListAsync(session)).Items, item => item.Type == NotificationTypes.PrinterOffline);
    }

    private async Task SetDeviceAsync(Guid workspaceId, Guid deviceId, bool online, DateTimeOffset lastSeen)
    {
        await using var dbContext = Db(factory, workspaceId);
        var device = await dbContext.Set<BambuDevice>().SingleAsync(candidate => candidate.Id == deviceId, Token);
        device.Online = online;
        device.LastSeenAt = lastSeen;
        await dbContext.SaveChangesAsync(Token);
    }

    [Fact]
    public async Task ReadStateAndDeleteWorkPerUserAndWorkspace()
    {
        var mine = await SignInAsync();
        var other = await SignInAsync();
        var printer = await ScheduleTests.CreatePrinterAsync(mine.Client, "A1");
        await FinishedJobAsync(mine.Client, printer.Id, "Failed");
        await FinishedJobAsync(mine.Client, printer.Id, "Done");
        await SweepAsync(mine);
        var items = (await ListAsync(mine)).Items;
        Assert.Equal(2, items.Count);

        Assert.Empty((await ListAsync(other)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/notifications/{items[0].Id}/read", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync($"/api/notifications/{items[0].Id}", Token)).StatusCode);

        var read = await ScheduleTests.SendAsync<NotificationResponse>(mine.Client, HttpMethod.Post, $"/api/notifications/{items[0].Id}/read");
        Assert.True(read.IsRead);
        var count = await ScheduleTests.SendAsync<JsonElement>(mine.Client, HttpMethod.Get, "/api/notifications/unread-count");
        Assert.Equal(1, count.GetProperty("count").GetInt32());
        Assert.Single((await ListAsync(mine, "?unreadOnly=true")).Items);

        await mine.Client.PostAsync("/api/notifications/read-all", null, Token);
        Assert.Equal(0, (await ListAsync(mine)).UnreadCount);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.Client.DeleteAsync($"/api/notifications/{items[1].Id}", Token)).StatusCode);
        Assert.Single((await ListAsync(mine)).Items);

        var page = await ListAsync(mine, "?limit=1");
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Preferences_DefaultToInAppAndCanSilenceATypeAndValidate()
    {
        var session = await SignInAsync();
        var defaults = await ScheduleTests.SendAsync<NotificationPreferencesResponse>(session.Client, HttpMethod.Get, "/api/notifications/preferences");
        Assert.Equal(NotificationTypes.All.Length, defaults.Types.Count);
        Assert.All(defaults.Types, preference => Assert.True(preference.InApp && !preference.Email));
        Assert.False(defaults.Channels.EmailAvailable);
        Assert.False(defaults.Channels.Push);

        var saved = await ScheduleTests.SendAsync<NotificationPreferencesResponse>(
            session.Client,
            HttpMethod.Put,
            "/api/notifications/preferences",
            new NotificationPreferencesRequest([new NotificationTypePreference(NotificationTypes.PrintFinished, false, false)], new QuietHoursSettings(true, "21:30", "06:15", 60)));
        Assert.False(saved.Types.Single(preference => preference.Type == NotificationTypes.PrintFinished).InApp);
        Assert.Equal(new QuietHoursSettings(true, "21:30", "06:15", 60), saved.QuietHours);

        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        await FinishedJobAsync(session.Client, printer.Id, "Done");
        await FinishedJobAsync(session.Client, printer.Id, "Failed");
        await SweepAsync(session);
        Assert.Equal([NotificationTypes.PrintFailed], (await ListAsync(session)).Items.Select(item => item.Type).ToList());

        var bad = await session.Client.PutAsJsonAsync(
            "/api/notifications/preferences",
            new NotificationPreferencesRequest([new NotificationTypePreference("Bogus", true, true), new NotificationTypePreference(NotificationTypes.LowStock, true, true), new NotificationTypePreference(NotificationTypes.LowStock, true, true)], new QuietHoursSettings(true, "25:00", "x", 9999)),
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var text = await bad.Content.ReadAsStringAsync(Token);
        Assert.Contains("types[0].type", text);
        Assert.Contains("types[2].type", text);
        Assert.Contains("quietHours.start", text);
        Assert.Contains("quietHours.end", text);
        Assert.Contains("quietHours.utcOffsetMinutes", text);
    }

    [Fact]
    public async Task RateLimit_DefersTheRestInsteadOfFlooding()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:RateLimitPerHour"] = "2" })));
        var session = await SignInAsync(limited);
        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        for (var index = 0; index < 4; index++)
        {
            await FinishedJobAsync(session.Client, printer.Id, "Failed");
        }

        await SweepAsync(limited, session);
        var first = await ScheduleTests.SendAsync<NotificationListResponse>(session.Client, HttpMethod.Get, "/api/notifications");
        Assert.Equal(2, first.Items.Count);

        await SweepAsync(limited, session, DateTimeOffset.UtcNow.AddHours(2));
        var later = await ScheduleTests.SendAsync<NotificationListResponse>(session.Client, HttpMethod.Get, "/api/notifications");
        Assert.Equal(4, later.Items.Count);
    }

    [Fact]
    public async Task Email_IsSentThroughTheSenderOnlyWhenEnabledPreferredAndOutsideQuietHours()
    {
        var sender = new RecordingEmailSender();
        await using var mailing = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:Smtp:Enabled"] = "true", ["Notifications:Smtp:From"] = "printos@example.com" }));
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IEmailSender>(sender)));
        });
        var session = await SignInAsync(mailing);
        await ScheduleTests.SendAsync<NotificationPreferencesResponse>(
            session.Client,
            HttpMethod.Put,
            "/api/notifications/preferences",
            new NotificationPreferencesRequest([new NotificationTypePreference(NotificationTypes.PrintFailed, true, true)], new QuietHoursSettings(true, "00:00", "23:59", 0)));
        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        await FinishedJobAsync(session.Client, printer.Id, "Failed");
        await FinishedJobAsync(session.Client, printer.Id, "Done");

        var noon = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(12), TimeSpan.Zero);
        await SweepAsync(mailing, session, noon);
        Assert.Empty(sender.Sent);

        await ScheduleTests.SendAsync<NotificationPreferencesResponse>(
            session.Client,
            HttpMethod.Put,
            "/api/notifications/preferences",
            new NotificationPreferencesRequest(null, new QuietHoursSettings(false, "00:00", "23:59", 0)));
        sender.Fail = true;
        await SweepAsync(mailing, session, noon);
        Assert.Empty(sender.Sent);

        sender.Fail = false;
        await SweepAsync(mailing, session, noon);
        var mail = Assert.Single(sender.Sent);
        Assert.Equal(session.Email, mail.To);
        Assert.StartsWith("PrintOS: Print mislukt", mail.Subject);

        await SweepAsync(mailing, session, noon);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Email_IsNeverSentWhenSmtpIsDisabled()
    {
        var sender = new RecordingEmailSender();
        await using var disabled = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IEmailSender>(sender))));
        var session = await SignInAsync(disabled);
        await ScheduleTests.SendAsync<NotificationPreferencesResponse>(
            session.Client,
            HttpMethod.Put,
            "/api/notifications/preferences",
            new NotificationPreferencesRequest([new NotificationTypePreference(NotificationTypes.PrintFailed, true, true)], null));
        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        await FinishedJobAsync(session.Client, printer.Id, "Failed");

        await SweepAsync(disabled, session);

        Assert.Empty(sender.Sent);
        Assert.Single((await ScheduleTests.SendAsync<NotificationListResponse>(session.Client, HttpMethod.Get, "/api/notifications")).Items);
    }

    [Fact]
    public async Task SignalR_PushesAChangedSignalToTheOwnerOnly()
    {
        var mine = await SignInAsync();
        var other = await SignInAsync();
        var printer = await ScheduleTests.CreatePrinterAsync(mine.Client, "A1");
        await FinishedJobAsync(mine.Client, printer.Id, "Failed");

        HubConnection Connect(Session session) => new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationsHub.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(session.AccessToken);
            })
            .Build();

        await using var ownerConnection = Connect(mine);
        await using var strangerConnection = Connect(other);
        var ownerSignal = new TaskCompletionSource();
        var strangerSignal = new TaskCompletionSource();
        ownerConnection.On("notificationsChanged", () => ownerSignal.TrySetResult());
        strangerConnection.On("notificationsChanged", () => strangerSignal.TrySetResult());
        await ownerConnection.StartAsync(Token);
        await strangerConnection.StartAsync(Token);

        await SweepAsync(mine);

        await ownerSignal.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.False(strangerSignal.Task.IsCompleted);
    }

    [Fact]
    public async Task SignalR_RejectsAnonymousConnections()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationsHub.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync(Token));
    }

    [Fact]
    public async Task Sweeps_SkipAWorkspaceWhileAnotherReplicaHoldsItsLock()
    {
        var session = await SignInAsync();
        var printer = await ScheduleTests.CreatePrinterAsync(session.Client, "A1");
        await FinishedJobAsync(session.Client, printer.Id, "Failed");
        var connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;

        await using (var holder = new Npgsql.NpgsqlConnection(connectionString))
        {
            await holder.OpenAsync(Token);
            await using var transaction = await holder.BeginTransactionAsync(Token);
            await using (var command = new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", holder, transaction))
            {
                command.Parameters.AddWithValue("key", NotificationWorker.AdvisoryKey(session.WorkspaceId));
                await command.ExecuteNonQueryAsync(Token);
            }

            Assert.False(await factory.Services.GetRequiredService<NotificationWorker>().SweepWorkspaceAsync(session.WorkspaceId, DateTimeOffset.UtcNow, Token));
        }

        await SweepAsync(session);
        Assert.Single((await ListAsync(session)).Items);
    }
}
