// usage: ReadDump <read-package.json.gz> <company> <serviceUrl> [sqlServer]   (env: DB_USER, DB_PASS)
// stdin: one JSON object per line {"id":..,"path":"Set(1)?$top=1","pageSize":20}; stdout: {"id","status","contentType","body"} per line.
using System.Text.Json;
using B1.Mcp.Compat.Core.Config;
using B1.Mcp.Compat.Core.Read;
using B1.Mcp.Compat.Data;

var package = ReadPackage.LoadGz(File.OpenRead(args[0]));
var db = CompanyDatabaseFactory.Create(new DatabaseOptions
{
    Server = args.Length > 3 ? args[3] : "localhost",
    User = Environment.GetEnvironmentVariable("DB_USER") ?? "",
    Password = Environment.GetEnvironmentVariable("DB_PASS") ?? "",
});
var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
string? line;
while ((line = Console.In.ReadLine()) != null)
{
    if (line.Length == 0) continue;
    using var req = JsonDocument.Parse(line);
    var id = req.RootElement.GetProperty("id").GetString();
    int status; string type, body;
    try
    {
        var r = await ReadService.GetAsync(package, db, args[1], req.RootElement.GetProperty("path").GetString()!, args[2],
            req.RootElement.GetProperty("pageSize").GetInt32(), CancellationToken.None);
        (status, type, body) = (r.Status, r.ContentType, r.Body);
    }
    catch (Exception e) { (status, type, body) = (-1, "exception", e.GetType().Name + ": " + e.Message); }
    stdout.WriteLine(JsonSerializer.Serialize(new { id, status, contentType = type, body }));
}
