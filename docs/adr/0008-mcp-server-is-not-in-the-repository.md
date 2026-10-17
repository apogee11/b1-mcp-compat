# The MCP server is not in the repository

The connector targets SAP's official `b1-mcp-server` and any partner variant that keeps the same Service Layer contract. Partners customise that server, so shipping one copy would be wrong for most of them and would hide whether the connector works with the official one. `b1-mcp-server/` is git-ignored; whoever clones the project puts SAP's package or their own variant in the root (README, "Get the MCP server"). `VENDOR.md` keeps the reference version and hash we verify against. The connector never depends on changes to the MCP server.
