using System.Globalization;
using System.Text.Json;

namespace PrintOs.Api.Modules.Bambu;

public sealed record BambuTray(string Id, string? Type, string? Color, int? Remain = null, int? WeightGrams = null);

public sealed record BambuReport(
    string? GcodeState,
    int? ProgressPercent,
    int? RemainingMinutes,
    int? Layer,
    int? TotalLayers,
    decimal? NozzleTemperature,
    decimal? BedTemperature,
    string? JobName,
    string? TrayNow,
    IReadOnlyList<BambuTray>? Trays,
    string? PrintErrorCode = null,
    IReadOnlyList<string>? HmsCodes = null,
    string? TaskId = null);

public static class BambuReportParser
{
    public static BambuReport? Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("print", out var print)
                || print.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var jobName = ReadString(print, "subtask_name");
            if (string.IsNullOrEmpty(jobName) && print.TryGetProperty("gcode_file", out _))
            {
                jobName = ReadString(print, "gcode_file");
            }

            return new BambuReport(
                ReadString(print, "gcode_state"),
                Clamp(ReadNumber(print, "mc_percent"), 0, 100),
                Clamp(ReadNumber(print, "mc_remaining_time"), 0, 525_600),
                Clamp(ReadNumber(print, "layer_num"), 0, 100_000),
                Clamp(ReadNumber(print, "total_layer_num"), 0, 100_000),
                ReadDecimal(print, "nozzle_temper"),
                ReadDecimal(print, "bed_temper"),
                jobName,
                ReadString(print, "tray_now") ?? ReadNestedString(print, "ams", "tray_now"),
                ReadTrays(print),
                ReadErrorCode(print),
                ReadHmsCodes(print),
                ReadString(print, "task_id"));
        }
    }

    public static string? MapState(string? gcodeState) => gcodeState?.ToUpperInvariant() switch
    {
        "IDLE" => BambuPrinterStates.Idle,
        "INIT" => BambuPrinterStates.Idle,
        "PREPARE" or "SLICING" or "RUNNING" => BambuPrinterStates.Printing,
        "PAUSE" => BambuPrinterStates.Paused,
        "FINISH" => BambuPrinterStates.Finished,
        "FAILED" => BambuPrinterStates.Failed,
        _ => null,
    };

    public static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var hex = color.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !hex.All(Uri.IsHexDigit))
        {
            return null;
        }

        return "#" + hex[..6].ToUpperInvariant();
    }

    private static string? ReadErrorCode(JsonElement print)
    {
        if (!print.TryGetProperty("print_error", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var code))
        {
            return null;
        }

        return code <= 0 || code > uint.MaxValue ? string.Empty : FormatCode((uint)code);
    }

    private static IReadOnlyList<string>? ReadHmsCodes(JsonElement print)
    {
        if (!print.TryGetProperty("hms", out var hms) || hms.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var codes = new List<string>();
        foreach (var entry in hms.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("attr", out var attribute) && attribute.TryGetUInt32(out var attributeValue)
                && entry.TryGetProperty("code", out var code) && code.TryGetUInt32(out var codeValue))
            {
                codes.Add($"{FormatCode(attributeValue)}-{FormatCode(codeValue)}");
            }
        }

        return codes.Take(10).ToList();
    }

    private static string FormatCode(uint value) => $"{value >> 16:X4}-{value & 0xFFFF:X4}";

    private static IReadOnlyList<BambuTray>? ReadTrays(JsonElement print)
    {
        var trays = new List<BambuTray>();
        var found = false;

        if (print.TryGetProperty("ams", out var ams) && ams.ValueKind == JsonValueKind.Object
            && ams.TryGetProperty("ams", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            found = true;
            foreach (var unit in units.EnumerateArray())
            {
                if (!int.TryParse(ReadString(unit, "id"), CultureInfo.InvariantCulture, out var unitId)
                    || !unit.TryGetProperty("tray", out var unitTrays) || unitTrays.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var tray in unitTrays.EnumerateArray())
                {
                    if (int.TryParse(ReadString(tray, "id"), CultureInfo.InvariantCulture, out var trayId))
                    {
                        trays.Add(new BambuTray((unitId * 4 + trayId).ToString(CultureInfo.InvariantCulture), ReadString(tray, "tray_type"), NormalizeColor(ReadString(tray, "tray_color")), ReadNumber(tray, "remain"), ReadNumber(tray, "tray_weight")));
                    }
                }
            }
        }

        if (print.TryGetProperty("vt_tray", out var external) && external.ValueKind == JsonValueKind.Object)
        {
            found = true;
            trays.Add(new BambuTray(ReadString(external, "id") ?? "254", ReadString(external, "tray_type"), NormalizeColor(ReadString(external, "tray_color")), ReadNumber(external, "remain"), ReadNumber(external, "tray_weight")));
        }

        return found ? trays : null;
    }

    private static string? ReadNestedString(JsonElement element, string parent, string name) =>
        element.TryGetProperty(parent, out var nested) && nested.ValueKind == JsonValueKind.Object ? ReadString(nested, name) : null;

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => Math.Round(number, 1),
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => Math.Round(parsed, 1),
            _ => null,
        };
    }

    private static int? ReadNumber(JsonElement element, string name) =>
        ReadDecimal(element, name) is { } value ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : null;

    private static int? Clamp(int? value, int minimum, int maximum) => value is { } number ? Math.Clamp(number, minimum, maximum) : null;
}

public static class BambuDeviceStateUpdater
{
    private const int MaximumStoredTrays = 20;

    public static void Apply(BambuDevice device, BambuReport report, DateTimeOffset now)
    {
        device.Online = true;
        device.LastSeenAt = now;
        device.UpdatedAt = now;

        var state = BambuReportParser.MapState(report.GcodeState);
        if (state is not null)
        {
            device.State = state;
        }
        else if (device.State == BambuPrinterStates.Offline)
        {
            device.State = BambuPrinterStates.Idle;
        }

        if (report.JobName is not null)
        {
            device.JobName = report.JobName.Length == 0 ? null : Truncate(report.JobName, 300);
        }

        device.ProgressPercent = report.ProgressPercent ?? device.ProgressPercent;
        device.RemainingMinutes = report.RemainingMinutes ?? device.RemainingMinutes;
        device.Layer = report.Layer ?? device.Layer;
        device.TotalLayers = report.TotalLayers ?? device.TotalLayers;
        device.NozzleTemperature = report.NozzleTemperature ?? device.NozzleTemperature;
        device.BedTemperature = report.BedTemperature ?? device.BedTemperature;

        if (device.State == BambuPrinterStates.Idle)
        {
            device.JobName = null;
            device.ProgressPercent = null;
            device.RemainingMinutes = null;
            device.Layer = null;
            device.TotalLayers = null;
        }

        if (report.Trays is not null)
        {
            var merged = ReadStoredTrays(device).ToDictionary(tray => tray.Id);
            foreach (var tray in report.Trays)
            {
                merged[tray.Id] = merged.TryGetValue(tray.Id, out var known)
                    ? tray with { Type = tray.Type ?? known.Type, Color = tray.Color ?? known.Color, Remain = tray.Remain ?? known.Remain, WeightGrams = tray.WeightGrams ?? known.WeightGrams }
                    : tray;
            }

            device.TraysJson = JsonSerializer.Serialize(merged.Values.Take(MaximumStoredTrays).ToList());
        }

        if (report.TrayNow is not null)
        {
            device.TrayNow = report.TrayNow;
        }

        if (report.PrintErrorCode is not null)
        {
            device.PrintErrorCode = report.PrintErrorCode.Length == 0 ? null : report.PrintErrorCode;
        }

        if (report.HmsCodes is not null)
        {
            device.HmsCodesJson = JsonSerializer.Serialize(report.HmsCodes);
        }

        var current = ReadStoredTrays(device).FirstOrDefault(tray => tray.Id == device.TrayNow);
        device.FilamentType = current?.Type is { Length: > 0 } type ? type : null;
        device.FilamentColor = current?.Color;
    }

    public static List<string> ReadStoredHmsCodes(BambuDevice device)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(device.HmsCodesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void MarkOffline(BambuDevice device, DateTimeOffset now)
    {
        device.Online = false;
        device.State = BambuPrinterStates.Offline;
        device.UpdatedAt = now;
    }

    public static List<BambuTray> ReadStoredTrays(BambuDevice device)
    {
        try
        {
            return JsonSerializer.Deserialize<List<BambuTray>>(device.TraysJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
