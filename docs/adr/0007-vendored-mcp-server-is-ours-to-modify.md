# The vendored MCP server is a baseline we modify freely

Supersedes [ADR 0006](0006-vendored-mcp-server-stays-untouched.md).

`b1-mcp-server` is SAP's sample, and SAP expects partners to adapt it. We edit it in place whenever the project needs it. `VENDOR.md` keeps the hash and git tag of the pristine SAP release as the baseline; updating to a new SAP release means merging from that baseline, not replacing the folder. The connector must still work with the unmodified sample ([ADR 0001](0001-connector-in-front-of-service-layer.md)), so `tools/parity` keeps running against the baseline behaviour.

## Considered Options

- Keep the folder pristine and carry changes as patches: rejected, extra machinery for a single maintainer, and git already merges from a baseline.
