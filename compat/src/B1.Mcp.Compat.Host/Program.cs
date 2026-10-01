using System.Net;
using System.Reflection;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Data;
using B1.Mcp.Compat.Host;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// Commands: serve (default) | version.  Seams for later passes: verify (doctor), secrets (DPAPI), service install (WinSW).
var command = args.Length > 0 && !args[0].StartsWith('-') && !args[0].Contains('=') ? args[0].ToLowerInvariant() : "serve";
var rest = command == "serve" && !(args.Length > 0 && args[0].Equals("serve", StringComparison.OrdinalIgnoreCase)) ? args : args.Skip(1).ToArray();

switch (command)
{
    case "serve": return await Serve(rest);
    case "version": return Version();
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Usage: B1.Mcp.Compat.Host [serve|version] [options]");
        return 2;
}

static int Version()
{
    var v = typeof(CompatOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    Console.WriteLine($"b1-mcp-compat {v.Split('+')[0]}");
    var kbs = KbLoader.Load(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    if (kbs.Count == 0) Console.WriteLine("knowledge base: none embedded");
    foreach (var (release, kb) in kbs.OrderBy(k => k.Key)) Console.WriteLine($"knowledge base FP{release}: {kb.Sets.Count} entity sets, {kb.Enums.Count} enums, generated {kb.GeneratedAt:u}");
    return 0;
}

static async Task<int> Serve(string[] args)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
    builder.Logging.ClearProviders().AddSimpleConsole(c => { c.SingleLine = true; c.TimestampFormat = "HH:mm:ss.fff "; }).AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    builder.Host.UseWindowsService(); // no-op when run from a console
    var options = builder.Configuration.GetSection(CompatOptions.Section).Get<CompatOptions>() ?? new CompatOptions();
    var listen = new Uri(options.Listen);
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.Limits.MaxRequestBodySize = null;
        var ip = listen.Host switch
        {
            "*" or "+" or "0.0.0.0" => IPAddress.Any,
            "localhost" => IPAddress.Loopback,
            var h => IPAddress.Parse(h),
        };
        k.Listen(ip, listen.Port, lo =>
        {
            if (listen.Scheme == Uri.UriSchemeHttps) lo.UseHttps(SelfSigned.Create(options.Tls));
            lo.Protocols = HttpProtocols.Http1;
        });
    });
    builder.Services.AddSingleton(options);
    var app = builder.Build();

    var log = app.Logger;
    var kbs = KbLoader.Load(log);
    var overrides = KbLoader.LoadMappings(options, log);
    var db = CompanyDatabaseFactory.Create(options.Database);
    var proxy = new ProxyEndpoint(options, Http.CreateUpstreamClient(options), kbs, overrides, db, log);
    log.LogInformation("b1-mcp-compat on {Listen} -> {Upstream} (db provider {Provider}, knowledge bases {Kbs}{Sim})",
        options.Listen, options.Upstream.Url, options.Database.Provider, string.Join(", ", kbs.Keys.Order().Select(k => "FP" + k)), options.SimulateLegacy ? ", SIMULATE LEGACY" : "");
    if (listen.Scheme == Uri.UriSchemeHttp) log.LogWarning("Serving plain HTTP. The Service Layer session cookie travels unprotected: use only on a trusted loopback/LAN.");

    app.Run(proxy.HandleAsync);
    await app.RunAsync();
    return 0;
}
