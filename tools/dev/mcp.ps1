# Starts SAP's b1-mcp-server (built in b1-mcp-server\, see README 'Run from the console') against the local connector, with the details in .credentials\<Name>.json.
#   .\mcp.ps1                 # credentials "development"; the connector (dev.ps1) must already be listening
#   .\mcp.ps1 -Name cliente2208 -McpPort 3100
# Local development only: it turns off the write confirmation and TLS verification. MCP endpoint: http://localhost:<McpPort>/mcp
param([string]$Name = 'development', [int]$ConnectorPort = 50300, [int]$McpPort = 3100)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $PSScriptRoot 'credentials.ps1')
$p = Get-DevCredentials $Name
$server = Join-Path $root 'b1-mcp-server'
if (-not (Test-Path (Join-Path $server 'dist\index.js'))) { throw "b1-mcp-server is not built. Put it in b1-mcp-server\ and run: npm install --legacy-peer-deps; npm run build (README, 'Run from the console')." }

$env:SERVICE_LAYER_ROOT_URL = "http://127.0.0.1:$ConnectorPort"
$env:AUTHENTICATION_MODE = 'direct'
$env:B1_COMPANY_DB = $p.Company
$env:B1_USERNAME = $p.User
$env:B1_PASSWORD = $p.Password
$env:HTTPS_ENABLED = 'false'
$env:PORT = "$McpPort"
$env:MCP_ALLOWED_HOSTS = "localhost:$McpPort,127.0.0.1:$McpPort,[::1]:$McpPort,localhost,127.0.0.1"
$env:MCP_HUMAN_CONFIRMATION_ENABLED = 'false'
$env:MCP_RATE_LIMIT_MAX = '5000'
$env:NODE_TLS_REJECT_UNAUTHORIZED = '0'

Write-Host "MCP server for '$Name' on http://localhost:$McpPort/mcp. Ctrl+C to stop."
node (Join-Path $server 'dist\index.js')
