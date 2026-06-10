using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal sealed class OgsFriendDataRequestCache
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>();
    private readonly TimeSpan lifetime;
    private readonly object syncRoot = new object();

    public OgsFriendDataRequestCache()
        : this(DefaultLifetime)
    {
    }

    public OgsFriendDataRequestCache(TimeSpan lifetime)
    {
        this.lifetime = lifetime <= TimeSpan.Zero ? DefaultLifetime : lifetime;
    }

    public async Task<JToken> GetJsonAsync(
        string cacheKey,
        Func<CancellationToken, Task<JToken>> requestFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cacheKey)) {
            throw new ArgumentException("OGS friend data cache key is empty.", nameof(cacheKey));
        }
        if (requestFactory == null) {
            throw new ArgumentNullException(nameof(requestFactory));
        }

        DateTime now = DateTime.UtcNow;
        lock (syncRoot) {
            if (entries.TryGetValue(cacheKey, out CacheEntry entry) && now - entry.requestedAtUtc < lifetime) {
                return entry.payload?.DeepClone() ?? new JObject();
            }
        }

        JToken payload = await requestFactory(cancellationToken);
        lock (syncRoot) {
            entries[cacheKey] = new CacheEntry(now, payload?.DeepClone() ?? new JObject());
        }

        return payload?.DeepClone() ?? new JObject();
    }

    public void Clear()
    {
        lock (syncRoot) {
            entries.Clear();
        }
    }

    private sealed class CacheEntry
    {
        public readonly DateTime requestedAtUtc;
        public readonly JToken payload;

        public CacheEntry(DateTime requestedAtUtc, JToken payload)
        {
            this.requestedAtUtc = requestedAtUtc;
            this.payload = payload;
        }
    }
}
