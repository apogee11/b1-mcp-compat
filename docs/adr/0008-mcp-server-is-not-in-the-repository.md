# The MCP server is not in the repository

Supersedes [ADR 0007](0007-vendored-mcp-server-is-ours-to-modify.md).

The goal is a connector as standard as possible: it targets SAP's official `b1-mcp-server`, and any partner variant that keeps the same Service Layer contract. Partners customise that server, so shipping one copy would be wrong for most of them. `b1-mcp-server/` is therefore git-ignored; whoever clones the project puts in the root SAP's package or their own variant (README, "Get the MCP server"). `VENDOR.md` keeps the reference version and hash we verify against.

The connector never depends on changes to the MCP server ([ADR 0001](0001-connector-in-front-of-service-layer.md)); the one optional tweak (the `limit` cap used by `tools/e2e`) is documented in `VENDOR.md`.

## Considered Options

- Keep a vendored, modified copy ([ADR 0007](0007-vendored-mcp-server-is-ours-to-modify.md)): rejected, it ties the project to one variant and hides whether the connector works with the official one.
