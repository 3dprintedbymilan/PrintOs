using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.ModelLibrary;

namespace PrintOs.Api.Tests;

public class ModelLibraryTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string StrongPassword = "Correct-Horse-9";

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static async Task<HttpClient> SignedInClientAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var cancellationToken = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/auth/sign-up", new SignUpRequest(email, StrongPassword, "Models"), cancellationToken);
        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(email, StrongPassword), cancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private Task<HttpClient> SignedInClientAsync() => SignedInClientAsync(factory);

    private static byte[] BinaryStl(int triangles)
    {
        var bytes = new byte[84 + (50 * triangles)];
        BitConverter.GetBytes((uint)triangles).CopyTo(bytes, 80);
        return bytes;
    }

    private static MultipartFormDataContent UploadForm(byte[] file, string fileName, Dictionary<string, string>? fields = null, byte[]? thumbnail = null)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(file), "file", fileName } };
        if (thumbnail is not null)
        {
            form.Add(new ByteArrayContent(thumbnail), "thumbnail", "thumb.png");
        }

        foreach (var (key, value) in fields ?? [])
        {
            form.Add(new StringContent(value), key);
        }

        return form;
    }

    private static async Task<ModelDetailResponse> CreateAsync(HttpClient client, string name = "Benchy", string tags = "boat, Test", byte[]? thumbnail = null)
    {
        using var form = UploadForm(BinaryStl(3), "benchy.stl", new() { ["name"] = name, ["tags"] = tags, ["infillPercent"] = "15", ["layerHeightMillimetres"] = "0.2" }, thumbnail);
        var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModelDetailResponse>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Models_RequireAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/models", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_StoresFileAndReturnsNormalizedMetadata()
    {
        using var client = await SignedInClientAsync();

        var model = await CreateAsync(client, "  Benchy  ", "Boat, boat ,Test");

        Assert.Equal("Benchy", model.Name);
        Assert.Equal(["boat", "test"], model.Tags);
        var version = Assert.Single(model.Versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("stl", version.Format);
        Assert.Equal(15, version.PrintProfile.InfillPercent);
        Assert.Equal(0.2m, version.PrintProfile.LayerHeightMillimetres);
        Assert.Contains(factory.Services.GetRequiredService<InMemoryFileStorage>().Keys, key => key.Contains(model.Id.ToString("N")));
    }

    [Fact]
    public async Task Create_DefaultsNameToFileName()
    {
        using var client = await SignedInClientAsync();
        using var form = UploadForm(BinaryStl(1), "gear-box.stl");

        var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);
        var model = await response.Content.ReadFromJsonAsync<ModelDetailResponse>(TestContext.Current.CancellationToken);

        Assert.Equal("gear-box", model!.Name);
    }

    [Theory]
    [InlineData("notes.txt", "hello")]
    [InlineData("fake.stl", "this is not an stl")]
    [InlineData("fake.3mf", "not a zip")]
    public async Task Create_RejectsWrongTypeOrContent(string fileName, string content)
    {
        using var client = await SignedInClientAsync();
        using var form = UploadForm(Encoding.ASCII.GetBytes(content), fileName);

        var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty("file", out _));
    }

    [Fact]
    public async Task Create_AcceptsAsciiStlAndObj()
    {
        using var client = await SignedInClientAsync();

        foreach (var (name, content) in new[] { ("a.stl", "solid cube\nendsolid cube\n"), ("b.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n") })
        {
            using var form = UploadForm(Encoding.ASCII.GetBytes(content), name);
            var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    [Fact]
    public async Task Create_RejectsFilesOverTheConfiguredLimit()
    {
        using var small = factory.WithWebHostBuilder(builder => builder.UseSetting("ModelLibrary:MaxModelFileBytes", "1000"));
        using var client = await SignedInClientAsync(small);
        using var form = UploadForm(BinaryStl(100), "big.stl");

        var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsInvalidThumbnailAndMetadata()
    {
        using var client = await SignedInClientAsync();
        using var form = UploadForm(BinaryStl(1), "a.stl", new() { ["sourceUrl"] = "javascript:alert(1)", ["infillPercent"] = "150" }, thumbnail: [1, 2, 3, 4]);

        var response = await client.PostAsync("/api/models", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("thumbnail", out _));
        Assert.True(errors.TryGetProperty("sourceUrl", out _));
        Assert.True(errors.TryGetProperty("infillPercent", out _));
    }

    [Fact]
    public async Task List_SearchesByTextAndFiltersByTag()
    {
        using var client = await SignedInClientAsync();
        await CreateAsync(client, "Dragon", "toy,dragon");
        await CreateAsync(client, "Phone stand", "desk");

        var byText = await client.GetFromJsonAsync<List<ModelSummaryResponse>>("/api/models?search=drag", TestContext.Current.CancellationToken);
        var byTag = await client.GetFromJsonAsync<List<ModelSummaryResponse>>("/api/models?tag=DESK", TestContext.Current.CancellationToken);
        var tags = await client.GetFromJsonAsync<List<string>>("/api/models/tags", TestContext.Current.CancellationToken);

        Assert.Equal("Dragon", Assert.Single(byText!).Name);
        Assert.Equal("Phone stand", Assert.Single(byTag!).Name);
        Assert.Equal(["desk", "dragon", "toy"], tags);
    }

    [Fact]
    public async Task Update_ChangesMetadata()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client);

        var response = await client.PutAsJsonAsync($"/api/models/{created.Id}", new ModelUpdateRequest("Renamed", "Nice", ["Calm", "calm", "x"], "https://example.com/m/1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await client.GetFromJsonAsync<ModelDetailResponse>($"/api/models/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(["calm", "x"], updated.Tags);
        Assert.Equal("https://example.com/m/1", updated.SourceUrl);
    }

    [Fact]
    public async Task Update_RejectsBlankName()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client);

        var response = await client.PutAsJsonAsync($"/api/models/{created.Id}", new ModelUpdateRequest(" ", null, null, null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddVersion_IncrementsNumberAndUpdatesProfile()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client);
        using var form = UploadForm(BinaryStl(5), "benchy-v2.stl", new() { ["notes"] = "Thicker hull" });

        var response = await client.PostAsync($"/api/models/{created.Id}/versions", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var model = await response.Content.ReadFromJsonAsync<ModelDetailResponse>(TestContext.Current.CancellationToken);
        Assert.Equal([2, 1], model!.Versions.Select(version => version.VersionNumber));

        var profile = await client.PutAsJsonAsync(
            $"/api/models/{created.Id}/versions/{model.Versions[0].Id}",
            new VersionUpdateRequest("Thicker hull", 0.16m, 20, true, "PETG", null), TestContext.Current.CancellationToken);
        var updated = await profile.Content.ReadFromJsonAsync<ModelVersionResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("PETG", updated!.PrintProfile.MaterialType);
        Assert.True(updated.PrintProfile.SupportsRequired);
    }

    [Fact]
    public async Task SignedDownloadUrl_ServesTheFileWithoutAuthorization()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client);

        var link = await client.GetFromJsonAsync<DownloadUrlResponse>($"/api/models/{created.Id}/versions/{created.Versions[0].Id}/download-url", TestContext.Current.CancellationToken);
        using var anonymous = factory.CreateClient();
        var download = await anonymous.GetAsync(link!.Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(84 + 150, (await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length);
        Assert.Equal("benchy.stl", download.Content.Headers.ContentDisposition?.FileName);
        Assert.True(link.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SignedDownloadUrl_RejectsTamperedTokens()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/model-files/not-a-real-token", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Thumbnail_IsServedThroughSignedUrlAndCanBeReplaced()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client, thumbnail: PngBytes);
        Assert.NotNull(created.ThumbnailUrl);

        using var anonymous = factory.CreateClient();
        var image = await anonymous.GetAsync(created.ThumbnailUrl, TestContext.Current.CancellationToken);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);

        var storage = factory.Services.GetRequiredService<InMemoryFileStorage>();
        var before = storage.Keys.Count(key => key.Contains("thumb-") && key.Contains(created.Id.ToString("N")));
        using var replacement = new MultipartFormDataContent { { new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2]), "thumbnail", "t.jpg" } };
        var response = await client.PutAsync($"/api/models/{created.Id}/thumbnail", replacement, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, before);
        Assert.Equal(1, storage.Keys.Count(key => key.Contains("thumb-") && key.Contains(created.Id.ToString("N"))));
    }

    [Fact]
    public async Task Delete_RemovesModelAndItsBlobs()
    {
        using var client = await SignedInClientAsync();
        var created = await CreateAsync(client, thumbnail: PngBytes);
        using var form = UploadForm(BinaryStl(2), "v2.stl");
        await client.PostAsync($"/api/models/{created.Id}/versions", form, TestContext.Current.CancellationToken);
        var storage = factory.Services.GetRequiredService<InMemoryFileStorage>();
        Assert.Equal(3, storage.Keys.Count(key => key.Contains(created.Id.ToString("N"))));

        var response = await client.DeleteAsync($"/api/models/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(storage.Keys, key => key.Contains(created.Id.ToString("N")));
        var after = await client.GetAsync($"/api/models/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Models_AreIsolatedBetweenWorkspaces()
    {
        using var owner = await SignedInClientAsync();
        using var stranger = await SignedInClientAsync();
        var created = await CreateAsync(owner);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Empty((await stranger.GetFromJsonAsync<List<ModelSummaryResponse>>("/api/models", cancellationToken))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/models/{created.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/models/{created.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/models/{created.Id}/versions/{created.Versions[0].Id}/download-url", cancellationToken)).StatusCode);
        using var form = UploadForm(BinaryStl(1), "x.stl");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/models/{created.Id}/versions", form, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/models/{created.Id}", cancellationToken)).StatusCode);
    }
}
