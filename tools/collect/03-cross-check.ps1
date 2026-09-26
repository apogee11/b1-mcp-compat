# Offline check: does what the Service Layer exposes agree with the database catalog?
# Needs the output of 01-service-layer.ps1 and 02-database.ps1. No network, no credentials.
#   .\03-cross-check.ps1 -SlDir .\out\sl -DbDir .\out\db -OutDir .\out\cross
param([string]$SlDir = '.\out\sl', [string]$DbDir = '.\out\db', [string]$OutDir = '.\out\cross')
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
$OutDir = New-OutDir $OutDir
$xml = [xml](Get-Content (Join-Path $SlDir 'md-v2-full.xml') -Raw -Encoding UTF8)
$db = Get-Content (Join-Path $DbDir 'db-summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$ns = New-Object Xml.XmlNamespaceManager $xml.NameTable
$edm = $xml.DocumentElement.SelectNodes('//*[local-name()="Schema"]')[0]
function Nodes($xp) { $xml.SelectNodes($xp) }
$sets = @{}; foreach ($s in Nodes '//*[local-name()="EntitySet"]') { $sets[$s.GetAttribute('Name')] = ($s.GetAttribute('EntityType') -replace '^SAPB1\.', '') }
$types = @{}; foreach ($t in Nodes '//*[local-name()="EntityType" or local-name()="ComplexType"]') { $types[$t.GetAttribute('Name')] = @($t.ChildNodes | Where-Object { $_.LocalName -eq 'Property' } | ForEach-Object { $_.GetAttribute('Name') }) }
$res = [ordered]@{}

# 1. User tables: ObjectType 0 -> entity U_<Table>; user objects -> entity named by OUDO.Code
$udtMiss = @(); $udtOk = 0
foreach ($t in @($db.udt)) { if ($t.ObjectType -eq 0) { if ($sets.ContainsKey("U_$($t.TableName)")) { $udtOk++ } else { $udtMiss += $t.TableName } } }
$udoMiss = @(); $udoOk = 0
foreach ($o in @($db.udo)) { if ($sets.ContainsKey($o.Code)) { $udoOk++ } else { $udoMiss += $o.Code } }
$res.userTables = [ordered]@{ ok = $udtOk; missingInServiceLayer = $udtMiss }
$res.userObjects = [ordered]@{ ok = $udoOk; missingInServiceLayer = $udoMiss }
Write-Result $(if ($udtMiss.Count) { 'WARN' } else { 'PASS' }) "User tables (OUTB ObjectType 0) exposed as U_<Table>: $udtOk ok, $($udtMiss.Count) missing"
Write-Result $(if ($udoMiss.Count) { 'WARN' } else { 'PASS' }) "User objects (OUDO) exposed by Code: $udoOk ok, $($udoMiss.Count) missing"

# 2. Entity sets named U_* that are NOT explained by OUTB (would break the 'U_<Table>' rule)
$expl = @{}; foreach ($t in @($db.udt)) { if ($t.ObjectType -eq 0) { $expl["U_$($t.TableName)"] = 1 } }
$unexpl = @($sets.Keys | Where-Object { $_ -like 'U_*' -and -not $expl.ContainsKey($_) })
$res.entitySetsWithUPrefixNotInOutb = $unexpl
Write-Result $(if ($unexpl.Count) { 'WARN' } else { 'PASS' }) "Entity sets with U_ prefix not explained by OUTB: $($unexpl.Count) $(if ($unexpl.Count) { '(' + ($unexpl -join ', ') + ')' })"

# 3. UDF coverage on well-known tables: CUFD fields vs properties of the matching Service Layer type
$map = @{ OCRD = 'BusinessPartner'; OITM = 'Item'; OUSR = 'User'; OCPR = 'ContactEmployee'; CRD1 = 'BPAddress' }
$cov = [ordered]@{}
foreach ($k in $map.Keys) {
  $tname = $map[$k]; if (-not $types.ContainsKey($tname)) { continue }
  $want = @($db.udf.tables | Where-Object { $_.TableID -eq $k }); if (-not $want) { continue }
  # fields come from the DB catalog json only as counts; names come from CUFD via db-summary if present
  $cov[$k] = [ordered]@{ type = $tname; serviceLayerUdfProperties = @($types[$tname] | Where-Object { $_ -like 'U_*' }).Count; cufdFields = $want[0].n }
  Write-Result INFO "$k -> $tname : CUFD has $($want[0].n) UDF, Service Layer type exposes $($cov[$k].serviceLayerUdfProperties) U_ properties"
}
$res.udfCoverage = $cov
Save-Json $res "$OutDir\cross-check.json"
Write-Result INFO "Cross-check saved in $OutDir"
