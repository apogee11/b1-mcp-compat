# Notes per build

What was and was not checked for each build that serves the extended read (sets the Service Layer of that build does not expose). The list of builds is in [supported-builds.md](supported-builds.md). Read this before relying on a set in production: **if your company uses one of the sets listed as not checked, test it against your own Service Layer first.**

Method: the rules come from the engine code of the build (not from guesses). The JSON of every set is compared with the native Service Layer FP2608 of a test company (`tools/parity/read_parity.py`). A set can only be compared when it has rows, and some sets cannot be filled in that test company.

## SP2408 (`B11000SP_2408`)

Build id: `10.0.260.0-ea80448581ff-fe6cbe19acb9-b03b314fdd6c`. Share of the 2608 sets served by the extended read: 28.6% (131 of 458, formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2408 Service Layer**.

140 sets in the package: 131 served, 9 not available in this build (they answer 400 "Not available in this version").

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 91 |
| Not available in this build (400 by design) | 9 |
| Expected differences (columns that do not exist in 2408 SP) | 2: `PersonalFieldsSetups` (no `SysDefined`), `TaxDeclarationBoxes` (no `Remarks`; positional link of BOX1-4 replicated from the engine) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below.

- **Tables that do not exist in 2408 SP (9 sets not available, they answer 400 "Not available in this version"):** `CAEAInformations` (CAEA), `DatevRuns` (ODTV), `DatevTransactionsDeliveriesAndServices` (ODTS), `ElsterRun` (OELS, ELS1-3; `TaxReportFilterElster` goes with it), `ISDDocuments` (ISD3), `ImportToExcels` (OIMP, IMP1-3), `PersonalDataRetentionPeriods` (ODRP), `WTaxCategories` (OWCT), `WTaxRelationTypes` (OWRT). There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** `PersonalFieldsSetups.SysDefined` (`IsSystemDefined` in 2608) and `TaxDeclarationBoxes.Remarks`.
- **Expected parity differences against the 2608 test company:** those 2 sets (the only DIFF of the parity run; 28 OK, 101 no data, 9 unavailable as designed).
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order in 2408 SP, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent. Also the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`).
- **Not measured against a real 2408 Service Layer (all of it):** the 38 sets without rows (list below), the order of the child lines of CHD2/FNS1/MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `$metadata` order of the injected items, `PageSize` of `b1s.conf`, `$batch` (not intercepted). No end-to-end run against a real 2408 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

**The test Service Layer does not let you create rows** (error `-1008`; the DI API has only `Get`/`GetList` for most of them, so this is the engine, not the REST API):
`ChecksForPaymentDrafts` (child CHD2), `FolioNumberingSeries` (FNS1), `ManualDistributionRules` (MDR1), `DataImportTemplates`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `GstRegnTypes`, `IncomingPaymentOrders`, `OutgoingPaymentOrders`, `PostdatedDeposits`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `MenuAbbreviations`, `ProcessChecklistTemplates`, `ProcessChecklistInstances`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances`, `WorkflowTemplateManagers`.

**Need a localization or a fiscal setup the test company does not have:**
`BrazilServiceCodes`, `BrazilTaxAdjustments`, `Counties`, `ISDInvoices`, `ISDCreditMemos`, `ISDRecipientInvoices`, `ISDRecipientCreditMemos`, `AcknowledgementNumbers` (needs a fiscal year), `CountWidgets` (needs a saved query), `EJABCompanyDetails`.

**Creation failed with an engine error that was not investigated:**
`DetailedCheckingRules`, `DistributionLists`, `POSCashRegisters`, `PointsOfIssues`, `ProductSourceCodes`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value (decompiled in 2408 SP and 2608), so the connector serves the date as `T00:00:00Z`. Not seen with live data (the test company has no row with a time).
- User fields: the JSON rule per type letter is decompiled in 2408 SP and 2608. Not seen with live data: D with time, N with time, B with percent or price edit type, `@NAME` user tables, and `eq null` on a user field.
- `TaxReportFilterElster` (`ElsterRun`) is not available in 2408 SP: its tables do not exist in that build.
- `ISDInvoices`, `ISDCreditMemos`, `ISDRecipientInvoices` and `ISDRecipientCreditMemos`: the column `HandWritten` is served from the column `HandWrtten`, as the native query does; its value was not compared (the tables are empty in the test company).
- `$batch` is not intercepted (decision closed 2026-10-27): an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET; intercepting needs parsing multipart, serving the local GETs, rebuilding the answers in order and stopping at the first failure. It will be revisited only if a client needs it.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields.
- Measured against the native 2608 (not yet against a 2408): Content-Type and `OData-Version` of `$metadata` and `/$count`, `@odata.context` with `$select`, `eq null` / `ne null`, and the page size order (`Prefer: odata.maxpagesize` over `B1S-PageSize` over the configured size). Injected enums sit in name order like the native ones.
- Order of the injected items in the flat `$metadata` (decision closed 2026-10-27, a convention and not a heuristic): every type a native 2408 lacks goes at the end of its section (ComplexType, EntityType, EntitySet), in ordinal UTF-16 name order; enums go to their ordinal place among the native ones. It is not measured against a 2408.
- Not measured: the position of the injected entity types and entity sets against a native 2408 (the native order is an internal container order, not alphabetical; see the convention above). The `PageSize` key of the Service Layer `b1s.conf` cannot be read by the connector (it is on another machine). Decision (closed 2026-10-27): the option `B1McpCompat__PageSize` (default 20, the native default) is the only source besides the request headers; an administrator who sets `PageSize` in `b1s.conf` must set the same value in this option by hand.
- Reads go through SQL: the B1 authorizations of the user are not applied.
