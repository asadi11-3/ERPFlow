using System.Security.Cryptography;
using System.Text;

namespace ERPFlow.Api;

public sealed class LoginAttemptLimiter(TimeProvider clock)
{
    private readonly object sync = new();
    private readonly Dictionary<string, Window> accounts = new(StringComparer.Ordinal);
    private Window global = new(DateTimeOffset.MinValue, 0);

    public bool TryAcquire(string email)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())));
        lock (sync)
        {
            var now = clock.GetUtcNow();
            if (now >= global.End) global = new(now.AddMinutes(1), 0);
            if (global.Count >= 100) return false;
            global = global with { Count = global.Count + 1 };
            foreach (var expired in accounts.Where(item => now >= item.Value.End).Select(item => item.Key).ToArray()) accounts.Remove(expired);
            if (!accounts.TryGetValue(key, out var window))
            {
                if (accounts.Count >= 2048) return false;
                window = new(now.AddMinutes(1), 0);
            }
            if (window.Count >= 5) return false;
            accounts[key] = window with { Count = window.Count + 1 };
            return true;
        }
    }

    private sealed record Window(DateTimeOffset End, int Count);
}
