using System.Text;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;

namespace PrintOs.Api.Modules.Bambu;

public sealed class BambuMqttTelemetrySource(IOptions<BambuOptions> options) : IBambuTelemetrySource
{
    private const string PushAllPayload = "{\"pushing\":{\"sequence_id\":\"0\",\"command\":\"pushall\",\"version\":1,\"push_target\":1}}";

    public async Task RunAsync(BambuTelemetryTarget target, Func<string, CancellationToken, Task> onReport, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var reportTopic = $"device/{target.Serial}/report";
        var requestTopic = $"device/{target.Serial}/request";

        using var client = new MqttClientFactory().CreateMqttClient();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.ApplicationMessageReceivedAsync += async message =>
        {
            if (message.ApplicationMessage.Topic == reportTopic)
            {
                await onReport(Encoding.UTF8.GetString(message.ApplicationMessage.Payload), cancellationToken);
            }
        };
        client.DisconnectedAsync += _ =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        var clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.MqttHost(target.Region), settings.MqttPort)
            .WithCredentials($"u_{target.AccountUid}", target.AccessToken)
            .WithClientId($"printos-{Guid.NewGuid():N}")
            .WithTlsOptions(tls => tls.UseTls())
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();

        try
        {
            await client.ConnectAsync(clientOptions, cancellationToken);
        }
        catch (MqttConnectingFailedException exception) when (IsCredentialRejection(exception.Message))
        {
            throw new BambuTokenRejectedException();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BambuCloudException("The Bambu Lab MQTT broker could not be reached.", exception);
        }

        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(reportTopic).Build(), cancellationToken);
        await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(requestTopic).WithPayload(PushAllPayload).Build(), cancellationToken);

        using var registration = cancellationToken.Register(() => disconnected.TrySetCanceled(cancellationToken));
        await disconnected.Task;
        throw new BambuCloudException("The Bambu Lab MQTT connection was closed.");
    }

    private static bool IsCredentialRejection(string message) =>
        message.Contains("BadUserNameOrPassword", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase);
}
