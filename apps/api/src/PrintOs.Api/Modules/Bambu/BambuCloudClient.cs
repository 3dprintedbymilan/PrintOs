using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PrintOs.Api.Modules.Bambu;

public sealed class BambuCloudClient(HttpClient httpClient, IOptions<BambuOptions> options) : IBambuCloudClient
{
    public async Task<BambuAccount> GetAccountAsync(string region, string accessToken, CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync(region, accessToken, "/v1/design-user-service/my/preference", cancellationToken);
        var uid = ReadString(root, "uid");
        if (string.IsNullOrWhiteSpace(uid))
        {
            throw new BambuCloudException("The Bambu Lab cloud did not return an account id.");
        }

        return new BambuAccount(uid, ReadString(root, "name"));
    }

    public async Task<IReadOnlyList<BambuBoundDevice>> ListDevicesAsync(string region, string accessToken, CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync(region, accessToken, "/v1/iot-service/api/user/bind", cancellationToken);
        var devices = new List<BambuBoundDevice>();
        if (root.TryGetProperty("devices", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in array.EnumerateArray())
            {
                var serial = ReadString(element, "dev_id");
                if (string.IsNullOrWhiteSpace(serial))
                {
                    continue;
                }

                var model = ReadString(element, "dev_product_name") ?? ReadString(element, "dev_model_name") ?? string.Empty;
                devices.Add(new BambuBoundDevice(serial, ReadString(element, "name") ?? serial, model, element.TryGetProperty("online", out var online) && online.ValueKind == JsonValueKind.True));
            }
        }

        return devices;
    }

    public async Task<IReadOnlyList<BambuTask>> ListTasksAsync(string region, string accessToken, int limit, CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync(region, accessToken, $"/v1/user-service/my/tasks?limit={Math.Clamp(limit, 1, 100)}", cancellationToken);
        var tasks = new List<BambuTask>();
        if (!root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return tasks;
        }

        foreach (var hit in hits.EnumerateArray())
        {
            var id = ReadString(hit, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var filaments = new List<BambuTaskFilament>();
            if (hit.TryGetProperty("amsDetailMapping", out var mapping) && mapping.ValueKind == JsonValueKind.Array)
            {
                filaments.AddRange(mapping.EnumerateArray().Select(entry => new BambuTaskFilament(
                    ReadString(entry, "filamentType"),
                    ReadString(entry, "sourceColor") ?? ReadString(entry, "targetColor"),
                    ReadDecimal(entry, "weight"))));
            }

            tasks.Add(new BambuTask(
                id,
                ReadString(hit, "title") ?? string.Empty,
                ReadString(hit, "deviceId") ?? string.Empty,
                (int)(ReadDecimal(hit, "status") ?? -1),
                ReadTime(hit, "startTime"),
                ReadTime(hit, "endTime"),
                ReadDecimal(hit, "weight"),
                filaments));
        }

        return tasks;
    }

    private async Task<JsonElement> GetJsonAsync(string region, string accessToken, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(options.Value.ApiBaseUrl(region)), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BambuCloudException("The Bambu Lab cloud could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BambuCloudException("The Bambu Lab cloud did not answer in time.", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new BambuTokenRejectedException();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new BambuCloudException($"The Bambu Lab cloud answered with status {(int)response.StatusCode}.");
            }

            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new BambuCloudException("The Bambu Lab cloud returned an unreadable answer.", exception);
            }
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
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
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static DateTimeOffset? ReadTime(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : null;
    }
}
