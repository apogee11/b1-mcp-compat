using System.Text.Json;

namespace B1.Mcp.Compat.Core.Read;

/// <summary>An error the read runtime answers with (status + Service Layer style code and message). Never a crash.</summary>
public sealed class ReadException : Exception
{
    public int Status { get; }
    public string Code { get; }

    public ReadException(int status, string code, string message) : base(message)
    {
        Status = status;
        Code = code;
    }

    /// <summary>Syntax or option error, native format: 400 code 201.</summary>
    public static ReadException Query(string message) => new(400, "201", message);

    /// <summary>Behaviour with no measured rule (rea/18 2.6): refused instead of guessed.</summary>
    public static ReadException NotMeasured(string what) =>
        new(501, "not-implemented", $"Extended read: {what} has no measured rule yet and is not served.");

    /// <summary>Service Layer error body.</summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        error = new { code = Code, details = new[] { new { code = "", message = "" } }, message = Message },
    });
}

/// <summary>Whether a request on a set of the package is served (pure; the host calls it before anything else).</summary>
public static class ReadGate
{
    /// <summary>Null = allowed (a read) or not a set of the package. Otherwise the error to answer with.</summary>
    public static ReadException? Decide(ReadPackage package, string method, string setName)
    {
        var r = package.Resolve(setName);
        if (r.Set == null && r.Unavailable == null) return null;   // not ours
        if (r.Unavailable != null) return package.UnavailableError(r.Unavailable);
        return method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            ? null
            : new ReadException(405, "405", $"'{setName}' is read only in this connector: it does not exist natively in this version of SAP Business One and {method.ToUpperInvariant()} is not supported.");
    }
}
