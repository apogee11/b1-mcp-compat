using System.Globalization;
using B1.Mcp.Compat.Core.Catalog;

namespace B1.Mcp.Compat.Core.Read;

public enum ReadAction { Passthrough, Serve, Reject }

/// <summary>What the proxy does with one request: pass it to the Service Layer untouched, serve it from the package, or refuse it.</summary>
public sealed record ReadRouteResult(ReadAction Action, ReadException? Error = null);

/// <summary>
/// Whether a request is ours. Only /b1s/v2 and only a set of the package that the Service Layer does not expose itself under the exact name asked
/// (each alias is judged on its own name): what the Service Layer serves is never intercepted, and without a package nothing is.
/// </summary>
public static class ReadRoute
{
    private const string Prefix = "/b1s/v2/";

    /// <summary>"/b1s/v2/Foo(1)/$count" to "Foo"; null when the path is not a /b1s/v2 resource (v1 included).</summary>
    public static string? SetNameOf(string path)
    {
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = path[Prefix.Length..];
        var end = rest.IndexOfAny(new[] { '(', '/' });
        var name = end < 0 ? rest : rest[..end];
        return ReadPackage.IsSafeIdentifier(name) ? name : null;
    }

    /// <param name="slExposes">True when the Service Layer of the session lists that entity set in its $metadata.</param>
    public static ReadRouteResult Decide(ReadPackage? package, Func<string, bool> slExposes, string method, string path)
    {
        var pass = new ReadRouteResult(ReadAction.Passthrough);
        var name = SetNameOf(path);
        if (package == null || name == null) return pass;
        var r = package.Resolve(name);
        if (r.Set == null && r.Unavailable == null) return pass;
        if (slExposes(name)) return pass;   // by the exact name asked: DeductibleTaxs may be the SL's own while DeductibleTaxes is not
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)) return new(ReadAction.Serve);
        var refused = ReadGate.Decide(package, method, name);
        return refused == null ? pass : new(ReadAction.Reject, refused);
    }
}

/// <summary>The answer of the read runtime.</summary>
public sealed record ReadResult(int Status, string ContentType, string Body);

/// <summary>Plans a GET with <see cref="ReadPlanner"/>, runs it on the company database and writes the answer with <see cref="ReadJson"/>. A <see cref="ReadException"/> becomes its error answer.</summary>
public static class ReadService
{
    public const string JsonType = "application/json;odata.metadata=minimal;charset=utf-8";

    /// <param name="pathAndQuery">Relative to /b1s/v2 ("Set(key)?$select=..").</param>
    /// <param name="serviceUrl">"https://host/b1s/v2" as the client sees it (context urls).</param>
    public static async Task<ReadResult> GetAsync(ReadPackage package, ICompanyDatabase db, string company, string pathAndQuery, string serviceUrl, int pageSize, CancellationToken ct)
    {
        try
        {
            // user fields (CUFD) of the tables this request reads: the set table and its children
            var name = ReadRoute.SetNameOf("/b1s/v2/" + (pathAndQuery.IndexOf('?') < 0 ? pathAndQuery : pathAndQuery[..pathAndQuery.IndexOf('?')]));
            var set = name == null ? null : package.Resolve(name).Set;
            var tables = set == null ? new List<string>() : new[] { set.Table }.Concat(set.Children.Select(c => c.Table)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var udf = tables.Count == 0 ? Array.Empty<UserFieldRow>() : await db.LoadUserFieldsAsync(company, tables, ct);
            var plan = ReadPlanner.Plan(package, pathAndQuery, pageSize, t => UserFields.ColumnsOf(udf, t));
            long? count = null;
            if (plan.Counts.Count > 0)   // the engine runs both COUNT statements; the first one is the answer
                count = Convert.ToInt64((await db.ReadRowsAsync(company, plan.Counts[0], 1, ct))[0][0], CultureInfo.InvariantCulture);
            if (plan.CountOnly) return new(200, "text/plain;charset=utf-8", count!.Value.ToString(CultureInfo.InvariantCulture));
            var rows = new List<ReadRow>();
            foreach (var values in await db.ReadRowsAsync(company, plan.Main!, plan.Columns.Count, ct))
            {
                Dictionary<string, IReadOnlyList<object?[]>>? kids = null;
                if (plan.IncludeChildren)
                {
                    kids = new();
                    foreach (var child in plan.Set.Children)
                        kids[child.Collection] = await db.ReadRowsAsync(company, plan.ChildQuery(child, values), child.Columns.Count, ct);
                }
                rows.Add(new ReadRow(values, kids));
            }
            return new(200, JsonType, ReadJson.Body(plan, package, rows, $"{serviceUrl}/$metadata#{plan.SetName}", count));
        }
        catch (ReadException e)
        {
            return new(e.Status, "application/json;charset=utf-8", e.ToJson());
        }
    }
}
