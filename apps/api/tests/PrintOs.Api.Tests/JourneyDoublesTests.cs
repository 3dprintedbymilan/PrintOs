using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PrintOs.Api.Modules.Bambu;

namespace PrintOs.Api.Tests;

public sealed class JourneyDoublesTests
{
    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "PrintOs.Api";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void JourneyBambuClientRefusesToRunInProduction()
    {
        Assert.Throws<InvalidOperationException>(() => new JourneyBambuCloudClient(new StubEnvironment(Environments.Production)));
    }

    [Fact]
    public async Task JourneyBambuClientAnswersWithOneFixedDeviceOutsideProduction()
    {
        var client = new JourneyBambuCloudClient(new StubEnvironment(Environments.Development));

        var account = await client.GetAccountAsync("global", "any-token", CancellationToken.None);
        var devices = await client.ListDevicesAsync("global", "any-token", CancellationToken.None);
        var tasks = await client.ListTasksAsync("global", "any-token", 10, CancellationToken.None);

        Assert.Equal(JourneyBambuCloudClient.AccountUid, account.Uid);
        Assert.Equal("JOURNEY-A1-0001", Assert.Single(devices).Serial);
        Assert.Empty(tasks);
    }
}
