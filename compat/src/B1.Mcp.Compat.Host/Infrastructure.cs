using B1.Mcp.Compat.Core.Config;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Core.Mappings;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Host;

/// <summary>Async cache with a time to live; failures are not cached.</summary>
public sealed class TtlCache<T>
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, (DateTime At, Task<T> Value)> _items = new(StringComparer.OrdinalIgnoreCase);

    public TtlCache(TimeSpan ttl) => _ttl = ttl;

    public async Task<T> GetAsync(string key, Func<Task<T>> factory)
    {
        if (_items.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < _ttl && !hit.Value.IsFaulted && !hit.Value.IsCanceled) return await hit.Value;
        var task = factory();
        _items[key] = (DateTime.UtcNow, task);
        try { return await task; }
        catch { _items.TryRemove(key, out _); throw; }
    }
}

/// <summary>Remembers which company database each Service Layer session belongs to (captured from POST /Login).</summary>
public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, string> _db = new();
    private readonly ConcurrentDictionary<string, string> _version = new();
    private readonly ConcurrentQueue<string> _order = new();
    private volatile string? _last;
    private volatile string? _lastVersion;

    public string? Last => _last;

    public void Add(string sessionId, string companyDb, string? version)
    {
        _last = companyDb;
        if (!string.IsNullOrEmpty(version)) { _version[sessionId] = version; _lastVersion = version; }
        if (_db.TryAdd(sessionId, companyDb)) _order.Enqueue(sessionId);
        else _db[sessionId] = companyDb;
        while (_db.Count > 5000 && _order.TryDequeue(out var old)) { _db.TryRemove(old, out _); _version.TryRemove(old, out _); }
    }

    public bool Knows(string? sessionId) => sessionId != null && _db.ContainsKey(sessionId);

    /// <summary>Company of the session; falls back to the company of the last login.</summary>
    public string? CompanyFor(string? sessionId) => sessionId != null && _db.TryGetValue(sessionId, out var d) ? d : _last;

    /// <summary>Service Layer version code of the session (the "Version" of the Login answer, e.g. 1000260); falls back to the last login.</summary>
    public string? VersionFor(string? sessionId) => sessionId != null && _version.TryGetValue(sessionId, out var v) ? v : _lastVersion;
}

/// <summary>Loads the knowledge bases embedded in the exe, one per package (kb-B11000SP_2408.json.gz, kb-B110002608HF_1.json.gz, ...).</summary>
public static class KbLoader
{
    /// <summary>Version code of the Login answer ("1000340") to the release ("2608") of the knowledge bases extracted from files with that file version (10.0.340.0). Derived from the embedded knowledge bases, so every supported package is listed.</summary>
    public static IReadOnlyDictionary<string, string> Releases(IReadOnlyDictionary<string, KnowledgeBase> kbs)
    {
        var r = new Dictionary<string, string>();
        foreach (var (package, kb) in kbs)
        {
            var v = kb.Build?.Files.FirstOrDefault(f => f.Name.Equals("OBServerDLL.dll", StringComparison.OrdinalIgnoreCase))?.FileVersionFixed?.Split('.');
            if (v is { Length: >= 3 } && int.TryParse(v[0], out var ma) && int.TryParse(v[1], out var mi) && int.TryParse(v[2], out var bu))
                r.TryAdd($"{ma}{mi:00}{bu:000}", BuildPolicy.ReleaseOf(package));
        }
        return r;
    }

    /// <summary>Package (e.g. "B11000SP_2408") to its knowledge base.</summary>
    public static Dictionary<string, KnowledgeBase> Load(ILogger log)
    {
        var all = new Dictionary<string, KnowledgeBase>();
        var asm = typeof(KbLoader).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("kb-", StringComparison.Ordinal) && n.EndsWith(".json.gz", StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            all[name["kb-".Length..^".json.gz".Length]] = KnowledgeBase.Load(gz);
        }
        if (all.Count == 0) log.LogWarning("No embedded knowledge base in this build: standard entity sets cannot be annotated.");
        return all;
    }

    /// <summary>Package (e.g. "B11000SP_2408", the key of its knowledge base) to its extended read package (read-B11000SP_2408.json.gz). A package that does not load is an error at start, never silently skipped.</summary>
    public static Dictionary<string, ReadPackage> LoadReadPackages(ILogger log)
    {
        var all = new Dictionary<string, ReadPackage>();
        var asm = typeof(KbLoader).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("read-", StringComparison.Ordinal) && n.EndsWith(".json.gz", StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            all[name["read-".Length..^".json.gz".Length]] = ReadPackage.LoadGz(s);
        }
        if (all.Count == 0) log.LogInformation("No embedded extended read package: sets missing in the Service Layer are not served.");
        return all;
    }

    public static MappingOverrides LoadMappings(CompatOptions o, ILogger log)
    {
        var p = CompatOptions.Resolve(o.Mappings.OverridePath);
        if (!File.Exists(p)) return MappingOverrides.Empty;
        var m = MappingOverrides.Parse(File.ReadAllText(p));
        log.LogInformation("Mapping overrides loaded from {Path}: {N} mapping(s), {E} enum value(s)", p, m.Mappings.Count, m.Enums.Count);
        return m;
    }
}

public static class Http
{
    /// <summary>HttpClient for the Service Layer: no redirects, no automatic decompression, no cookie jar (cookies are passed through).</summary>
    public static HttpClient CreateUpstreamClient(CompatOptions o)
    {
        var h = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };
        if (o.ServiceLayer.SkipTlsVerify) h.SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true };
        return new HttpClient(h) { Timeout = TimeSpan.FromMinutes(10) };
    }
}

public static class TlsCertificate
{
    /// <summary>Loads the PFX the administrator provides for https. There is no generated fallback.</summary>
    public static X509Certificate2 Load(CompatOptions.TlsOptions tls)
    {
        if (string.IsNullOrWhiteSpace(tls.CertificatePath))
            throw new InvalidOperationException("Listen is https:// but Tls:CertificatePath is not set. Provide a PFX certificate or listen on http://.");
        var p = CompatOptions.Resolve(tls.CertificatePath);
        if (!File.Exists(p)) throw new InvalidOperationException($"Certificate file not found: {p}");
        return X509CertificateLoader.LoadPkcs12FromFile(p, tls.CertificatePassword);
    }
}
