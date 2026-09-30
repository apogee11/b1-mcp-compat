using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Edm;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Data;
using Microsoft.Extensions.Configuration;

namespace B1.Mcp.Compat.Host;

/// <summary>
/// gen-kb: builds the knowledge base from a NATIVE FP2608+ Service Layer.
///   B1.Mcp.Compat.Host gen-kb [--sl https://host:50000] [--out kb.json.gz] [--merge existing.json.gz] [--company DB] [--no-db]
/// Credentials come from the environment (B1_COMPANY_DB, B1_USERNAME, B1_PASSWORD), never from the command line.
/// The company database (Compat__Database__* variables) is read for CUFD/OUTB/OUDO so that user fields, user tables and user
/// objects of that company are EXCLUDED; --no-db drops every U_* property instead (standard U_* columns such as OUSR.U_NAME are then lost).
/// An existing KB (--merge, default: --out if it exists) is merged: union, newest wins.
/// </summary>
public static class GenKbCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? Opt(string n) { var i = Array.IndexOf(args, "--" + n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var cfg = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args.Where(a => a.Contains('=')).ToArray()).Build();
        var o = cfg.GetSection(CompatOptions.Section).Get<CompatOptions>() ?? new CompatOptions();
        var sl = (Opt("sl") ?? o.Upstream.Url).TrimEnd('/');
        var outPath = Opt("out") ?? "kb.json.gz";
        var mergePath = Opt("merge") ?? (File.Exists(outPath) ? outPath : null);
        var noDb = args.Contains("--no-db");
        var company = Opt("company") ?? Environment.GetEnvironmentVariable("B1_COMPANY_DB");
        var user = Environment.GetEnvironmentVariable("B1_USERNAME");
        var pwd = Environment.GetEnvironmentVariable("B1_PASSWORD");
        if (string.IsNullOrEmpty(company) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pwd))
        {
            Console.Error.WriteLine("gen-kb needs the environment variables B1_COMPANY_DB, B1_USERNAME and B1_PASSWORD (a native FP2608+ Service Layer).");
            return 2;
        }

        Func<string, string, bool> isUserField;
        Func<string, bool> isUserSet;
        if (noDb)
        {
            Console.WriteLine("WARNING --no-db: every U_* property is dropped from the KB (cannot tell standard U_ columns from user fields).");
            isUserField = (_, p) => p.StartsWith("U_", StringComparison.Ordinal);
            isUserSet = _ => false;
        }
        else
        {
            CompanyCatalog cat;
            try { cat = await CompanyDatabaseFactory.Create(o.Database).LoadCatalogAsync(company, CancellationToken.None); }
            catch (Exception e) when (e is CompanyDatabaseException or NotSupportedException)
            {
                Console.Error.WriteLine("gen-kb needs the company database to exclude user-defined things (or --no-db): " + e.Message);
                return 2;
            }
            Console.WriteLine($"Company catalog: {cat.Udfs.Count} user fields, {cat.UserTables.Count} user tables, {cat.UserObjects.Count} user objects.");
            isUserField = (t, p) => cat.TryGetUdf(t, p, out _);
            isUserSet = n => (n.StartsWith("U_", StringComparison.Ordinal) && cat.UserTables.TryGetValue(n[2..], out var ut) && ut.IsEntitySet) || cat.UserObjects.ContainsKey(n);
        }

        using var http = Http.CreateUpstreamClient(new CompatOptions { Upstream = new CompatOptions.UpstreamOptions { SkipTlsVerify = o.Upstream.SkipTlsVerify } });
        using var login = await http.PostAsync(sl + "/b1s/v2/Login", new StringContent(JsonSerializer.Serialize(new { CompanyDB = company, UserName = user, Password = pwd }), Encoding.UTF8, "application/json"));
        if (!login.IsSuccessStatusCode) { Console.Error.WriteLine($"Login failed: HTTP {(int)login.StatusCode}"); return 2; }
        var lj = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        var cookie = "B1SESSION=" + lj.GetProperty("SessionId").GetString();
        var slVersion = lj.TryGetProperty("Version", out var v) ? v.GetString() : null;

        async Task<string> Get(string q)
        {
            using var m = new HttpRequestMessage(HttpMethod.Get, sl + "/b1s/v2/$metadata" + q);
            m.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var r = await http.SendAsync(m);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"$metadata{q} -> HTTP {(int)r.StatusCode}");
            return await r.Content.ReadAsStringAsync();
        }

        var list = EdmModel.Parse(await Get("?scope=entityset&annotation=labelWithTable"));
        if (!list.Sets.Any(s => EdmModel.Annotations(s).ContainsKey("SAPB1.TableName")))
        {
            Console.Error.WriteLine("This Service Layer does not return annotated metadata: gen-kb needs a native FP2608+ Service Layer.");
            return 2;
        }
        var full = EdmModel.Parse(await Get(""));
        var names = list.Sets.Select(s => (string)s.Attribute("Name")!).ToList();
        Console.WriteLine($"Fetching {names.Count} entity sets from {sl} ...");
        var xmls = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var queue = new System.Collections.Concurrent.ConcurrentQueue<string>(names);
        var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            while (queue.TryDequeue(out var n))
            {
                try { xmls[n] = await Get($"?scope=entityset&annotation=labelWithField&annotation=labelWithTable&entityset={Uri.EscapeDataString(n)}&dependency=true"); }
                catch (Exception e) { failed.Add($"{n}: {e.Message}"); }
            }
        }));
        var b = new KbBuilder(isUserField, isUserSet);
        foreach (var n in names) b.AddSet(n, EdmModel.Parse(xmls[n]), full);

        // Personal data: ask the native action for every standard table and remember which OPFS rows it serves (see KnowledgeBase.Personal).
        var tables = b.Kb.Sets.Values.Select(s => s.Table).Concat(b.Kb.Blobs.Values.SelectMany(p => p.Values).Select(p => p?.ChildTable))
            .Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).Distinct(StringComparer.Ordinal).ToList();
        var pq = new System.Collections.Concurrent.ConcurrentQueue<string>(tables);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            while (pq.TryDequeue(out var t))
            {
                try
                {
                    using var m = new HttpRequestMessage(HttpMethod.Post, sl + "/b1s/v2/PersonalFieldsSetupsService_GetPersonalFieldsByTable")
                    { Content = new StringContent(JsonSerializer.Serialize(new { PersonalFieldsSetupTableParams = new { TableName = t } }), Encoding.UTF8, "application/json") };
                    m.Headers.TryAddWithoutValidation("Cookie", cookie);
                    using var r = await http.SendAsync(m);
                    if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)r.StatusCode}");
                    var pairs = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("value").EnumerateArray()
                        .Select(x => x.GetProperty("TableName").GetString() + "|" + x.GetProperty("Category").GetString()).Distinct().Order(StringComparer.Ordinal).ToList();
                    if (pairs.Count > 0) lock (b.Kb.Personal) b.Kb.Personal[t] = pairs;
                }
                catch (Exception e) { failed.Add($"personal {t}: {e.Message}"); }
            }
        }));

        try { using var lo = new HttpRequestMessage(HttpMethod.Post, sl + "/b1s/v2/Logout"); lo.Headers.TryAddWithoutValidation("Cookie", cookie); (await http.SendAsync(lo)).Dispose(); } catch { /* best effort */ }
        if (!failed.IsEmpty) { Console.Error.WriteLine("Failed entity sets: " + string.Join("; ", failed.Take(10))); return 3; }

        b.Kb.Sources.Add(new KbSource { ServiceLayer = sl, Version = slVersion, At = DateTimeOffset.UtcNow });

        var leaks = b.FindLeaks();
        Console.WriteLine($"Entity sets in KB: {b.SetsAdded}; excluded entity sets (user tables/objects): {b.ExcludedSets.Count} [{string.Join(", ", b.ExcludedSets)}]");
        Console.WriteLine($"Excluded user-defined fields (CUFD): {b.ExcludedUserFields}");
        Console.WriteLine($"Retained U_* properties (standard columns, not in CUFD): {b.RetainedUProperties.Count}" + (b.RetainedUProperties.Count > 0 ? " [" + string.Join(", ", b.RetainedUProperties.Take(20)) + "]" : ""));
        if (leaks.Count > 0) { Console.Error.WriteLine("LEAK: company-specific entries in the KB: " + string.Join("; ", leaks.Take(20))); return 3; }

        var kb = b.Kb;
        if (mergePath != null && File.Exists(mergePath))
        {
            var existing = KbLoader.ReadFile(mergePath);
            existing.MergeFrom(kb);
            kb = existing;
            Console.WriteLine($"Merged with {mergePath} (union, newest wins).");
        }
        kb.GeneratedAt = DateTimeOffset.UtcNow;
        KbLoader.WriteFile(kb, outPath);
        Console.WriteLine($"KB written: {outPath} ({new FileInfo(outPath).Length:N0} bytes, {kb.Sets.Count} entity sets, {kb.Personal.Count} personal-data tables, {kb.Blobs.Count} type blobs, {kb.Enums.Count} enums). KB entries with U_ names: {kb.Blobs.Values.Sum(x => x.Keys.Count(k => k.StartsWith("U_", StringComparison.Ordinal)))}");
        return 0;
    }
}
