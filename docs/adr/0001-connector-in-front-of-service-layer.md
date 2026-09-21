# The connector sits in front of the Service Layer instead of patching either side

The SAP MCP server needs two things older Service Layers lack (annotated metadata and a personal-data classification service). We add a separate connector that answers them, rather than modifying the MCP server (it is SAP's sample and must stay updatable) or the Service Layer binaries (closed C++, would break support and signatures). The connector must stay inert when the upstream is already native.

## Considered Options

- Patch the Service Layer of each old version: rejected, closed source and unsupported.
- Fork the MCP server to degrade gracefully: rejected, every upstream release would need a merge.
