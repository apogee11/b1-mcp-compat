using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Config;
using B1.Mcp.Compat.Core.Edm;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Core.Mappings;
using B1.Mcp.Compat.Core.Metadata;
using B1.Mcp.Compat.Core.Personal;

namespace B1.Mcp.Compat.Host;

/// <summary>
/// The HTTP proxy: everything is passed through to the Service Layer except two things an older Service Layer cannot answer:
/// the annotated $metadata (scope=entityset) and PersonalFieldsSetupsService_GetPersonalFieldsByTable.
/// An upstream that natively supports the annotations is detected and then passed through untouched.
/// </summary>
public sealed partial class ProxyEndpoint
{
    private readonly CompatOptions _o;
    private readonly HttpClient _http;
    private readonly string _upstream;
    private readonly Dictionary<string, MetadataSynthesizer> _synths;
    private readonly ICompanyDatabase _db;
    private readonly SessionRegistry _sessions = new();
    private readonly ConcurrentDictionary<string, bool> _buildWarned = new();
    private readonly BuildPolicy _policy;
    private readonly ILogger _log;
    private readonly TtlCache<EdmModel> _models = new(TimeSpan.FromMinutes(10));
    private readonly TtlCache<UserCatalog> _catalogs = new(TimeSpan.FromMinutes(5));
    private readonly TtlCache<IReadOnlyList<PersonalFieldRow>> _personal = new(TimeSpan.FromMinutes(5));
    private bool? _native;

    private static readonly HashSet<string> SkipRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "host", "content-length", "connection", "accept-encoding", "expect", "transfer-encoding", "keep-alive", "proxy-connection", "upgrade" };

    private static readonly HashSet<string> SkipResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "content-encoding", "transfer-encoding", "content-length", "set-cookie", "connection", "keep-alive" };

    public ProxyEndpoint(CompatOptions o, HttpClient http, IReadOnlyDictionary<string, KnowledgeBase> kbs, MappingOverrides ov, ICompanyDatabase db, BuildPolicy policy, ILogger log)
    {
        _o = o;
        _http = http;
        _upstream = o.ServiceLayer.Url.TrimEnd('/');
        _synths = kbs.ToDictionary(k => k.Key, k => new MetadataSynthesizer(k.Value, ov));
        _db = db;
        _policy = policy;
        _log = log;
    }

    [GeneratedRegex(@"/b1s/v2/\$metadata$", RegexOptions.IgnoreCase)] private static partial Regex MetadataPath();
    [GeneratedRegex(@"/PersonalFieldsSetupsService_GetPersonalFieldsByTable$", RegexOptions.IgnoreCase)] private static partial Regex PersonalPath();
    [GeneratedRegex(@"/b1s/v\d+/Login$", RegexOptions.IgnoreCase)] private static partial Regex LoginPath();
    [GeneratedRegex(@"B1SESSION=([^;]+)")] private static partial Regex SessionCookie();
    [GeneratedRegex(@";\s*Secure", RegexOptions.IgnoreCase)] private static partial Regex SecureAttr();
    [GeneratedRegex(@";\s*SameSite=None", RegexOptions.IgnoreCase)] private static partial Regex SameSiteNone();

    public async Task HandleAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var path = req.Path.Value ?? "/";
        var started = Stopwatch.GetTimestamp();
        var ev = new Dictionary<string, object?>
        {
            ["http.request.method"] = req.Method,
            ["url.path"] = path,
            ["url.query"] = req.QueryString.Value,
            ["client.address"] = ctx.Connection.RemoteIpAddress?.ToString(),
            ["kind"] = "passthrough",
        };
        if (SessionOf(ctx) is { } sid) ev["session"] = sid.Length > 8 ? sid[..8] : sid;
        ctx.Items[WideKey] = ev;
        Exception? failure = null;
        try
        {
            if (HttpMethods.IsGet(req.Method) && MetadataPath().IsMatch(path) && req.Query["scope"].Contains("entityset") && !await IsNativeAsync(ctx))
            {
                await ServeMetadataAsync(ctx);
                return;
            }
            if (HttpMethods.IsPost(req.Method) && PersonalPath().IsMatch(path))
            {
                await ServePersonalAsync(ctx);
                return;
            }
            await ForwardAsync(ctx, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            failure = e;
            if (!ctx.Response.HasStarted) await WriteJsonAsync(ctx, 502, ErrorJson($"b1-mcp-compat: the Service Layer at {_upstream} could not be reached: {e.Message}"));
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            // Wide event: one line per request with everything known about it.
            var status = failure != null && !ctx.Response.HasStarted ? 500 : ctx.Response.StatusCode;
            ev["http.response.status_code"] = status;
            ev["duration_ms"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);
            ev["outcome"] = status >= 500 || failure != null ? "error" : "success";
            _log.Log(status >= 500 || failure != null ? LogLevel.Error : LogLevel.Information, 0, ev.ToList(), failure,
                (_, _) => FormattableString.Invariant($"{req.Method} {path} {status} {ev["duration_ms"]}ms"));
        }
    }

    private const string WideKey = "wide-event";

    private static Dictionary<string, object?> Wide(HttpContext ctx) => (Dictionary<string, object?>)ctx.Items[WideKey]!;

    private static string Truncate(string s) => s.Length <= 2000 ? s : s[..2000] + "...";

    // ------------------------------------------------------------ metadata

    private async Task<bool> IsNativeAsync(HttpContext ctx)
    {
        if (_native is { } n) return n;
        using var r = await SendAsync(ctx, HttpMethod.Get, "/b1s/v2/$metadata?scope=entityset&annotation=labelWithTable", null, Accept: null);
        if (!r.IsSuccessStatusCode) return false; // cannot tell (e.g. session expired): do not cache; the legacy path will surface the error
        var text = await r.Content.ReadAsStringAsync(ctx.RequestAborted);
        _native = text.Contains("SAPB1.TableName", StringComparison.Ordinal);
        _log.LogInformation("Upstream natively supports annotated metadata: {Native}", _native);
        return _native.Value;
    }

    private string? CompanyOf(HttpContext ctx) => _sessions.CompanyFor(SessionOf(ctx));

    private static string? SessionOf(HttpContext ctx) => ctx.Request.Cookies.TryGetValue("B1SESSION", out var s) ? s : null;

    private async Task ServeMetadataAsync(HttpContext ctx)
    {
        var q = MetadataQuery.Parse(k => ctx.Request.Query[k].Select(v => v ?? ""));
        var company = CompanyOf(ctx);
        var ev = Wide(ctx);
        ev["kind"] = "synth_metadata";
        ev["b1.entity_set"] = q.EntitySet;
        ev["b1.company_db"] = company;
        if (company == null)
        {
            await WriteJsonAsync(ctx, 502, ErrorJson("b1-mcp-compat: the company database of this session is unknown. Log in through the connector (POST /b1s/v2/Login) so the CompanyDB can be captured."));
            return;
        }
        var code = _sessions.VersionFor(SessionOf(ctx));
        var decision = _policy.Resolve(code);
        if (!decision.Ok || !_synths.TryGetValue(decision.Release!, out var synth))
        {
            await WriteJsonAsync(ctx, 502, ErrorJson(decision.Error ?? BuildPolicy.UnsupportedMessage));
            return;
        }
        var release = decision.Release!;
        ev["b1.release"] = release;
        WarnOnce(release, decision);
        try
        {
            var model = await _models.GetAsync(company, async () =>
            {
                using var r = await SendAsync(ctx, HttpMethod.Get, "/b1s/v2/$metadata", null, Accept: "application/xml");
                if (!r.IsSuccessStatusCode) throw new UpstreamStatusException((int)r.StatusCode, await r.Content.ReadAsStringAsync(ctx.RequestAborted));
                return EdmModel.Parse(await r.Content.ReadAsStringAsync(ctx.RequestAborted));
            });
            var catalog = new LazyCatalog(ct => _catalogs.GetAsync(company, () => LoadUserCatalogAsync(ctx, company)));
            var xml = await synth.BuildAsync(q, model, catalog, s => _log.LogWarning("Entity set {Name} skipped from the list: not in the knowledge base and not a user table/object of {Company}", s, company), ctx.RequestAborted);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/xml;odata.metadata=minimal;charset=utf-8";
            await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(xml), ctx.RequestAborted);
        }
        catch (UnresolvedMappingException e)
        {
            await WriteJsonAsync(ctx, 502, e.ToJson());
        }
        catch (EntitySetNotFoundException)
        {
            await WriteJsonAsync(ctx, 404, ErrorJson("Entity set not found"));
        }
        catch (UpstreamStatusException e)
        {
            await WriteJsonAsync(ctx, e.Status, e.Body.Length > 0 ? e.Body : ErrorJson("Service Layer metadata request failed"));
        }
    }

    // ------------------------------------------------------------ user-defined objects

    /// <summary>Reads the company's user fields, tables and objects from the Service Layer itself (UserFieldsMD, UserTablesMD, UserObjectsMD) with the caller's session.</summary>
    private async Task<UserCatalog> LoadUserCatalogAsync(HttpContext ctx, string company)
    {
        var fields = await ReadAllAsync(ctx, "UserFieldsMD?$select=Name,TableName,Description");
        var tables = await ReadAllAsync(ctx, "UserTablesMD?$select=TableName,TableDescription,TableType");
        var objects = await ReadAllAsync(ctx, "UserObjectsMD");
        var catalog = UserCatalog.Parse(fields, tables, objects);
        // Child tables of user objects take ALL their physical columns (see UserCatalog.PhysicalColumns). Without a database login they stay unresolved.
        if (catalog.UserObjects.Values.Any(o => o.Children.Count > 0))
        {
            try
            {
                foreach (var (t, cols) in await _db.LoadUserTableColumnsAsync(company, ctx.RequestAborted))
                    catalog.PhysicalColumns[t] = new HashSet<string>(cols, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is CompanyDatabaseException or NotSupportedException or InvalidOperationException)
            {
                _log.LogWarning("Physical columns of user-object child tables unavailable for {Company}: {Msg}", company, e.Message);
            }
        }
        return catalog;
    }

    private async Task<List<JsonElement>> ReadAllAsync(HttpContext ctx, string first)
    {
        var rows = new List<JsonElement>();
        for (string? next = "/b1s/v2/" + first; next != null;)
        {
            using var r = await SendAsync(ctx, HttpMethod.Get, next, null, Accept: "application/json");
            var text = await r.Content.ReadAsStringAsync(ctx.RequestAborted);
            if (!r.IsSuccessStatusCode) throw new UpstreamStatusException((int)r.StatusCode, text);
            using var doc = JsonDocument.Parse(text);
            rows.AddRange(doc.RootElement.GetProperty("value").EnumerateArray().Select(e => e.Clone()));
            next = doc.RootElement.TryGetProperty("odata.nextLink", out var n) || doc.RootElement.TryGetProperty("@odata.nextLink", out n)
                ? (n.GetString()!.StartsWith("/b1s/", StringComparison.Ordinal) ? n.GetString() : "/b1s/v2/" + n.GetString())
                : null;
        }
        return rows;
    }

    // ------------------------------------------------------------ personal data

    private async Task ServePersonalAsync(HttpContext ctx)
    {
        var body = await ReadBodyAsync(ctx);
        Wide(ctx)["kind"] = "synth_personal";
        if (!_o.SimulateLegacy)
        {
            using var r = await SendAsync(ctx, HttpMethod.Post, ctx.Request.Path.Value + ctx.Request.QueryString, body, Accept: null);
            // 400/404 = the upstream does not know the service (older Service Layer). Everything else (incl. 401) is passed on as is.
            if ((int)r.StatusCode is not (400 or 404))
            {
                await CopyResponseAsync(ctx, r, body);
                return;
            }
            Wide(ctx)["upstream.status"] = (int)r.StatusCode; // the upstream has no PersonalFieldsSetupsService: served from OPFS
        }
        else if (!_sessions.Knows(SessionOf(ctx)))
        {
            await WriteJsonAsync(ctx, 401, ErrorJson("Invalid session."));
            return;
        }

        string table;
        try { table = JsonDocument.Parse(body).RootElement.GetProperty("PersonalFieldsSetupTableParams").GetProperty("TableName").GetString() ?? ""; }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            await WriteJsonAsync(ctx, 400, ErrorJson("Invalid request body: expected {\"PersonalFieldsSetupTableParams\":{\"TableName\":\"...\"}}"));
            return;
        }
        var company = CompanyOf(ctx);
        Wide(ctx)["b1.table"] = table;
        Wide(ctx)["b1.company_db"] = company;
        if (company == null)
        {
            await WriteJsonAsync(ctx, 502, ErrorJson("b1-mcp-compat: the company database of this session is unknown. Log in through the connector."));
            return;
        }
        var decision = _policy.Resolve(_sessions.VersionFor(SessionOf(ctx)));
        if (!decision.Ok)
        {
            await WriteJsonAsync(ctx, 502, ErrorJson(decision.Error!));
            return;
        }
        var release = decision.Release!;
        Wide(ctx)["b1.release"] = release;
        WarnOnce(release, decision);
        try
        {
            var rows = await _personal.GetAsync(company, () => _db.LoadPersonalFieldsAsync(company, ctx.RequestAborted));
            var json = PersonalFieldMapper.ToJson(rows, table, release, $"{ctx.Request.Scheme}://{ctx.Request.Host}");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json;odata.metadata=minimal;charset=utf-8";
            await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(json), ctx.RequestAborted);
        }
        catch (Exception e) when (e is CompanyDatabaseException or NotSupportedException)
        {
            Wide(ctx)["error"] = e.Message;
            await WriteJsonAsync(ctx, 502, ErrorJson("b1-mcp-compat: personal data classification (OPFS) is required and failed: " + e.Message));
        }
    }

    // ------------------------------------------------------------ pass through

    private static async Task<byte[]> ReadBodyAsync(HttpContext ctx)
    {
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
        return ms.ToArray();
    }

    private async Task ForwardAsync(HttpContext ctx, byte[]? bufferedBody)
    {
        var req = ctx.Request;
        var isLogin = HttpMethods.IsPost(req.Method) && LoginPath().IsMatch(req.Path.Value ?? "");
        if (isLogin)
        {
            bufferedBody ??= await ReadBodyAsync(ctx);
            try // the password is never logged
            {
                var login = JsonDocument.Parse(bufferedBody).RootElement;
                Wide(ctx)["b1.company_db"] = login.GetProperty("CompanyDB").GetString();
                Wide(ctx)["user.name"] = login.GetProperty("UserName").GetString();
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        var pathAndQuery = req.Path.ToUriComponent() + req.QueryString;
        using var r = await SendAsync(ctx, new HttpMethod(req.Method), pathAndQuery, bufferedBody, Accept: null, streamBody: bufferedBody == null);
        await CopyResponseAsync(ctx, r, bufferedBody, isLogin);
    }

    /// <summary>What an SL older than FP2608 does: the annotated-metadata options are silently ignored (observed with /b1s/v1 on a 2608).</summary>
    private string ToUpstream(string pathAndQuery)
    {
        if (!_o.SimulateLegacy) return pathAndQuery;
        var qi = pathAndQuery.IndexOf('?');
        var p = qi < 0 ? pathAndQuery : pathAndQuery[..qi];
        if (!MetadataPath().IsMatch(p) || qi < 0) return pathAndQuery;
        var kept = pathAndQuery[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(kv => Uri.UnescapeDataString(kv.Split('=')[0]) is not ("scope" or "annotation" or "entityset" or "dependency"));
        var rest = string.Join('&', kept);
        return rest.Length == 0 ? p : p + "?" + rest;
    }

    private Task<HttpResponseMessage> SendAsync(HttpContext ctx, HttpMethod method, string pathAndQuery, byte[]? body, string? Accept, bool streamBody = false)
    {
        var msg = new HttpRequestMessage(method, _upstream + ToUpstream(pathAndQuery));
        HttpContent? content = null;
        if (body is { Length: > 0 }) content = new ByteArrayContent(body);
        else if (streamBody && (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding")))
        {
            content = new StreamContent(ctx.Request.Body);
            if (ctx.Request.ContentLength is { } len) content.Headers.ContentLength = len;
        }
        foreach (var h in ctx.Request.Headers)
        {
            if (SkipRequestHeaders.Contains(h.Key)) continue;
            if (Accept != null && h.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)) continue;
            if (!msg.Headers.TryAddWithoutValidation(h.Key, (IEnumerable<string>)h.Value) && content != null)
                content.Headers.TryAddWithoutValidation(h.Key, (IEnumerable<string>)h.Value);
        }
        if (Accept != null) msg.Headers.TryAddWithoutValidation("Accept", Accept);
        msg.Content = content;
        return _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
    }

    private async Task CopyResponseAsync(HttpContext ctx, HttpResponseMessage r, byte[]? requestBody, bool isLogin = false)
    {
        var res = ctx.Response;
        res.StatusCode = (int)r.StatusCode;
        foreach (var h in r.Headers.Concat(r.Content.Headers))
            if (!SkipResponseHeaders.Contains(h.Key)) res.Headers[h.Key] = h.Value.ToArray();
        var plain = !ctx.Request.IsHttps;
        if (r.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var c in cookies)
                res.Headers.Append("Set-Cookie", plain ? SameSiteNone().Replace(SecureAttr().Replace(c, ""), "") : c);
        }
        var type = r.Content.Headers.ContentType?.MediaType ?? "";
        if (type.Contains("json", StringComparison.OrdinalIgnoreCase) || isLogin)
        {
            var text = await r.Content.ReadAsStringAsync(ctx.RequestAborted);
            if (!r.IsSuccessStatusCode) Wide(ctx)["response_body"] = Truncate(text);
            if (isLogin && r.IsSuccessStatusCode) CaptureLogin(requestBody, text);
            var self = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var outText = text.Replace(_upstream, self, StringComparison.Ordinal);
            await res.Body.WriteAsync(Encoding.UTF8.GetBytes(outText), ctx.RequestAborted);
            return;
        }
        if (r.Content.Headers.ContentLength is { } l) res.ContentLength = l;
        await r.Content.CopyToAsync(res.Body, ctx.RequestAborted);
    }

    /// <summary>Logs the unverified-build warning (or the verified confirmation) once per package.</summary>
    private void WarnOnce(string release, BuildDecision d)
    {
        if (!_buildWarned.TryAdd(release, true)) return;
        if (d.Warning != null) _log.LogWarning("{Msg}", d.Warning);
        else _log.LogInformation("Package {Package}: declared build {Build} is the one the knowledge base was extracted from (supported).", release, _policy.Build);
    }

    private void CaptureLogin(byte[]? requestBody, string responseText)
    {
        try
        {
            var db = JsonDocument.Parse(requestBody ?? Array.Empty<byte>()).RootElement.GetProperty("CompanyDB").GetString();
            using var answer = JsonDocument.Parse(responseText);
            var sid = answer.RootElement.GetProperty("SessionId").GetString();
            var version = answer.RootElement.TryGetProperty("Version", out var v) ? v.GetString() : null;
            if (!string.IsNullOrEmpty(db) && !string.IsNullOrEmpty(sid)) _sessions.Add(sid, db, version);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { /* not a direct Login body */ }
    }

    // ------------------------------------------------------------ helpers

    private static string ErrorJson(string message) =>
        JsonSerializer.Serialize(new { error = new { code = -1, message = new { lang = "en-us", value = message } } });

    private static async Task WriteJsonAsync(HttpContext ctx, int status, string json)
    {
        if (status >= 400) Wide(ctx)["response_body"] = Truncate(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json;charset=utf-8";
        await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(json));
    }

    private sealed class UpstreamStatusException : Exception
    {
        public int Status { get; }
        public string Body { get; }

        public UpstreamStatusException(int status, string body) : base($"Service Layer answered {status}")
        {
            Status = status;
            Body = body;
        }
    }
}
