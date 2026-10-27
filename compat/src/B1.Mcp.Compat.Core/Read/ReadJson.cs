using System.Globalization;
using System.Text;
using System.Text.Json;

namespace B1.Mcp.Compat.Core.Read;

/// <summary>A root row as the database returned it (values in the order of <see cref="ReadPlan.Columns"/>) and its child rows by collection.</summary>
public sealed record ReadRow(object?[] Values, IReadOnlyDictionary<string, IReadOnlyList<object?[]>>? Children = null);

/// <summary>
/// Rows to Service Layer JSON (rea/18 2.6 "Conversion a JSON"): properties in EntityType order, NULL to null, enum to its name, a value outside the
/// enum omits the property, date to "yyyy-mm-ddT00:00:00Z", hhmm integer to "HH:MM:00", numeric to a double (15660.0). User fields (U_x) go last.
/// What the package marks as pending (SQL type, a date with a time) is an error, never improvised.
/// </summary>
public static class ReadJson
{
    /// <summary>Whole response body. <paramref name="contextUrl"/> is the "$metadata#Set" url the host builds from the request.</summary>
    public static string Body(ReadPlan plan, ReadPackage pkg, IReadOnlyList<ReadRow> rows, string contextUrl, long? count = null)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            if (plan.InlineCount && count != null) w.WriteNumber("@odata.count", count.Value);
            if (plan.Single)
            {
                if (rows.Count == 0) throw plan.NotFound ?? new ReadException(404, "-2028", "No matching records found (ODBC -2028)");
                w.WriteString("@odata.context", contextUrl + "/$entity");
                WriteRow(w, plan, pkg, rows[0]);
            }
            else
            {
                w.WriteString("@odata.context", contextUrl);
                w.WriteStartArray("value");
                foreach (var r in rows)
                {
                    w.WriteStartObject();
                    WriteRow(w, plan, pkg, r);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                var next = plan.NextLink(rows.Count);
                if (next != null) w.WriteString("@odata.nextLink", next);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>The properties of one root row (the enclosing object is written by the caller of this method).</summary>
    public static void WriteRow(Utf8JsonWriter w, ReadPlan plan, ReadPackage pkg, ReadRow row)
    {
        var order = plan.Set.PropertyOrder ?? throw ReadException.NotMeasured($"JSON of '{plan.SetName}': the package has no EntityType property order (propertyOrder)");
        var cols = plan.Columns.ToList();
        var byProp = new Dictionary<string, int>();
        for (var i = 0; i < cols.Count; i++) if (cols[i].Json != null) byProp[cols[i].Json!.Property] = i;
        var known = new HashSet<string>(order);
        // the EntityType order, then user fields in the order they were read
        foreach (var prop in order.Concat(byProp.Keys.Where(p => !known.Contains(p))))
            if (byProp.TryGetValue(prop, out var i)) WriteValue(w, pkg, cols[i], row.Values[i]);
        if (!plan.IncludeChildren) return;
        foreach (var ch in plan.Set.Children)
        {
            if (ch.Pending != null) throw ReadException.NotMeasured($"child '{ch.Collection}' (pending in the package)");
            var childOrder = ch.PropertyOrder ?? throw ReadException.NotMeasured($"JSON of child '{ch.Collection}': the package has no property order (propertyOrder)");
            w.WriteStartArray(ch.Collection);
            foreach (var cr in row.Children != null && row.Children.TryGetValue(ch.Collection, out var l) ? l : Array.Empty<object?[]>())
            {
                w.WriteStartObject();
                foreach (var prop in childOrder)
                {
                    var i = ch.Columns.FindIndex(c => c.Json?.Property == prop);
                    if (i >= 0) WriteValue(w, pkg, ch.Columns[i], cr[i]);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
    }

    private static void WriteValue(Utf8JsonWriter w, ReadPackage pkg, ReadColumn c, object? v)
    {
        var name = c.Json!.Property;
        if (c.SqlTypePending != null) throw ReadException.NotMeasured($"column '{c.Name}' (SQL type pending in the package: {c.SqlTypePending})");
        if (v == null || v is DBNull) { w.WriteNull(name); return; }
        var inv = CultureInfo.InvariantCulture;
        if (c.Json.Enum != null && pkg.Enums.TryGetValue(c.Json.Enum, out var map))
        {
            if (map.TryGetValue(Convert.ToString(v, inv)!, out var en)) w.WriteString(name, en);
            return;   // a value outside the enum: the property is omitted
        }
        if (c.Json.EdmType == "Edm.TimeOfDay")
        {
            var hm = Convert.ToInt32(v, inv);
            if (hm < 0 || hm / 100 > 23 || hm % 100 > 59) throw ReadException.NotMeasured($"time value {hm} in '{name}'");
            w.WriteString(name, $"{hm / 100:00}:{hm % 100:00}:00");
            return;
        }
        switch (c.BaseType)
        {
            case "nvarchar" or "char" or "varchar" or "ntext": w.WriteString(name, Convert.ToString(v, inv)); break;
            case "int" or "smallint": w.WriteNumber(name, Convert.ToInt64(v, inv)); break;
            case "numeric" or "decimal":
            {
                var d = Convert.ToDouble(v, inv);
                var s = d.ToString("R", inv);
                w.WritePropertyName(name);
                w.WriteRawValue(s.Contains('.') || s.Contains('E') ? s : s + ".0");   // jsoncpp style: 15660.0
                break;
            }
            case "datetime":
            {
                var dt = v is DateTimeOffset o ? o.DateTime : Convert.ToDateTime(v, inv);
                // DBMCdataBase::DataToDAG (case 'D') formats only year/month/day of the ODBC timestamp ("%4.4d%2.2d%2.2d"): the time is dropped
                w.WriteString(name, dt.ToString("yyyy-MM-dd", inv) + "T00:00:00Z");
                break;
            }
            default: throw ReadException.NotMeasured($"JSON of a {c.SqlType} column ('{name}')");
        }
    }
}
