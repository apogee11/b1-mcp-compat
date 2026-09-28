# MCP server (b1-mcp-server/)

The MCP server is not stored in this repository. `b1-mcp-server/` is git-ignored: each clone supplies SAP's package or the partner's own variant ([ADR 0008](docs/adr/0008-mcp-server-is-not-in-the-repository.md)). The setup steps are in the README, "Get the MCP server".

## Reference version

The connector is developed and verified against:

- SAP Business One MCP Server sample, 1.0.0 (CHANGELOG 2026-07-15), shipped with 10.0 FP2608 as `b1-mcp-server.zip`, MIT license.
- Content hash of that pristine release (SHA-256 over the sorted per-file SHA-256 list, excluding `node_modules`, `dist` and `.env`; 99 files):
  `e43ed66a64fad047a3f1c4cf59aa2f6d0eadb76a8e0e8160f82cc37b1583338d`

Recompute (Git Bash, from `b1-mcp-server/`):
```
find . -type f -not -path './node_modules/*' -not -path './dist/*' -not -name '.env' | sort | xargs sha256sum | sha256sum
```

## Optional tweak

`tools/e2e` lists every entity set. SAP's `b1_find_entities` caps `limit` at 50, so on the stock server the test sweeps by letter and may miss some. For full coverage raise the cap in `src/tools/b1-tool-registry.ts` (`.max(50)` to `.max(100000)`). Nothing else requires changes to the MCP server.
