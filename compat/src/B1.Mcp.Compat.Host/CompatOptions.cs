using B1.Mcp.Compat.Core.Config;

namespace B1.Mcp.Compat.Host;

/// <summary>Configuration section "Compat", read from environment variables (B1McpCompat__ServiceLayer__Url, ...) and command-line arguments. There is no settings file.</summary>
public sealed class CompatOptions
{
    public const string Section = "B1McpCompat";

    /// <summary>Where the connector listens. https:// requires the PFX certificate below, provided by the administrator.</summary>
    public string Listen { get; set; } = "http://127.0.0.1:50300";

    /// <summary>Release of the Service Layer the connector points to, as declared by the administrator (2208, 2408, 2608; "FP" prefix allowed). Empty: the release of the Login Version.</summary>
    public string Release { get; set; } = "";

    /// <summary>Build of that Service Layer, as printed by tools/collect/check-build.ps1 (e.g. 10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194). The Service Layer cannot tell it, so it is declared. Empty: no build declared (treated as an unsupported build).</summary>
    public string Build { get; set; } = "";

    /// <summary>false (default): a build without knowledge base is an error with a fixed message. true: serve it with the knowledge base of the same release, log a warning, do not claim verification.</summary>
    public bool AllowUnsupportedBuild { get; set; }

    /// <summary>Page size of an extended read when the request does not ask for one. Mirror of the PageSize key of the Service Layer's b1s.conf (the connector cannot read that file: the Service Layer is another machine). 20 is the native default when the key is absent; 0 or less: no paging.</summary>
    public int PageSize { get; set; } = 20;

    public ServiceLayerOptions ServiceLayer { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public MappingsOptions Mappings { get; set; } = new();
    public TlsOptions Tls { get; set; } = new();
    public LogOptions Log { get; set; } = new();

    /// <summary>TEST ONLY: make a native FP2608+ Service Layer behave like an older one (annotated $metadata options and the personal data service are hidden from it).</summary>
    public bool SimulateLegacy { get; set; }

    public sealed class ServiceLayerOptions
    {
        public string Url { get; set; } = "https://localhost:50000";

        /// <summary>Service Layers ship self-signed certificates, hence true by default.</summary>
        public bool SkipTlsVerify { get; set; } = true;
    }

    public sealed class MappingsOptions
    {
        public string OverridePath { get; set; } = "mappings.override.json";
    }

    public sealed class LogOptions
    {
        /// <summary>Folder of the log files. Empty: next to the exe.</summary>
        public string? Path { get; set; }

        /// <summary>"info" (everything, default) or "error".</summary>
        public string Level { get; set; } = "info";

        /// <summary>Days a daily log file is kept before it is deleted.</summary>
        public int RetainDays { get; set; } = 14;
    }

    public sealed class TlsOptions
    {
        /// <summary>PFX file for https (required when Listen is https://). The connector never generates a certificate.</summary>
        public string? CertificatePath { get; set; }

        public string? CertificatePassword { get; set; }
    }

    public static string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
