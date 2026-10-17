# The connector sits in front of the Service Layer

The goal is a connector that makes an older Service Layer behave, towards SAP's MCP server, like FP2608: transparent, and a superset of what the older versions do. Two things older Service Layers lack (annotated metadata and a personal-data classification service) are answered by a separate connector, because neither the MCP server (SAP's sample, must stay updatable) nor the Service Layer binaries (closed C++) can be patched. When the upstream is already native the connector stays inert.

ADRs are guidance, not constraints. If research shows a better way to reach the goal, change the ADR in the same piece of work and write the reason down.
