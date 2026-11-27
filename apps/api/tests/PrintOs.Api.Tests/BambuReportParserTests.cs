using PrintOs.Api.Modules.Bambu;

namespace PrintOs.Api.Tests;

public class BambuReportParserTests
{
    internal const string PrintingReport = """
        {
          "print": {
            "command": "push_status",
            "msg": 0,
            "sequence_id": "1500",
            "gcode_state": "RUNNING",
            "gcode_file": "/data/Metadata/plate_1.gcode",
            "subtask_name": "Benchy",
            "task_id": "7001",
            "mc_percent": 42,
            "mc_remaining_time": 83,
            "layer_num": 120,
            "total_layer_num": 340,
            "nozzle_temper": 219.6,
            "nozzle_target_temper": 220,
            "bed_temper": 60.2,
            "bed_target_temper": 60,
            "print_error": 0,
            "tray_now": "1",
            "ams": {
              "tray_now": "1",
              "ams": [
                {
                  "id": "0",
                  "humidity": "4",
                  "tray": [
                    { "id": "0", "tray_type": "PLA", "tray_color": "F4F4F0FF" },
                    { "id": "1", "tray_type": "PETG", "tray_color": "1A1A1AFF" }
                  ]
                }
              ]
            },
            "vt_tray": { "id": "254", "tray_type": "", "tray_color": "00000000" }
          }
        }
        """;

    private static BambuDevice NewDevice() => new() { Serial = "TEST-SERIAL-1", Name = "A1", Model = "A1" };

    [Fact]
    public void Parse_ReadsAnA1PushStatusReport()
    {
        var report = BambuReportParser.Parse(PrintingReport);

        Assert.NotNull(report);
        Assert.Equal("RUNNING", report.GcodeState);
        Assert.Equal(42, report.ProgressPercent);
        Assert.Equal(83, report.RemainingMinutes);
        Assert.Equal(120, report.Layer);
        Assert.Equal(340, report.TotalLayers);
        Assert.Equal(219.6m, report.NozzleTemperature);
        Assert.Equal(60.2m, report.BedTemperature);
        Assert.Equal("Benchy", report.JobName);
        Assert.Equal("1", report.TrayNow);
        Assert.Contains(report.Trays!, tray => tray.Id == "1" && tray.Type == "PETG" && tray.Color == "#1A1A1A");
    }

    [Fact]
    public void Apply_FillsTheDeviceFromAFullReport()
    {
        var device = NewDevice();

        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse(PrintingReport)!, DateTimeOffset.UtcNow);

        Assert.Equal(BambuPrinterStates.Printing, device.State);
        Assert.True(device.Online);
        Assert.Equal("Benchy", device.JobName);
        Assert.Equal(42, device.ProgressPercent);
        Assert.Equal("PETG", device.FilamentType);
        Assert.Equal("#1A1A1A", device.FilamentColor);
        Assert.NotNull(device.LastSeenAt);
    }

    [Fact]
    public void Apply_KeepsKnownValuesWhenAPartialUpdateArrives()
    {
        var device = NewDevice();
        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse(PrintingReport)!, DateTimeOffset.UtcNow);

        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse("""{ "print": { "mc_percent": 43, "nozzle_temper": 220.0 } }""")!, DateTimeOffset.UtcNow);

        Assert.Equal(BambuPrinterStates.Printing, device.State);
        Assert.Equal(43, device.ProgressPercent);
        Assert.Equal(83, device.RemainingMinutes);
        Assert.Equal("Benchy", device.JobName);
        Assert.Equal("PETG", device.FilamentType);
    }

    [Theory]
    [InlineData("IDLE", "idle")]
    [InlineData("PREPARE", "printing")]
    [InlineData("RUNNING", "printing")]
    [InlineData("PAUSE", "paused")]
    [InlineData("FINISH", "finished")]
    [InlineData("FAILED", "failed")]
    public void MapState_CoversTheKnownGcodeStates(string gcodeState, string expected)
    {
        Assert.Equal(expected, BambuReportParser.MapState(gcodeState));
    }

    [Fact]
    public void Apply_ClearsTheJobWhenThePrinterGoesIdle()
    {
        var device = NewDevice();
        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse(PrintingReport)!, DateTimeOffset.UtcNow);

        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse("""{ "print": { "gcode_state": "IDLE", "subtask_name": "" } }""")!, DateTimeOffset.UtcNow);

        Assert.Equal(BambuPrinterStates.Idle, device.State);
        Assert.Null(device.JobName);
        Assert.Null(device.ProgressPercent);
        Assert.Null(device.Layer);
    }

    [Fact]
    public void Apply_KeepsTheFinishedJobVisible()
    {
        var device = NewDevice();
        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse(PrintingReport)!, DateTimeOffset.UtcNow);

        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse("""{ "print": { "gcode_state": "FINISH", "mc_percent": 100, "mc_remaining_time": 0 } }""")!, DateTimeOffset.UtcNow);

        Assert.Equal(BambuPrinterStates.Finished, device.State);
        Assert.Equal("Benchy", device.JobName);
        Assert.Equal(100, device.ProgressPercent);
        Assert.Equal(0, device.RemainingMinutes);
    }

    [Fact]
    public void MarkOffline_SetsTheOfflineState()
    {
        var device = NewDevice();
        BambuDeviceStateUpdater.Apply(device, BambuReportParser.Parse(PrintingReport)!, DateTimeOffset.UtcNow);

        BambuDeviceStateUpdater.MarkOffline(device, DateTimeOffset.UtcNow);

        Assert.Equal(BambuPrinterStates.Offline, device.State);
        Assert.False(device.Online);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "info": { "command": "get_version" } }""")]
    public void Parse_IgnoresMessagesWithoutPrintStatus(string payload)
    {
        Assert.Null(BambuReportParser.Parse(payload));
    }

    [Theory]
    [InlineData("F4F4F0FF", "#F4F4F0")]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("zzzzzz", null)]
    [InlineData("", null)]
    public void NormalizeColor_AcceptsBambuHexColours(string input, string? expected)
    {
        Assert.Equal(expected, BambuReportParser.NormalizeColor(input));
    }
}
