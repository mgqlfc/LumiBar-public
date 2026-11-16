namespace LumiBar;

internal readonly record struct RequestResult<T>(long Version, T Value);

/// <summary>Coalesce duplicate in-flight work and cancel superseded work. Consumers check Version before committing.</summary>
internal sealed class LatestRequest<T> : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task<RequestResult<T>>? _running;
    private string? _key;
    private long _version;
    private bool _disposed;
    public long Version { get { lock (_gate) return _version; } }

    public Task<RequestResult<T>> Run(string key, Func<CancellationToken, Task<T>> factory)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_key == key && _running is { IsCompleted: false }) return _running;
            _cancellation?.Cancel();
            _key = key;
            var source = _cancellation = new CancellationTokenSource();
            var version = ++_version;
            return _running = Execute(factory, source, version);
        }
    }

    private async Task<RequestResult<T>> Execute(Func<CancellationToken, Task<T>> factory, CancellationTokenSource source, long version)
    {
        try
        {
            var value = await factory(source.Token);
            source.Token.ThrowIfCancellationRequested();
            return new(version, value);
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_cancellation, source)) _cancellation = null; }
            source.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _version++; _cancellation?.Cancel(); }
    }
}

internal sealed class RefreshSchedule
{
    public DateTimeOffset NextAttempt { get; private set; } = DateTimeOffset.MinValue;
    public int Failures { get; private set; }
    public bool IsDue(DateTimeOffset now) => now >= NextAttempt;
    public void Success(DateTimeOffset now, int minutes) { Failures = 0; NextAttempt = now.AddMinutes(Math.Clamp(minutes, 10, 60)); }
    public void Failure(DateTimeOffset now)
    {
        var delay = new[] { 1, 5, 15, 30 }[Math.Min(Failures, 3)];
        Failures = Math.Min(Failures + 1, 4);
        NextAttempt = now.AddMinutes(delay);
    }
    public void Invalidate() => NextAttempt = DateTimeOffset.MinValue;
}
