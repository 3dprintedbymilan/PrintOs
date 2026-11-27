using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace PrintOs.Api.Modules.Account;

public sealed class ExportRateLimiter(IOptions<AccountOptions> options, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> attempts = new();

    public bool TryAcquire(Guid workspaceId, out TimeSpan retryAfter)
    {
        var now = timeProvider.GetUtcNow();
        var window = TimeSpan.FromMinutes(Math.Max(1, options.Value.ExportWindowMinutes));
        var queue = attempts.GetOrAdd(workspaceId, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= Math.Max(1, options.Value.ExportsPerWindow))
            {
                retryAfter = window - (now - queue.Peek());
                return false;
            }

            queue.Enqueue(now);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }
}
