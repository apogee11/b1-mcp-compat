# The SAP MCP server is vendored unmodified

> Superseded by [ADR 0007](0007-vendored-mcp-server-is-ours-to-modify.md).

The repository carries a copy of SAP's `b1-mcp-server` so the whole system can be built and tested together. It is never edited; `VENDOR.md` records its upstream version and a content hash. Updating it means replacing the folder and re-running the parity checks.
