# Customer-defined objects come from the Service Layer; the database is read only for OPFS

Fields starting with `U_` are not reliably customer-defined (some system columns also start with `U_`), so the prefix is not used. User-defined fields, tables and objects are read from the Service Layer itself (`UserFieldsMD`, `UserTablesMD`, `UserObjectsMD`) with the session of the caller: on FP2208 and FP2408 they equal the company's `CUFD`, `OUTB` and `OUDO` row for row (verified with a SQL cross-check on both versions), so no SQL is needed for them. The only thing left in the database is `OPFS`, the personal-data classification, which an older Service Layer does not expose and which companies may customise; the connector reads it with one `SELECT` and selects rows the way the native action does. This replaces ADR 0003 (database access as a requirement for everything) and ADR 0005 (catalog from the database).

## Consequences

- The system columns of user tables are not looked up in `INFORMATION_SCHEMA` any more; they are fixed per table type (`Code`/`Name`, document and master-data headers, line tables). A user table owned by SAP with columns that are neither of those nor user fields (e.g. `SBOUDT`) has to come from the knowledge base or `mappings.override.json`.
- A Service Layer too old to expose `UserFieldsMD` (before FP2208) is out of scope.
- Without a database login the connector still annotates metadata; only the personal-data action fails closed.
