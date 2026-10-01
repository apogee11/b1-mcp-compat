using B1.Mcp.Compat.Core.Config;

namespace B1.Mcp.Compat.Host;

/// <summary>Configuration section "Compat", read from environment variables (Compat__Upstream__Url, ...) and command-line arguments. There is no settings file.</summary>
public sealed class CompatOptions
{
    public const string Section = "Compat";

    /// <summary>Where the connector listens. https:// uses the certificate below or an auto-generated self-signed one.</summary>
    public string Listen { get; set; } = "https://localhost:50100";

    public UpstreamOptions Upstream { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public MappingsOptions Mappings { get; set; } = new();
    public TlsOptions Tls { get; set; } = new();

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

    public sealed class TlsOptions
    {
        /// <summary>PFX file for https. If it does not exist a self-signed certificate is generated and saved there; if empty an in-memory self-signed certificate is created at each start.</summary>
        public string? CertificatePath { get; set; }

        public string? CertificatePassword { get; set; }
    }

    public static string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
