using B1.Mcp.Compat.Core.Config;

namespace B1.Mcp.Compat.Host;

/// <summary>Configuration section "Compat", read from environment variables (B1McpCompat__Upstream__Url, ...) and command-line arguments. There is no settings file.</summary>
public sealed class CompatOptions
{
    public const string Section = "B1McpCompat";

    /// <summary>Where the connector listens. https:// uses the certificate below or an auto-generated self-signed one.</summary>
    public string Listen { get; set; } = "https://localhost:50100";

    /// <summary>Release of the Service Layer the connector points to, as declared by the administrator (2208, 2408, 2608; "FP" prefix allowed). Empty: the release of the Login Version.</summary>
    public string Release { get; set; } = "";

    /// <summary>Build of that Service Layer, as printed by tools/collect/check-build.ps1 (e.g. 10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194). The Service Layer cannot tell it, so it is declared. Empty: no build declared (treated as an unsupported build).</summary>
    public string Build { get; set; } = "";

    /// <summary>false (default): a build without knowledge base is an error with a fixed message. true: serve it with the knowledge base of the same release, log a warning, do not claim verification.</summary>
    public bool AllowUnsupportedBuild { get; set; }

    public UpstreamOptions Upstream { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public MappingsOptions Mappings { get; set; } = new();
    public TlsOptions Tls { get; set; } = new();
    public LogOptions Log { get; set; } = new();

    /// <summary>TEST ONLY: make a native FP2608+ Service Layer behave like an older one (annotated $metadata options and the personal data service are hidden from it).</summary>
    public bool SimulateLegacy { get; set; }

    public sealed class UpstreamOptions
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
        /// <summary>PFX file for https. If it does not exist a self-signed certificate is generated and saved there; if empty an in-memory self-signed certificate is created at each start.</summary>
        public string? CertificatePath { get; set; }

        public string? CertificatePassword { get; set; }
    }

    public static string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
