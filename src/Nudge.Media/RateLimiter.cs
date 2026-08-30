namespace Nudge.Media;

/// <summary>
/// Ensures at least a minimum interval between calls, so a bulk artwork pass (e.g. every tile in a
/// 1,000-table library loading at once) never hammers a network source. Shared by every
/// network-backed <see cref="Core.Abstractions.IArtworkProvider"/> rather than each reimplementing
/// the same gate.
/// </summary>
public sealed class RateLimiter(TimeSpan minimumInterval)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan sinceLastRequest = DateTimeOffset.UtcNow - _lastRequestAt;
            if (sinceLastRequest < minimumInterval)
            {
                await Task.Delay(minimumInterval - sinceLastRequest, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}
