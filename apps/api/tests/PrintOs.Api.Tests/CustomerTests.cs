using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Sales;

namespace PrintOs.Api.Tests;

public class CustomerTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static readonly CustomerRequest ValidCustomer = new("Jan Peeters", "jan@example.com", "+32 470 12 34 56", "Kerkstraat 1\n9000 Gent", "BE0123456789", "Wil blauw");

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Customers"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<CustomerResponse> CreateAsync(HttpClient client, CustomerRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/customers", request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Customers_RequireAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_TrimsValuesAndReturnsTheCustomer()
    {
        using var client = await SignedInClientAsync();

        var customer = await CreateAsync(client, ValidCustomer with { Name = "  Jan Peeters  ", Phone = "  ", VatNumber = null });

        Assert.Equal("Jan Peeters", customer.Name);
        Assert.Null(customer.Phone);
        Assert.Null(customer.VatNumber);
        Assert.Equal("jan@example.com", customer.Email);
        Assert.False(customer.IsArchived);
    }

    [Fact]
    public async Task Update_ChangesValues()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client, ValidCustomer);

        var response = await client.PutAsJsonAsync($"/api/customers/{created.Id}", ValidCustomer with { Name = "Jan P.", Notes = null }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await client.GetFromJsonAsync<CustomerResponse>($"/api/customers/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal("Jan P.", updated!.Name);
        Assert.Null(updated.Notes);
    }

    [Fact]
    public async Task Archive_HidesFromDefaultListAndRestoreBringsItBack()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await CreateAsync(client, ValidCustomer);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/customers/{created.Id}/archive", null, cancellationToken)).StatusCode);

        Assert.Empty((await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers", cancellationToken))!);
        Assert.True(Assert.Single((await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers?includeArchived=true", cancellationToken))!).IsArchived);

        await client.PostAsync($"/api/customers/{created.Id}/restore", null, cancellationToken);
        Assert.Single((await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers", cancellationToken))!);
    }

    [Fact]
    public async Task Search_MatchesNameEmailAndPhone_WithoutTreatingWildcardsAsPatterns()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateAsync(client, ValidCustomer);
        await CreateAsync(client, new CustomerRequest("Fatima", "fatima@shop.be", "0499 99 99 99", null, null, null));

        Assert.Equal("Fatima", Assert.Single((await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers?search=shop.be", cancellationToken))!).Name);
        Assert.Equal("Fatima", Assert.Single((await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers?search=0499", cancellationToken))!).Name);
        Assert.Equal("Jan Peeters", Assert.Single((await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers?search=peet", cancellationToken))!).Name);
        Assert.Empty((await client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers?search=%25", cancellationToken))!);
    }

    [Fact]
    public async Task Delete_RemovesTheCustomer()
    {
        using var client = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await CreateAsync(client, ValidCustomer);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/customers/{created.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/{created.Id}", cancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("", "a@b.be", "name")]
    [InlineData("Jan", "not-an-email", "email")]
    [InlineData("Jan", "a b@c.be", "email")]
    public async Task Create_RejectsInvalidValues(string name, string email, string failingField)
    {
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/customers", new CustomerRequest(name, email, null, null, null, null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(failingField, out _));
    }

    [Fact]
    public async Task Create_RejectsTooLongNotes()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/customers", ValidCustomer with { Notes = new string('x', CustomerRules.NotesMaxLength + 1) }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customers_AreIsolatedPerWorkspace()
    {
        using var owner = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await CreateAsync(owner, ValidCustomer);

        Assert.Empty((await other.GetFromJsonAsync<List<CustomerResponse>>("/api/customers?includeArchived=true", cancellationToken))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/customers/{created.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/customers/{created.Id}", ValidCustomer, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/customers/{created.Id}/archive", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/customers/{created.Id}", cancellationToken)).StatusCode);
    }
}
