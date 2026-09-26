# Reads the company-database catalog the connector depends on. SELECT only; no business data is read.
# Output contains table/field NAMES (including the customer's user-defined ones), never row data from business tables.
#   .\02-database.ps1 -SqlServer SQLHOST -Database SBODEMO -OutDir .\out\db                 (Windows identity)
#   .\02-database.ps1 -SqlServer SQLHOST -Database SBODEMO -SqlUser reader -OutDir .\out\db   (SQL login; password: env B1_SQL_PASSWORD or prompt)
# SQL Server only. SAP HANA is experimental in the project and not covered by this probe.
param(
  [Parameter(Mandatory)][string]$SqlServer, [Parameter(Mandatory)][string]$Database,
  [string]$SqlUser = '', [string]$OutDir = '.\out\db'
)
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
$OutDir = New-OutDir $OutDir
$cs = "Server=$SqlServer;Database=$Database;Application Name=b1-mcp-compat-probe;Connect Timeout=15;TrustServerCertificate=True;"
if ($SqlUser) { $cs += "User Id=$SqlUser;Password=$(Get-PlainPassword 'B1_SQL_PASSWORD' "SQL password for $SqlUser");" } else { $cs += 'Integrated Security=SSPI;' }
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
try { $cn.Open() } catch { Write-Result FAIL "Cannot connect to $SqlServer/$Database : $($_.Exception.Message)"; exit 2 }

function Q([string]$sql) {
  $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 60
  $dt = New-Object System.Data.DataTable; (New-Object System.Data.SqlClient.SqlDataAdapter $cmd).Fill($dt) | Out-Null
  $rows = @(); foreach ($r in $dt.Rows) { $o = [ordered]@{}; foreach ($c in $dt.Columns) { $v = $r[$c.ColumnName]; $o[$c.ColumnName] = $(if ($v -is [DBNull]) { $null } else { $v }) }; $rows += [pscustomobject]$o }
  $rows
}
function Scalar([string]$sql) { $r = @(Q $sql); if ($r.Count) { @($r[0].PSObject.Properties)[0].Value } }

$sum = [ordered]@{ collectedAt = (Get-Date).ToString('s'); server = $SqlServer; database = $Database }
$whoami = Scalar 'SELECT SUSER_SNAME()'
$sum.connectedAs = $whoami
$sum.sqlServerVersion = Scalar "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)) + ' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(100))"
$sum.b1Version = Scalar 'SELECT TOP 1 Version FROM CINF'
Write-Result PASS "Connected as $whoami. SQL Server $($sum.sqlServerVersion). CINF.Version = $($sum.b1Version)"

# Tables the connector reads, and their columns
$need = 'OPFS', 'OPFT', 'APFS', 'CUFD', 'UFD1', 'OUTB', 'OUDO', 'CINF'
$inList = "'" + ($need -join "','") + "'"
$cols = @(Q "SELECT TABLE_NAME t, COLUMN_NAME c, DATA_TYPE d, ORDINAL_POSITION p FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ($inList) ORDER BY TABLE_NAME, ORDINAL_POSITION")
$sum.tables = [ordered]@{}
foreach ($t in $need) {
  $c = @($cols | Where-Object { $_.t -eq $t })
  $sum.tables[$t] = [ordered]@{ exists = ($c.Count -gt 0); columns = @($c | ForEach-Object { "$($_.c):$($_.d)" }) }
  if ($c.Count) { Write-Result INFO "Table $t present ($($c.Count) columns)" } elseif ($t -in 'OPFS', 'CUFD', 'OUTB', 'OUDO') { Write-Result FAIL "Table $t is MISSING: the connector cannot work without it" } else { Write-Result WARN "Table $t not present" }
}

# Personal data classification (configuration rows, not personal data)
if ($sum.tables.OPFS.exists) {
  $sum.opfs = [ordered]@{
    rows = [int](Scalar 'SELECT COUNT(*) FROM OPFS')
    customRows = [int](Scalar 'SELECT COUNT(*) FROM OPFS WHERE AbsEntry > 0')
    byTypeCategory = @(Q 'SELECT Type, Category, COUNT(*) n FROM OPFS GROUP BY Type, Category ORDER BY n DESC')
    byTable = @(Q 'SELECT TableName, COUNT(*) n FROM OPFS GROUP BY TableName ORDER BY n DESC')
    customSample = @(Q 'SELECT TOP 50 AbsEntry, TableName, FieldName, Category, Type FROM OPFS WHERE AbsEntry > 0 ORDER BY AbsEntry')
  }
  Write-Result INFO "OPFS: $($sum.opfs.rows) rows ($($sum.opfs.customRows) customer-defined)"
}

# User-defined fields: catalog vs real columns
if ($sum.tables.CUFD.exists) {
  $sum.udf = [ordered]@{
    catalogRows = [int](Scalar 'SELECT COUNT(*) FROM CUFD')
    withRealColumn = [int](Scalar "SELECT COUNT(*) FROM CUFD c JOIN INFORMATION_SCHEMA.COLUMNS i ON i.TABLE_NAME = c.TableID AND i.COLUMN_NAME = 'U_' + c.AliasID")
    catalogWithoutColumn = @(Q "SELECT TOP 100 c.TableID, c.AliasID FROM CUFD c WHERE NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS i WHERE i.TABLE_NAME = c.TableID AND i.COLUMN_NAME = 'U_' + c.AliasID)")
    systemColumnsWithUPrefix = @(Q "SELECT i.TABLE_NAME t, i.COLUMN_NAME c FROM INFORMATION_SCHEMA.COLUMNS i WHERE i.COLUMN_NAME LIKE 'U[_]%' AND i.TABLE_NAME NOT LIKE '@%' AND NOT EXISTS (SELECT 1 FROM CUFD c WHERE c.TableID = i.TABLE_NAME AND 'U_' + c.AliasID = i.COLUMN_NAME)")
    tables = @(Q 'SELECT TableID, COUNT(*) n FROM CUFD GROUP BY TableID ORDER BY TableID')
  }
  $ok = $sum.udf.catalogRows -eq $sum.udf.withRealColumn
  Write-Result $(if ($ok) { 'PASS' } else { 'WARN' }) "CUFD: $($sum.udf.withRealColumn) of $($sum.udf.catalogRows) catalog fields exist as real column U_<AliasID>"
  Write-Result INFO "System columns starting with U_ not in CUFD: $(@($sum.udf.systemColumnsWithUPrefix).Count)"
}

# User tables and objects
if ($sum.tables.OUTB.exists) { $sum.udt = @(Q 'SELECT TableName, ObjectType FROM OUTB ORDER BY TableName'); Write-Result INFO "OUTB (user tables): $(@($sum.udt).Count)" }
if ($sum.tables.OUDO.exists) { $sum.udo = @(Q 'SELECT Code, TableName, [TYPE] AS ObjType FROM OUDO ORDER BY Code'); Write-Result INFO "OUDO (user objects): $(@($sum.udo).Count)" }
$cn.Close()
Save-Json $sum "$OutDir\db-summary.json"
Write-Result INFO "Database catalog saved in $OutDir"
