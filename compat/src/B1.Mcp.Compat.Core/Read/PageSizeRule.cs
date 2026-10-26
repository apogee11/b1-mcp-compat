using System.Globalization;
using System.Text.RegularExpressions;

namespace B1.Mcp.Compat.Core.Read;

/// <summary>
/// Page size of a read, as the native Service Layer FP2608 answers it (measured by GET on the local 2608, rea/01-findings 2026-10-27).
/// Order: the Prefer header (odata.maxpagesize) over the B1S-PageSize header over the configured PageSize (b1s.conf of the Service Layer, default 20).
/// Zero or less in B1S-PageSize, or zero in Prefer, means no paging (every row, no nextLink).
/// </summary>
public static partial class PageSizeRule
{
    /// <summary>Stands for "no paging": the page is never full, so no nextLink is written.</summary>
    public const int Unlimited = int.MaxValue;

    // the native parser only knows a Prefer header that is a single odata.maxpagesize preference: case sensitive, no comma anywhere,
    // spaces around '=' allowed, trailing text after the number ignored, a non-number reads as 0
    [GeneratedRegex(@"^odata\.maxpagesize\s*=\s*([+-]?\d*)")] private static partial Regex PreferRx();
    [GeneratedRegex(@"^\s*([+-]?\d+)")] private static partial Regex LeadingInt();

    /// <param name="prefer">Value of the Prefer header or null.</param>
    /// <param name="b1sPageSize">Value of the B1S-PageSize header or null.</param>
    /// <param name="configured">PageSize the administrator declares (mirror of b1s.conf); 20 when the Service Layer has none.</param>
    /// <exception cref="ReadException">B1S-PageSize without a number (native: 500, code 402).</exception>
    public static int Resolve(string? prefer, string? b1sPageSize, int configured)
    {
        if (prefer != null && !prefer.Contains(',') && PreferRx().Match(prefer.Trim()) is { Success: true } m)
        {
            var n = m.Groups[1].Value.Length == 0 ? 0 : Clamp(m.Groups[1].Value);
            if (n == 0) return Unlimited;
            if (n > 0) return n;
            // negative: native answers the default page size (measured with no other source; with B1S-PageSize present it is not measured and falls through to it)
        }
        if (b1sPageSize != null)
        {
            var m2 = LeadingInt().Match(b1sPageSize);
            if (!m2.Success) throw new ReadException(500, "402", $"The value('{b1sPageSize}') of parameter 'B1S-PageSize' is invalid.");
            var n = Clamp(m2.Groups[1].Value);
            return n <= 0 ? Unlimited : n;
        }
        return configured > 0 ? configured : Unlimited;
    }

    private static int Clamp(string digits) =>
        long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? (int)Math.Clamp(v, int.MinValue, int.MaxValue) : int.MaxValue;
}
