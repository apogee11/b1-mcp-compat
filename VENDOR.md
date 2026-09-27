# Vendored third-party code

## b1-mcp-server

- Origin: SAP Business One MCP Server sample, shipped with SAP Business One 10.0 FP2608 (`b1-mcp-server.zip`).
- Version: 1.0.0 (CHANGELOG 2026-07-15), license MIT (see `b1-mcp-server/LICENSE`).
- Status: **unmodified**. Do not edit; see [ADR 0006](docs/adr/0006-vendored-mcp-server-stays-untouched.md).
- Content hash (SHA-256 over the sorted per-file SHA-256 list, excluding `node_modules`, `dist` and `.env`; 99 files):
  `e43ed66a64fad047a3f1c4cf59aa2f6d0eadb76a8e0e8160f82cc37b1583338d`

Recompute (Git Bash, from `b1-mcp-server/`):
```
find . -type f -not -path './node_modules/*' -not -path './dist/*' -not -name '.env' | sort | xargs sha256sum | sha256sum
```

To update: replace the folder with the new SAP release, update this file, run `tools/parity`.
