using PrintOs.Api.Modules.Bambu;

namespace PrintOs.Api.Tests;

public class BambuUtilizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_ReturnsThePrintingShareOfTheObservedTime()
    {
        var changes = new List<BambuStateInterval>
        {
            new("idle", Now.AddHours(-24)),
            new("printing", Now.AddHours(-18)),
            new("finished", Now.AddHours(-12)),
        };

        var window = BambuUtilization.Compute(changes, Now, 24, Now);

        Assert.Equal(25, window.PrintingPercent);
        Assert.Equal(360, window.PrintingMinutes);
        Assert.Equal(1440, window.ObservedMinutes);
        Assert.Equal(100, window.CoveragePercent);
    }

    [Fact]
    public void Compute_ExcludesTimeBeforeTheFirstRecordFromTheShareButReportsCoverage()
    {
        var changes = new List<BambuStateInterval> { new("printing", Now.AddHours(-6)), new("idle", Now.AddHours(-3)) };

        var window = BambuUtilization.Compute(changes, Now, 24, Now);

        Assert.Equal(50, window.PrintingPercent);
        Assert.Equal(25, window.CoveragePercent);
    }

    [Fact]
    public void Compute_ClipsAnIntervalThatStartedBeforeTheWindow()
    {
        var changes = new List<BambuStateInterval> { new("printing", Now.AddDays(-5)), new("idle", Now.AddHours(-20)) };

        var window = BambuUtilization.Compute(changes, Now, 24, Now);

        Assert.Equal(4 * 60, window.PrintingMinutes);
        Assert.Equal(100, window.CoveragePercent);
    }

    [Fact]
    public void Compute_DoesNotTrustAnOpenPrintingStateWhenTheDeviceWentSilent()
    {
        var changes = new List<BambuStateInterval> { new("idle", Now.AddHours(-10)), new("printing", Now.AddHours(-5)) };

        var window = BambuUtilization.Compute(changes, Now, 24, Now.AddHours(-4).AddMinutes(-50));

        Assert.Equal(20, window.PrintingMinutes);
        Assert.Equal(300 + 20, window.ObservedMinutes);
    }

    [Fact]
    public void Compute_CountsOfflineTimeAsObservedAndNotPrinting()
    {
        var changes = new List<BambuStateInterval> { new("printing", Now.AddHours(-4)), new("offline", Now.AddHours(-2)) };

        var window = BambuUtilization.Compute(changes, Now, 24, Now.AddHours(-2));

        Assert.Equal(50, window.PrintingPercent);
        Assert.Equal(120, window.OfflineMinutes);
    }

    [Fact]
    public void Compute_HasNoPercentageWithoutAnyRecord()
    {
        var window = BambuUtilization.Compute([], Now, 24, null);

        Assert.Null(window.PrintingPercent);
        Assert.Equal(0, window.CoveragePercent);
    }
}
