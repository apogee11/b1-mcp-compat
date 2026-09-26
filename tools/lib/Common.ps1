# Shared helpers for the probe scripts. Windows PowerShell 4.0+ (Windows Server 2012 R2 and later), .NET Framework only, no external tools.
Set-StrictMode -Version 2

if (-not ('B1McpTrustAll' -as [type])) {
  Add-Type -TypeDefinition @"
using System.Net;
public static class B1McpTrustAll {
  public static void Enable() { ServicePointManager.ServerCertificateValidationCallback = delegate { return true; }; }
}
"@
}

function Initialize-Network {
  param([bool]$SkipTlsVerify = $true)
  # Old servers default to TLS 1.0; Service Layer only accepts 1.2+.
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  # The Service Layer answers 'Invalid login credential' (HTTP 400) when the request carries 'Expect: 100-continue'.
  [Net.ServicePointManager]::Expect100Continue = $false
  if ($SkipTlsVerify) { [B1McpTrustAll]::Enable() }
}

function New-OutDir {
  param([string]$Path)
  if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Force $Path | Out-Null }
  (Resolve-Path $Path).Path
}

function Get-PlainPassword {
  param([string]$EnvName, [string]$Prompt)
  $v = [Environment]::GetEnvironmentVariable($EnvName)
  if ($v) { return $v }
  $s = Read-Host $Prompt -AsSecureString
  [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}

# One HTTP call. Never throws on HTTP errors: returns Status/Headers/Bytes/Text/Ms.
function Invoke-Http {
  param(
    [string]$Method, [string]$Url, [string]$Body = $null,
    [hashtable]$Headers = @{}, [Net.CookieContainer]$Jar = $null, [int]$TimeoutSec = 180
  )
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $req = [Net.HttpWebRequest]::Create($Url)
  $req.Method = $Method; $req.Timeout = $TimeoutSec * 1000; $req.ReadWriteTimeout = $TimeoutSec * 1000
  $req.AllowAutoRedirect = $false; $req.AutomaticDecompression = [Net.DecompressionMethods]::GZip -bor [Net.DecompressionMethods]::Deflate
  if ($Jar) { $req.CookieContainer = $Jar }
  $req.Accept = 'application/json'
  foreach ($k in $Headers.Keys) {
    switch ($k.ToLower()) {
      'accept' { $req.Accept = $Headers[$k] }
      'content-type' { $req.ContentType = $Headers[$k] }
      default { $req.Headers[$k] = $Headers[$k] }
    }
  }
  if ($Body -ne $null -and $Method -ne 'GET') {
    if (-not $req.ContentType) { $req.ContentType = 'application/json' }
    $b = [Text.Encoding]::UTF8.GetBytes($Body); $req.ContentLength = $b.Length
    $st = $req.GetRequestStream(); $st.Write($b, 0, $b.Length); $st.Close()
  }
  $resp = $null; $err = $null
  try { $resp = $req.GetResponse() }
  catch [Net.WebException] { $resp = $_.Exception.Response; $err = $_.Exception.Message }
  $status = 0; $hdr = @{}; $bytes = New-Object byte[] 0; $setCookie = @()
  if ($resp) {
    $status = [int]$resp.StatusCode
    $sc = $resp.Headers.GetValues('Set-Cookie'); if ($sc) { $setCookie = @($sc) }
    foreach ($k in $resp.Headers.AllKeys) { if ($k -ne 'Set-Cookie') { $hdr[$k] = $resp.Headers[$k] } }
    $ms = New-Object IO.MemoryStream; $rs = $resp.GetResponseStream(); $rs.CopyTo($ms); $bytes = $ms.ToArray(); $rs.Close(); $resp.Close()
  }
  $sw.Stop()
  [pscustomobject]@{ Status = $status; Headers = $hdr; Bytes = $bytes; Text = [Text.Encoding]::UTF8.GetString($bytes); Ms = $sw.ElapsedMilliseconds; Error = $err; SetCookie = $setCookie }
}

# Saves headers+body of a call under OutDir and returns a one-line index entry. Login bodies are redacted.
function Save-Call {
  param([string]$OutDir, [string]$Name, $Res, [switch]$Redact)
  $body = $Res.Bytes
  if ($Redact) { $body = [Text.Encoding]::UTF8.GetBytes(($Res.Text -replace '"SessionId"\s*:\s*"[^"]*"', '"SessionId":"<redacted>"')) }
  [IO.File]::WriteAllBytes((Join-Path $OutDir "$Name.body"), $body)
  $h = @("HTTP $($Res.Status)  ($($Res.Ms) ms)") + ($Res.Headers.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name): $($_.Value)" })
  Set-Content -Path (Join-Path $OutDir "$Name.headers.txt") -Value $h -Encoding UTF8
}

# First "message" of a Service Layer error body (v1 and v2 shapes), or $null.
function Get-SlErrorMessage {
  param($Res)
  if ($Res.Status -lt 400) { return $null }
  $m = [regex]::Match($Res.Text, '"message"\s*:\s*(?:\{[^}]*?"value"\s*:\s*)?"([^"]*)"')
  if ($m.Success) { $m.Groups[1].Value } else { ($Res.Text -replace '\s+', ' ').Substring(0, [Math]::Min(160, $Res.Text.Length)) }
}

function Write-Result {
  param([string]$Level, [string]$Text)
  $c = @{ PASS = 'Green'; INFO = 'Gray'; WARN = 'Yellow'; FAIL = 'Red' }[$Level]
  Write-Host ("[{0}] {1}" -f $Level, $Text) -ForegroundColor $c
}

function Save-Json {
  param($Object, [string]$Path)
  $Object | ConvertTo-Json -Depth 8 | Set-Content -Path $Path -Encoding UTF8
}

# CookieContainer drops cookies for dot-less host names (e.g. WIN-SET2R2U2763), so cookies are carried by hand.
function Get-CookieHeader {
  param($Res)
  ($Res.SetCookie | ForEach-Object { ($_ -split ';')[0].Trim() }) -join '; '
}
