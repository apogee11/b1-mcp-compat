using System.Text.RegularExpressions;
using B1.Mcp.Compat.Core.Kb;

namespace B1.Mcp.Compat.Core.Config;

/// <summary>Outcome of <see cref="BuildPolicy.Resolve"/>: either the package (key of the knowledge base, e.g. "B11000SP_2408") whose knowledge base serves the session, or an error message.</summary>
public sealed record BuildDecision(string? Release, string? Error, string? Warning, bool Verified)
{
    public bool Ok => Release != null;
}

/// <summary>
/// Which knowledge base serves a session. Nothing is detected: the administrator declares the release (B1McpCompat:Release) and the
/// build (B1McpCompat:Build) of the Service Layer, and the connector only compares those declarations with what each knowledge base
/// was extracted from (rea/09: no heuristic; ADR 0010).
/// </summary>
public sealed partial class BuildPolicy
{
    /// <summary>Fixed answer (always the same text) for a release or build without a knowledge base.</summary>
    public const string UnsupportedMessage =
        "b1-mcp-compat: this Service Layer release or build is not supported: there is no knowledge base for it. " +
        "Contact the maintainer of this repository and send the output of tools/collect/check-build.ps1 (file versions and SHA-256 hashes) so that the knowledge base for your build is generated. " +
        "To keep working meanwhile with the knowledge base of the same release, without verification, set B1McpCompat__AllowUnsupportedBuild=true " +
        "(this does not help for a release that has no knowledge base at all).";

    [GeneratedRegex(@"^(?:FP)?(\d{4})$", RegexOptions.IgnoreCase)] private static partial Regex ReleaseFormat();
    [GeneratedRegex(@"^\d+\.\d+\.\d+\.\d+-[0-9a-f]{12}-[0-9a-f]{12}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)] private static partial Regex BuildFormat();

    private readonly IReadOnlyDictionary<string, KnowledgeBase> _kbs;
    private readonly IReadOnlyDictionary<string, string> _codeToRelease;
    private readonly bool _allow;

    /// <summary>Declared release ("2608"), or null when not declared (then the release is the one of the Login Version).</summary>
    public string? Release { get; }

    /// <summary>Declared build id (lower case), or null when not declared.</summary>
    public string? Build { get; }

    /// <param name="kbs">Package ("B11000_2608", "B110002608HF_1"; a bare release such as "2608" also works) to its knowledge base. The release of a package is the first four-digit group of its name.</param>
    /// <param name="codeToRelease">Login Version code ("1000340") to release.</param>
    /// <exception cref="InvalidOperationException">The declared release or build has an invalid format.</exception>
    public BuildPolicy(IReadOnlyDictionary<string, KnowledgeBase> kbs, IReadOnlyDictionary<string, string> codeToRelease, string? release, string? build, bool allowUnsupportedBuild)
    {
        _kbs = kbs;
        _codeToRelease = codeToRelease;
        _allow = allowUnsupportedBuild;
        if (!string.IsNullOrWhiteSpace(release))
        {
            var m = ReleaseFormat().Match(release.Trim());
            if (!m.Success) throw new InvalidOperationException($"B1McpCompat__Release '{release}' is not valid: use the four digits of the release, e.g. 2608 (or FP2608).");
            Release = m.Groups[1].Value;
        }
        if (!string.IsNullOrWhiteSpace(build))
        {
            if (!BuildFormat().IsMatch(build.Trim())) throw new InvalidOperationException($"B1McpCompat__Build '{build}' is not valid: copy the value printed by tools/collect/check-build.ps1, e.g. 10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194.");
            Build = build.Trim().ToLowerInvariant();
        }
    }

    /// <summary>The build id of a knowledge base: the exact-build identifier. Null if the knowledge base lacks the data to compute it.</summary>
    public static string? BuildIdOf(KnowledgeBase kb)
    {
        KbBuildFile? F(string n) => kb.Build?.Files.FirstOrDefault(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        var ob = F("OBServerDLL.dll");
        var en = F("B1_Engine.dll");
        var rs = F("B1_EnginesResources.dll");
        if (ob?.FileVersionFixed == null || ob.Sha256 is not { Length: >= 12 } || en?.Sha256 is not { Length: >= 12 } || rs?.Sha256 is not { Length: >= 12 }) return null;
        return $"{ob.FileVersionFixed}-{ob.Sha256[..12]}-{en.Sha256[..12]}-{rs.Sha256[..12]}".ToLowerInvariant();
    }

    /// <summary>Release ("2608") of a package key: <c>B11000_2608</c> and <c>B11000SP_2608</c> end with it, <c>B110002608HF_1</c> carries it before "HF"; a bare release is itself.</summary>
    public static string ReleaseOf(string package) => ReleaseInName().Match(package) is { Success: true } m ? m.Groups["r"].Value : package;

    [GeneratedRegex(@"^(?<r>\d{4})$|_(?<r>\d{4})$|(?<r>\d{4})HF")] private static partial Regex ReleaseInName();

    /// <summary>Decision for the session whose Login answered <paramref name="loginCode"/> (may be null when unknown).</summary>
    public BuildDecision Resolve(string? loginCode)
    {
        string? loginRelease = loginCode != null && _codeToRelease.TryGetValue(loginCode, out var lr) ? lr : null;
        var release = Release ?? loginRelease;
        var candidates = release == null ? [] : _kbs.Where(k => ReleaseOf(k.Key) == release).OrderBy(k => k.Key.Contains("HF", StringComparison.Ordinal)).ThenBy(k => k.Key, StringComparer.Ordinal).ToList();
        if (candidates.Count == 0) return new BuildDecision(null, UnsupportedMessage, null, false);

        // The Login Version must belong to the declared release. Refused always: serving another release's dictionary is wrong in known places.
        if (Release != null && loginRelease != Release)
            return new BuildDecision(null, $"b1-mcp-compat: the Service Layer reports Version '{loginCode ?? "unknown"}'{(loginRelease != null ? $" (release {loginRelease})" : "")} but B1McpCompat__Release is {Release}. Declare the release and build of the installation this connector points to (tools/collect/check-build.ps1 prints them).", null, false);

        // Exact build: the package whose files are the ones declared (a hotfix is its own package).
        if (Build != null)
        {
            var exact = candidates.FirstOrDefault(k => BuildIdOf(k.Value) == Build);
            if (exact.Key != null) return new BuildDecision(exact.Key, null, null, true);
        }

        // A build declared for another release's knowledge base is a contradiction, not a patch: never served.
        var other = Build == null ? null : _kbs.FirstOrDefault(k => ReleaseOf(k.Key) != release && BuildIdOf(k.Value) == Build).Key;
        if (other != null)
            return new BuildDecision(null, $"b1-mcp-compat: B1McpCompat__Build {Build} belongs to release {ReleaseOf(other)} but B1McpCompat__Release / the Login Version is {release}. Fix the declaration.", null, false);

        if (!_allow) return new BuildDecision(null, UnsupportedMessage, null, false);
        var (key, kb) = candidates[0];
        return new BuildDecision(key, null,
            $"Unsupported build served with the knowledge base {key}: declared build {(Build ?? "(none declared)")}, knowledge base build {BuildIdOf(kb) ?? "(no fingerprint)"}. " +
            "This build is NOT supported; annotations can be wrong where the build's dictionary differs. B1McpCompat__AllowUnsupportedBuild=true is set.", false);
    }
}
