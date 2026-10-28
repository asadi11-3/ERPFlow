namespace ERPFlow.Domain;

public static class BusinessClock
{
    private static readonly AsyncLocal<TimeProvider?> Current = new();

    public static DateTime UtcNow => (Current.Value ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    internal static IDisposable Use(TimeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var previous = Current.Value;
        Current.Value = provider;
        return new Scope(previous);
    }

    private sealed class Scope(TimeProvider? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            Current.Value = previous;
            disposed = true;
        }
    }
}
