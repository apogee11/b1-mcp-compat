namespace B1.Mcp.Compat.Core.Metadata;

/// <summary>
/// The FP2608 options of GET /b1s/v2/$metadata:
/// scope=entityset, annotation=labelWithTable|labelWithField (repeatable or comma separated), entityset=NAME, dependency=true.
/// </summary>
public sealed record MetadataQuery(bool Scoped, bool Label, bool WithField, bool WithTable, string? EntitySet, bool Dependency)
{
    public static MetadataQuery Parse(Func<string, IEnumerable<string>> values)
    {
        var scoped = values("scope").Any(v => v == "entityset");
        var anns = values("annotation").SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        var entity = values("entityset").FirstOrDefault();
        var dep = values("dependency").Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
        return new MetadataQuery(scoped,
            anns.Any(a => a.StartsWith("label", StringComparison.Ordinal)),
            anns.Contains("labelWithField"),
            anns.Contains("labelWithTable"),
            string.IsNullOrEmpty(entity) ? null : entity,
            dep);
    }
}
