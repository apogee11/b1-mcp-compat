using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Core.Mappings;

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
    private readonly ConcurrentQueue<string> _order = new();
    private volatile string? _last;

    public string? Last => _last;

    public void Add(string sessionId, string companyDb)
    {
        _last = companyDb;
        if (_db.TryAdd(sessionId, companyDb)) _order.Enqueue(sessionId);
        else _db[sessionId] = companyDb;
        while (_db.Count > 5000 && _order.TryDequeue(out var old)) _db.TryRemove(old, out _);
    }

    public bool Knows(string? sessionId) => sessionId != null && _db.ContainsKey(sessionId);

    /// <summary>Company of the session; falls back to the company of the last login.</summary>
    public string? CompanyFor(string? sessionId) => sessionId != null && _db.TryGetValue(sessionId, out var d) ? d : _last;
}

/// <summary>Loads the knowledge base: embedded resource plus optional override file merged over it (the file wins).</summary>
public static class KbLoader
{
    public static KnowledgeBase Load(CompatOptions o, ILogger log)
    {
        var kb = new KnowledgeBase();
        var asm = typeof(KbLoader).Assembly;
        using (var s = asm.GetManifestResourceStream("kb.json.gz"))
        {
            if (s != null) { using var gz = new GZipStream(s, CompressionMode.Decompress); kb = KnowledgeBase.Load(gz); }
            else log.LogWarning("No embedded knowledge base in this build: run 'gen-kb' and rebuild, or provide KnowledgeBase:OverridePath.");
        }
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.KnowledgeBase.OverridePath)) candidates.Add(CompatOptions.Resolve(o.KnowledgeBase.OverridePath));
        else { candidates.Add(CompatOptions.Resolve("kb.override.json.gz")); candidates.Add(CompatOptions.Resolve("kb.override.json")); }
        foreach (var p in candidates.Where(File.Exists))
        {
            kb.MergeFrom(ReadFile(p));
            log.LogInformation("Knowledge base override merged from {Path}", p);
            break;
        }
        return kb;
    }

    public static KnowledgeBase ReadFile(string path)
    {
        using var f = File.OpenRead(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) { using var gz = new GZipStream(f, CompressionMode.Decompress); return KnowledgeBase.Load(gz); }
        return KnowledgeBase.Load(f);
    }

    public static void WriteFile(KnowledgeBase kb, string path)
    {
        using var f = File.Create(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) { using var gz = new GZipStream(f, CompressionLevel.SmallestSize); kb.Save(gz); }
        else kb.Save(f);
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
        if (o.Upstream.SkipTlsVerify) h.SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true };
        return new HttpClient(h) { Timeout = TimeSpan.FromMinutes(10) };
    }
}

public static class SelfSigned
{
    public static X509Certificate2 Create(CompatOptions.TlsOptions tls)
    {
        if (!string.IsNullOrWhiteSpace(tls.CertificatePath))
        {
            var p = CompatOptions.Resolve(tls.CertificatePath);
            if (File.Exists(p)) return X509CertificateLoader.LoadPkcs12FromFile(p, tls.CertificatePassword);
            var generated = Generate();
            File.WriteAllBytes(p, generated.Export(X509ContentType.Pfx, tls.CertificatePassword));
            return generated;
        }
        return Generate();
    }

    private static X509Certificate2 Generate()
    {
        using var rsa = RSA.Create(2048);
        var host = Dns.GetHostName();
        var req = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(host);
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        // re-import so the private key is usable by Kestrel on Windows
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }
}
