# Customer-defined objects are resolved from the database catalog, never from the knowledge base

Fields starting with `U_` are not reliably customer-defined (some system columns also start with `U_`), so the prefix is not used. User-defined fields come from the field catalog, user tables and objects from the table and object catalogs of each company database. The knowledge base is generated from a native upstream with every customer-defined item excluded, so it stays valid across companies.
