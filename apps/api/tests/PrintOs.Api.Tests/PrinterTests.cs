using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Production;

namespace PrintOs.Api.Tests;

public class PrinterTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static readonly PrinterRequest ValidPrinter = new("A1 #1", "Bambu Lab A1", 256, 256, 256, 0.4m, 95, 339m, 5000);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Printers"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<PrinterResponse> CreateAsync(HttpClient client, PrinterRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/printers", request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Printers_RequireAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/printers", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Defaults_DescribeTheBambuLabA1()
    {
        using var client = await SignedInClientAsync();

        var defaults = await client.GetFromJsonAsync<PrinterRequest>("/api/printers/defaults", TestContext.Current.CancellationToken);

        Assert.Equal("Bambu Lab A1", defaults!.Model);
        Assert.Equal(256, defaults.BuildVolumeXMillimetres);
        Assert.Equal(0.4m, defaults.NozzleDiameterMillimetres);
    }

    [Fact]
    public async Task Create_ReturnsPrinterWithDerivedDepreciation()
    {
        using var client = await SignedInClientAsync();

        var printer = await CreateAsync(client, ValidPrinter with { Name = "  Desk A1  ", PurchaseCost = 500m, LifetimeHours = 4000 });

        Assert.Equal("Desk A1", printer.Name);
        Assert.Equal(0.125m, printer.DepreciationPerHour);
        Assert.False(printer.IsArchived);
    }

    [Fact]
    public async Task Update_ChangesValuesAndRecalculatesDepreciation()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client, ValidPrinter);

        var response = await client.PutAsJsonAsync($"/api/printers/{created.Id}", ValidPrinter with { Name = "Renamed", PurchaseCost = 400m, LifetimeHours = 2000 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(0.2m, updated.DepreciationPerHour);
    }

    [Fact]
    public async Task Archive_HidesPrinterFromDefaultListAndRestoreBringsItBack()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await CreateAsync(client, ValidPrinter);

        var archive = await client.PostAsync($"/api/printers/{created.Id}/archive", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var active = await client.GetFromJsonAsync<List<PrinterResponse>>("/api/printers", cancellationToken);
        var all = await client.GetFromJsonAsync<List<PrinterResponse>>("/api/printers?includeArchived=true", cancellationToken);
        Assert.Empty(active!);
        Assert.True(Assert.Single(all!).IsArchived);

        await client.PostAsync($"/api/printers/{created.Id}/restore", null, cancellationToken);
        var restored = await client.GetFromJsonAsync<List<PrinterResponse>>("/api/printers", cancellationToken);
        Assert.Single(restored!);
    }

    [Theory]
    [InlineData("", "Bambu Lab A1", 256, 0.4, 95, 339, 5000, "name")]
    [InlineData("A1", " ", 256, 0.4, 95, 339, 5000, "model")]
    [InlineData("A1", "Bambu Lab A1", 0, 0.4, 95, 339, 5000, "buildVolumeXMillimetres")]
    [InlineData("A1", "Bambu Lab A1", 256, 5, 95, 339, 5000, "nozzleDiameterMillimetres")]
    [InlineData("A1", "Bambu Lab A1", 256, 0.4, 0, 339, 5000, "powerDrawWatts")]
    [InlineData("A1", "Bambu Lab A1", 256, 0.4, 95, -1, 5000, "purchaseCost")]
    [InlineData("A1", "Bambu Lab A1", 256, 0.4, 95, 339, 0, "lifetimeHours")]
    public async Task Create_RejectsInvalidValues(string name, string model, int volume, double nozzle, int power, double cost, int lifetime, string failingField)
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new PrinterRequest(name, model, volume, volume, volume, (decimal)nozzle, power, (decimal)cost, lifetime);

        var response = await client.PostAsJsonAsync("/api/printers", request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(failingField, out _));
    }

    [Fact]
    public async Task Printers_AreIsolatedBetweenWorkspaces()
    {
        using var firstClient = await SignedInClientAsync();
        using var secondClient = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await CreateAsync(firstClient, ValidPrinter);

        var list = await secondClient.GetFromJsonAsync<List<PrinterResponse>>("/api/printers", cancellationToken);
        var get = await secondClient.GetAsync($"/api/printers/{created.Id}", cancellationToken);
        var put = await secondClient.PutAsJsonAsync($"/api/printers/{created.Id}", ValidPrinter, cancellationToken);
        var archive = await secondClient.PostAsync($"/api/printers/{created.Id}/archive", null, cancellationToken);

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
    }
}
