namespace PrintOs.Api.Modules.Bambu;

public abstract record PrintEvent(
    Guid WorkspaceId,
    Guid PrinterId,
    string PrinterName,
    Guid PrintJobId,
    string JobName,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool ConsumedStock,
    string? Flag,
    Guid? OrderLineId,
    string Source,
    bool IsCatchUp);

public sealed record PrintFinishedEvent(
    Guid WorkspaceId,
    Guid PrinterId,
    string PrinterName,
    Guid PrintJobId,
    string JobName,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool ConsumedStock,
    string? Flag,
    Guid? OrderLineId,
    string Source,
    bool IsCatchUp)
    : PrintEvent(WorkspaceId, PrinterId, PrinterName, PrintJobId, JobName, StartedAt, FinishedAt, ConsumedStock, Flag, OrderLineId, Source, IsCatchUp);

public sealed record PrintFailedEvent(
    Guid WorkspaceId,
    Guid PrinterId,
    string PrinterName,
    Guid PrintJobId,
    string JobName,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool ConsumedStock,
    string? Flag,
    Guid? OrderLineId,
    string Source,
    bool IsCatchUp,
    string? ErrorCode)
    : PrintEvent(WorkspaceId, PrinterId, PrinterName, PrintJobId, JobName, StartedAt, FinishedAt, ConsumedStock, Flag, OrderLineId, Source, IsCatchUp);

public static class PrintEventSources
{
    public const string Telemetry = "telemetry";
    public const string HistorySync = "history-sync";
}

public interface IPrintEventPublisher
{
    Task PublishAsync<TEvent>(TEvent printEvent, CancellationToken cancellationToken)
        where TEvent : PrintEvent;
}

public interface IPrintEventHandler<in TEvent>
    where TEvent : PrintEvent
{
    Task HandleAsync(TEvent printEvent, CancellationToken cancellationToken);
}

public sealed class InProcessPrintEventPublisher(IServiceProvider services, ILogger<InProcessPrintEventPublisher> logger) : IPrintEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent printEvent, CancellationToken cancellationToken)
        where TEvent : PrintEvent
    {
        foreach (var handler in services.GetServices<IPrintEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(printEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Print event handler {Handler} failed for job {PrintJobId}", handler.GetType().Name, printEvent.PrintJobId);
            }
        }
    }
}
