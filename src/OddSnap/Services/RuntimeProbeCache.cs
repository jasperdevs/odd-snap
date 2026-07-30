namespace OddSnap.Services;

internal sealed class RuntimeProbeCache<TKey>(TimeSpan ttl, Func<DateTime>? utcNow = null)
    where TKey : notnull
{
    private sealed record State(bool Ready, string Status, DateTime CheckedUtc);

    private readonly object _gate = new();
    private readonly Dictionary<TKey, State> _states = new();
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public void Update(TKey key, bool ready, string status)
    {
        lock (_gate)
            _states[key] = new State(ready, status, _utcNow());
    }

    public void Clear(TKey key)
    {
        lock (_gate)
            _states.Remove(key);
    }

    public bool TryGet(TKey key, bool requireReady, out bool ready, out string status)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(key, out var state) &&
                (!requireReady || state.Ready) &&
                _utcNow() - state.CheckedUtc <= ttl)
            {
                ready = state.Ready;
                status = state.Status;
                return true;
            }
        }

        ready = false;
        status = "";
        return false;
    }
}
