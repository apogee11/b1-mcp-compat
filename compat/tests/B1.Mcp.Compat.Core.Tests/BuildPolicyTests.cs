using System.IO.Compression;
using System.Text.Json;
using Xunit;
using B1.Mcp.Compat.Core.Config;
using B1.Mcp.Compat.Core.Kb;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>Build policy: release and build are declared by the administrator, never detected (rea/09, ADR 0010).</summary>
public class BuildPolicyTests
{
    private static KnowledgeBase Kb(string fixedVersion, string ob, string en, string rs) => new()
    {
        Build = new KbBuild
        {
            Files =
            {
                new KbBuildFile { Name = "OBServerDLL.dll", FileVersionFixed = fixedVersion, Sha256 = ob + new string('0', 52) },
                new KbBuildFile { Name = "B1_Engine.dll", FileVersionFixed = fixedVersion, Sha256 = en + new string('0', 52) },
                new KbBuildFile { Name = "B1_EnginesResources.dll", FileVersionFixed = fixedVersion, Sha256 = rs + new string('0', 52) },
            },
        },
    };

    private static readonly Dictionary<string, KnowledgeBase> Kbs = new()
    {
        ["2408"] = Kb("10.0.260.0", "aaaaaaaaaaaa", "bbbbbbbbbbbb", "cccccccccccc"),
        ["2608"] = Kb("10.0.340.0", "111111111111", "222222222222", "333333333333"),
    };

    private static readonly Dictionary<string, string> Codes = new() { ["1000260"] = "2408", ["1000340"] = "2608", ["1000200"] = "2208" };
    private const string Good2608 = "10.0.340.0-111111111111-222222222222-333333333333";
    private const string Other2608 = "10.0.340.0-111111111111-222222222222-444444444444";

    private static BuildPolicy P(string? release, string? build, bool allow = false) => new(Kbs, Codes, release, build, allow);

    [Fact]
    public void BuildId_is_file_version_plus_three_hash_prefixes() => Assert.Equal(Good2608, BuildPolicy.BuildIdOf(Kbs["2608"]));

    [Theory]
    [InlineData("2608")]
    [InlineData("FP2608")]
    [InlineData("fp2608")]
    public void Valid_declaration_is_served_as_verified(string release)
    {
        var d = P(release, Good2608.ToUpperInvariant()).Resolve("1000340");
        Assert.True(d.Ok);
        Assert.Equal("2608", d.Release);
        Assert.True(d.Verified);
        Assert.Null(d.Warning);
    }

    [Fact]
    public void Release_not_declared_is_taken_from_the_login_version()
    {
        var d = P(null, Good2608).Resolve("1000340");
        Assert.Equal("2608", d.Release);
        Assert.True(d.Verified);
    }

    [Theory]
    [InlineData("26", null)]
    [InlineData("FP26080", null)]
    [InlineData("abcd", null)]
    [InlineData("2608", "10.0.340.0")]
    [InlineData("2608", "10.0.340.0-zzzzzzzzzzzz-222222222222-333333333333")]
    [InlineData("2608", "d02489c511c7")]
    public void Invalid_format_is_rejected_at_start(string release, string? build) =>
        Assert.Throws<InvalidOperationException>(() => P(release, build));

    [Theory]
    [InlineData(Other2608)] // another patch: one hash differs
    [InlineData(null)]      // build not declared
    public void Unsupported_build_without_permission_is_an_error_with_the_fixed_message(string? build)
    {
        var d = P("2608", build).Resolve("1000340");
        Assert.False(d.Ok);
        Assert.Equal(BuildPolicy.UnsupportedMessage, d.Error);
    }

    [Fact]
    public void Unsupported_build_with_permission_uses_the_kb_of_the_release_and_warns()
    {
        var d = P("2608", Other2608, allow: true).Resolve("1000340");
        Assert.True(d.Ok);
        Assert.Equal("2608", d.Release);
        Assert.False(d.Verified);
        Assert.Contains(Other2608, d.Warning);
        Assert.Contains(Good2608, d.Warning);
        Assert.Contains("NOT verified", d.Warning);
    }

    [Fact]
    public void Undeclared_build_with_permission_warns_that_none_was_declared()
    {
        var d = P("2608", null, allow: true).Resolve("1000340");
        Assert.True(d.Ok);
        Assert.Contains("none declared", d.Warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Release_without_any_kb_is_always_an_error(bool allow)
    {
        Assert.Equal(BuildPolicy.UnsupportedMessage, P("2502", "10.0.400.0-111111111111-222222222222-333333333333", allow).Resolve("1000400").Error);
        Assert.Equal(BuildPolicy.UnsupportedMessage, P(null, null, allow).Resolve("1000400").Error);
        Assert.Equal(BuildPolicy.UnsupportedMessage, P("2208", null, allow).Resolve("1000200").Error); // known code, no kb in this table
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Login_version_of_another_release_is_always_an_error(bool allow)
    {
        var d = P("2608", Good2608, allow).Resolve("1000260");
        Assert.False(d.Ok);
        Assert.Contains("Compat__Release is 2608", d.Error);
        Assert.NotEqual(BuildPolicy.UnsupportedMessage, d.Error);
        Assert.False(P("2608", Good2608, allow).Resolve("1000999").Ok);
        Assert.False(P("2608", Good2608, allow).Resolve(null).Ok);
    }

    [Fact]
    public void Build_of_another_release_is_a_contradiction_even_with_permission()
    {
        var d = P("2608", BuildPolicy.BuildIdOf(Kbs["2408"]), allow: true).Resolve("1000340");
        Assert.False(d.Ok);
        Assert.Contains("belongs to release 2408", d.Error);
    }

    [Fact]
    public void Fixed_message_text_is_exactly_this()
    {
        Assert.Equal(
            "b1-mcp-compat: this Service Layer release or build is not supported: there is no knowledge base for it. " +
            "Contact the maintainer of this repository and send the output of tools/collect/check-build.ps1 (file versions and SHA-256 hashes) so that the knowledge base for your build is generated. " +
            "To keep working meanwhile with the knowledge base of the same release, without verification, set Compat__AllowUnsupportedBuild=true " +
            "(this does not help for a release that has no knowledge base at all).",
            BuildPolicy.UnsupportedMessage);
    }

    /// <summary>The embedded knowledge bases must agree with docs/covered-builds.json: the id the script prints is the id the connector expects.</summary>
    [Fact]
    public void Embedded_kbs_have_the_build_ids_of_covered_builds()
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "docs", "covered-builds.json"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, "docs", "covered-builds.json")));
        foreach (var b in table.RootElement.GetProperty("builds").EnumerateArray())
        {
            var release = b.GetProperty("release").GetString()![2..];
            using var gz = new GZipStream(File.OpenRead(Path.Combine(root!, "compat", "src", "B1.Mcp.Compat.Host", "Resources", $"kb-{release}.json.gz")), CompressionMode.Decompress);
            var id = BuildPolicy.BuildIdOf(KnowledgeBase.Load(gz));
            var f = b.GetProperty("files").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
            string Sha(string n) => f[n].GetProperty("sha256").GetString()![..12];
            Assert.Equal($"{f["OBServerDLL.dll"].GetProperty("fileVersion").GetString()}-{Sha("OBServerDLL.dll")}-{Sha("B1_Engine.dll")}-{Sha("B1_EnginesResources.dll")}", id);
        }
    }
}
