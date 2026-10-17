# Customer-defined objects and data sources: use whatever gives the native behaviour

Fields starting with `U_` are not reliably customer-defined (some system columns also start with `U_`), so the prefix is not used. User-defined fields, tables and objects are read from the Service Layer itself (`UserFieldsMD`, `UserTablesMD`, `UserObjectsMD`) with the caller's session: on FP2208 and FP2408 they equal the company's `CUFD`, `OUTB` and `OUDO` row for row (verified with a SQL cross-check), so no SQL is needed for them and no extra login is required. The personal-data classification (`OPFS`) is not exposed by older Service Layers and companies may customise it; it is read with one `SELECT`.

There is no rule limiting the database to `OPFS`. Any source (Service Layer, company database, the version's DLLs) may be used wherever it brings the connector closer to the native FP2608 behaviour, for example the schema of user tables owned by SAP such as `SBOUDT`. Prefer the Service Layer when it gives the same answer. Without a database login the connector still annotates metadata; only what truly needs the database fails closed. SQL Server is supported; HANA is built behind the same interface and untested.

A Service Layer too old to expose `UserFieldsMD` (before FP2208) is out of scope.
