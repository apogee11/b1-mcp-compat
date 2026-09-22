# Read access to the company database is a requirement

The personal-data classification and every customer-defined object live only in the company database, and the Service Layer of older versions does not expose them. Instead of degrading when the database is unreachable, the connector requires it. SQL Server is supported; SAP HANA is built behind the same interface and labelled experimental because it will not be tested yet. The connector and the Service Layer may run on different servers, so database settings are separate from the upstream URL.

## Consequences

Without a database login the connector fails closed, which matches the MCP server's own production behaviour.
