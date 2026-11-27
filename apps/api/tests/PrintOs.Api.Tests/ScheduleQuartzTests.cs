using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class ScheduleQuartzTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;


    [Fact]
    public async Task Quartz_ReplansOnItsOwnWhenJobsChange()
    {
        await using var scheduled = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Scheduling:Enabled"] = "true" })));
        using var client = scheduled.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Quartz"), Token);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), Token);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());

        var (busy, free, automatic) = await ScheduleTests.BusyAndFreeAsync(client);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        Guid printerId;
        do
        {
            await Task.Delay(250, Token);
            printerId = (await ScheduleTests.SendAsync<PrintJobResponse>(client, HttpMethod.Get, $"/api/print-jobs/{automatic.Id}")).PrinterId;
        }
        while (printerId != free.Id && DateTimeOffset.UtcNow < deadline);

        Assert.Equal(free.Id, printerId);
    }
}
