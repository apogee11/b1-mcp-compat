# Notes per build

What was and was not checked for each build that serves the extended read (sets the Service Layer of that build does not expose). The list of builds is in [supported-builds.md](supported-builds.md). Read this before relying on a set in production: **if your company uses one of the sets listed as not checked, test it against your own Service Layer first.**

Method: the rules come from the engine code of the build (not from guesses). The JSON of every set is compared with the native Service Layer FP2608 of a test company (`tools/parity/read_parity.py`). A set can only be compared when it has rows, and some sets cannot be filled in that test company.

## SP2408 (`B11000SP_2408`)

Build id: `10.0.260.0-ea80448581ff-fe6cbe19acb9-b03b314fdd6c`. Total share of the 2608 sets: 96.9% (313 native + 131 extended = 444 of 458; the extended read alone adds 28.6%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

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

## SP2505 (`B11000SP_2505`)

Build id: `10.0.290.0-c9ae21daeed4-55fe44b82e4d-3443b8dca453`. Total share of the 2608 sets: 97.2% (317 native + 132 extended, 4 of them in both = 445 of 458; the extended read alone adds 28.8%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2505 Service Layer**.

140 sets in the package: 132 served, 8 not available in this build (they answer 400 "Not available in this version").

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 92 |
| Not available in this build (400 by design) | 8 |
| Expected differences | 1: `TaxDeclarationBoxes` (length of `BoxDefinitionRows` only; positional link of BOX1-4 replicated from the engine) |
| **Not checked: no rows to compare** | 39 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below.

- **Tables that do not exist in 2505 SP (8 sets not available, they answer 400 "Not available in this version"):** `CAEAInformations` (CAEA), `DatevRuns` (ODTV), `DatevTransactionsDeliveriesAndServices` (ODTS), `ElsterRun` (OELS, ELS1-3; `TaxReportFilterElster` goes with it), `ISDDocuments` (child ISD3; 5 columns of OISD are also missing), `ImportToExcels` (OIMP, IMP1-3), `WTaxCategories` (OWCT), `WTaxRelationTypes` (OWRT). There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** none. All the columns of the 132 served sets exist in 2505 SP.
- **Difference with 2408 SP:** 2505 SP serves three sets more, because their table or column exists: `PersonalDataRetentionPeriods` (ODRP), `PersonalFieldsSetups` (`SysDefined` exists) and `TaxDeclarationBoxes` (`Remarks` exists). No alias is needed for `DeductibleTaxes` (the 2505 dictionary already calls it that; 2408 SP calls it `DeductibleTaxs`). 132 served here against 131 in 2408 SP.
- **Expected parity differences against the 2608 test company:** `TaxDeclarationBoxes`, only the length of `BoxDefinitionRows` (the only DIFF of the parity run; 92 OK, 39 no data, 8 unavailable as designed). `Remarks` does not differ.
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order in 2505 SP, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent. The length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`).
- **Not measured against a real 2505 Service Layer (all of it):** the 39 sets without rows (list below, `PersonalDataRetentionPeriods` among them: its table is empty in the test company), the order of the child lines of CHD2/FNS1/MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `$metadata` order of the injected items, `PageSize` of `b1s.conf`, `$batch` (not intercepted). The `$metadata` writer and the functions that add actions and complex types of the 2505 Service Layer were not compared in full with the ones of 2608 (some code paths were not decompiled). No end-to-end run against a real 2505 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

**The test Service Layer does not let you create rows** (error `-1008`; the DI API has only `Get`/`GetList` for most of them, so this is the engine, not the REST API):
`ChecksForPaymentDrafts` (child CHD2), `FolioNumberingSeries` (FNS1), `ManualDistributionRules` (MDR1), `DataImportTemplates`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `GstRegnTypes`, `IncomingPaymentOrders`, `OutgoingPaymentOrders`, `PostdatedDeposits`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `MenuAbbreviations`, `ProcessChecklistTemplates`, `ProcessChecklistInstances`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances`, `WorkflowTemplateManagers`.

**Need a localization or a fiscal setup the test company does not have:**
`BrazilServiceCodes`, `BrazilTaxAdjustments`, `Counties`, `ISDInvoices`, `ISDCreditMemos`, `ISDRecipientInvoices`, `ISDRecipientCreditMemos`, `AcknowledgementNumbers` (needs a fiscal year), `CountWidgets` (needs a saved query), `EJABCompanyDetails`.

**Creation failed with an engine error that was not investigated:**
`DetailedCheckingRules`, `DistributionLists`, `POSCashRegisters`, `PointsOfIssues`, `ProductSourceCodes`.

**Not seeded:** `PersonalDataRetentionPeriods` (the table is empty in the test company; no data to compare).

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves the date as `T00:00:00Z`. Not seen with live data.
- `ISDInvoices`, `ISDCreditMemos`, `ISDRecipientInvoices` and `ISDRecipientCreditMemos`: the column `HandWritten` is served from the column `HandWrtten`, as the native query does; its value was not compared (the tables are empty in the test company).
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its order and the `PageSize` of `b1s.conf` follow the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and are not measured against a 2505.
- Reads go through SQL: the B1 authorizations of the user are not applied.

## SP2402 (`B11000SP_2402`)

Build id: `10.0.240.0-b04e4db08c43-2bd6f6550ccb-2a7e7add7c82`. Total share of the 2608 sets: 95.4% (310 native + 127 extended = 437 of 458; the extended read alone adds 27.7%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2402 Service Layer**.

140 sets in the package: 127 served, 13 not available in this build (they answer 400 "Not available in this version").

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 87 |
| Not available in this build (400 by design) | 13 |
| Expected differences (columns that do not exist in 2402 SP) | 2: `PersonalFieldsSetups` (no `SysDefined`), `TaxDeclarationBoxes` (no `Remarks`; positional link of BOX1-4 replicated from the engine) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below and in [SP2408](#sp2408-b11000sp_2408), whose conventions apply here too.

- **Tables that do not exist in 2402 SP (13 sets not available, they answer 400 "Not available in this version"):** `ATDocumentTypes` (OSDT), `CAEAInformations` (CAEA), `DatevRuns` (ODTV), `DatevTransactionsDeliveriesAndServices` (ODTS), `ElsterRun` (OELS, ELS1-3; `TaxReportFilterElster` goes with it), `ISDDocuments` (ISD3), `ImportToExcels` (OIMP, IMP1-3), `PersonalDataRetentionPeriods` (ODRP), `SAFTProductTypes` (OSPT), `SAFTTaxCodes` (OSTX), `SAFTTransactionTypes` (OSTE), `WTaxCategories` (OWCT), `WTaxRelationTypes` (OWRT). There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** `PersonalFieldsSetups.SysDefined` (`IsSystemDefined` in 2608) and `TaxDeclarationBoxes.Remarks`.
- **Difference with 2408 SP:** 2402 SP serves four sets less, because their tables do not exist: `ATDocumentTypes` and the three `SAFT*` sets (`SAFTProductTypes`, `SAFTTaxCodes`, `SAFTTransactionTypes`). It serves no set that 2408 SP does not. The alias is the same: the engine calls the table `DeductibleTaxs` and 2608 calls it `DeductibleTaxes`, so the package answers to both. 127 served here against 131 in 2408 SP.
- **Expected parity differences against the 2608 test company:** those 2 sets (the only DIFF of the parity run; 87 OK, 38 no data, 13 unavailable as designed).
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order in 2402 SP, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent. Also the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`).
- **Not measured against a real 2402 Service Layer (all of it):** the 38 sets without rows (list below, the same ones as in 2408 SP), the order of the child lines of CHD2/FNS1/MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `$metadata` order of the injected items, `PageSize` of `b1s.conf`, `$batch` (not intercepted). No end-to-end run against a real 2402 yet.

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
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves the date as `T00:00:00Z`. Not seen with live data.
- `ISDInvoices`, `ISDCreditMemos`, `ISDRecipientInvoices` and `ISDRecipientCreditMemos`: the column `HandWritten` is served from the column `HandWrtten`, as the native query does; its value was not compared (the tables are empty in the test company).
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its order and the `PageSize` of `b1s.conf` follow the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and are not measured against a 2402.
- Reads go through SQL: the B1 authorizations of the user are not applied.

## FP2602 (`B11000_2602`)

Build id: `10.0.320.0-1dbf7e7cfc75-a13b197811f4-92a0ab6f1a33`. Total share of the 2608 sets: 98.9% (331 native + 138 extended, 16 of them in both = 453 of 458; the extended read alone adds 30.1%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2602 Service Layer**.

140 sets in the package: 138 served, 2 not available in this build (they answer 400 "Not available in this version"). 16 of the 138 are sets that the 2602 Service Layer already exposes: it answers them, not the connector.

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 92 |
| Not available in this build (400 by design) | 2 |
| Expected differences | 1: `TaxDeclarationBoxes` (length of `BoxDefinitionRows` only; positional link of BOX1-4 replicated from the engine) |
| **Not checked: no rows to compare** | 45 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below and in [SP2408](#sp2408-b11000sp_2408) and [SP2505](#sp2505-b11000sp_2505), whose conventions apply here too.

- **Tables that do not exist in 2602 (2 sets not available, they answer 400 "Not available in this version"):** `ElsterRun` (OELS, ELS1-3; `TaxReportFilterElster` goes with it) and `ImportToExcels` (OIMP, IMP1-3). There is nothing to serve: the engine of this build has no such table. `ScheduleJobs` is outside the scope of the extended read.
- **Columns that do not exist (not announced, not returned):** none. All the columns of the 138 served sets exist in 2602.
- **Difference with 2505 SP:** 2602 serves six sets more, because their tables exist: `CAEAInformations`, `DatevRuns`, `DatevTransactionsDeliveriesAndServices`, `ISDDocuments` (children ISD1-3), `WTaxCategories` and `WTaxRelationTypes`; it serves no set that 2505 SP serves and 2602 does not. New columns in 2602: `SpecificWTHAmountsService` (`WHTRelCode`, `AmtDedUsed`), `ManualDistributionRules` (`DTVId`), `PointsOfIssues` (`CAEARqtFrm`, `Regime`); `SpecificWTHAmountsService.Category` is `nvarchar(3)` (`char(1)` in 2505 SP). 138 served here against 132 in 2505 SP. The 2602 Service Layer also declares three complex types in its `$metadata` that 2505 SP does not (`CompanySummary`, `Event`, `EventCatagory`).
- **Sets the 2602 Service Layer already exposes (16): the connector leaves them to the Service Layer.** `CentralBankIndicator`, `Counties`, `DatevRuns`, `DeductibleTaxes`, `DistributionLists`, `ISDCreditMemos`, `ISDDocuments`, `ISDInvoices`, `ISDRecipientCreditMemos`, `ISDRecipientInvoices`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `PaymentWizards`, `RecurringTransactionTemplates`, `SpecificWTHAmountsService`. A request is intercepted only when the `$metadata` of the Service Layer of that session does not list the exact name asked, so these are never served from the package. That the 2602 Service Layer exposes them comes from its engine code and from the knowledge base of the build, not from a live 2602; if one is missing from your `$metadata`, the connector serves it from the package. The extended read serves the other 122.
- **Expected parity differences against the 2608 test company:** `TaxDeclarationBoxes`, only the length of `BoxDefinitionRows` (the only DIFF of the parity run; 92 OK, 45 no data, 2 unavailable as designed). `Remarks` does not differ. The parity run compares the connector with the package against the 2608, so it does not tell the 16 native sets apart (4 of them with rows and no differences: `CentralBankIndicator`, `DeductibleTaxes`, `PaymentWizards`, `SpecificWTHAmountsService`).
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order in 2602, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent. The length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`).
- **Not measured against a real 2602 Service Layer (all of it):** the 45 sets without rows (list below), the new columns and the children ISD1-3, the order of the child lines of CHD2/FNS1/MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `$metadata` order of the injected items, `PageSize` of `b1s.conf`, `$batch` (not intercepted). The Service Layer functions that add actions and complex types (`AddFunctions`) were not decompiled; they are identical to the 2505 ones, which were not decompiled either. For `WTaxCategories` and `WTaxRelationTypes` it is not shown that the engine reads their main row with the generic rule (they have no children, so the risk is only the main row). No end-to-end run against a real 2602 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

**The test Service Layer does not let you create rows** (error `-1008`; the DI API has only `Get`/`GetList` for most of them, so this is the engine, not the REST API):
`ChecksForPaymentDrafts` (child CHD2), `FolioNumberingSeries` (FNS1), `ManualDistributionRules` (MDR1), `DataImportTemplates`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `GstRegnTypes`, `IncomingPaymentOrders`, `OutgoingPaymentOrders`, `PostdatedDeposits`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `MenuAbbreviations`, `ProcessChecklistTemplates`, `ProcessChecklistInstances`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances`, `WorkflowTemplateManagers`.

**Need a localization or a fiscal setup the test company does not have:**
`BrazilServiceCodes`, `BrazilTaxAdjustments`, `Counties`, `ISDInvoices`, `ISDCreditMemos`, `ISDRecipientInvoices`, `ISDRecipientCreditMemos`, `AcknowledgementNumbers` (needs a fiscal year), `CountWidgets` (needs a saved query), `EJABCompanyDetails`.

**Creation failed with an engine error that was not investigated:**
`DetailedCheckingRules`, `DistributionLists`, `POSCashRegisters`, `PointsOfIssues`, `ProductSourceCodes`.

**Not seeded (the table is empty in the test company; no data to compare, and whether rows can be created there was not investigated):** `PersonalDataRetentionPeriods`, and the six sets that 2602 adds over 2505: `CAEAInformations`, `DatevRuns`, `DatevTransactionsDeliveriesAndServices`, `ISDDocuments`, `WTaxCategories`, `WTaxRelationTypes`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves the date as `T00:00:00Z`. Not seen with live data.
- `ISDInvoices`, `ISDCreditMemos`, `ISDRecipientInvoices` and `ISDRecipientCreditMemos`: the column `HandWritten` is served from the column `HandWrtten`, as the native query does; its value was not compared (the tables are empty in the test company). In 2602 these four are answered by the Service Layer itself.
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its order and the `PageSize` of `b1s.conf` follow the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and are not measured against a 2602.
- Reads go through SQL: the B1 authorizations of the user are not applied.

## FP2208 (`B11000_2208`)

Build id: `10.0.200.0-24aeb23de137-2eb48c85224f-c15ab35d594d`. Total share of the 2608 sets: 93.4% (307 native + 121 extended = 428 of 458; the extended read alone adds 26.4%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2208 Service Layer**.

140 sets in the package: 121 served, 19 not available in this build (they answer 400 "Not available in this version"). None of the 121 is exposed by the 2208 Service Layer, so the extended read serves all of them.

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 81 |
| Not available in this build (400 by design) | 19 |
| Expected differences | 2: `PersonalFieldsSetups` (column `IsSystemDefined` not returned) and `TaxDeclarationBoxes` (column `Remarks` not returned; length of `BoxDefinitionRows`) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below and in [SP2408](#sp2408-b11000sp_2408) and [SP2505](#sp2505-b11000sp_2505), whose conventions apply here too.

- **Tables that do not exist in 2208 (19 sets not available, they answer 400 "Not available in this version"):** `ATDocumentTypes` (OSDT), `CAEAInformations` (CAEA), `DatevRuns` (ODTV), `DatevTransactionsDeliveriesAndServices` (ODTS), `DeductibleTaxes` (ORTG), `EBooksFeesCategories` (OEFC), `EBooksOtherTaxCategories` (OEOT), `EBooksRecordTypes` (OREC), `EBooksStampDutyCategories` (OSDC), `ElsterRun` (OELS, ELS1-3), `ISDDocuments` (ISD3), `ImportToExcels` (OIMP, IMP1-3), `OfficialAccountCodes` (OOAC), `PersonalDataRetentionPeriods` (ODRP), `SAFTProductTypes` (OSPT), `SAFTTaxCodes` (OSTX), `SAFTTransactionTypes` (OSTE), `WTaxCategories` (OWCT) and `WTaxRelationTypes` (OWRT). There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** `PersonalFieldsSetups.SysDefined` (the property `IsSystemDefined`) and `TaxDeclarationBoxes.Remarks`. The 2608 returns them; the 2208 tables have no such column, so they are omitted instead of invented.
- **Expected parity differences against the 2608 test company:** `PersonalFieldsSetups` (`IsSystemDefined` only in the 2608) and `TaxDeclarationBoxes` (`Remarks` only in the 2608, and the length of `BoxDefinitionRows`). Both are the two DIFF of the parity run (81 OK, 38 no data, 19 unavailable as designed, 2 DIFF).
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent; the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`). The same defect exists in the 2208 engine. In 2208 `OBOX` has no `Remarks` column, which the connector omits.
- **Physical types specific to 2208** (they only change the informative SQL type of the column, not the JSON): `OCHD` (`ChecksForPaymentDrafts`) has 59 columns, without `EnAcctNum`, `EncryptIV` and `VersionNum`; `BPLName` is `A(100)` here (`A(200)` in 2408 SP) in `OCHD`, `OICD`, `OIOD`, `OIPD`, `OIRI`, `OIRR`, `OISC` and `OISI`, and `VendorName` of `OCHD` is `A(100)`; `OBOX.Formula` (`TaxDeclarationBoxes`) is `A(250)` instead of `ntext`.
- **Sets that the 2208 Service Layer does not expose but the 2408 SP does, not covered by this package (not served):** `CIGCodes`, `CUPCodes`, `DeductibleTaxs`, `Genders`, `SelfCreditMemos`, `SelfInvoices` and `TaxExemptReasons`. They are outside the scope of the extended read, so a request for them answers as in the native Service Layer.
- **Hotfixes:** `B110002208HF1_1` and `B110002208HF_2` (FP2208 HF1 and HF2) are other builds with another build id. This package does not serve them; check the build id of your installation against the one above.
- **Not measured against a real 2208 Service Layer (all of it):** the 38 sets without rows (list below), the order of the child lines of CHD2, FNS1 and MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `PageSize` of `b1s.conf`, `$batch` (not intercepted) and the authorizations of the user. The `$metadata` injected for the extended sets goes at the end of its section; the order of the native entity types, entity sets and complex types of the 2208 Service Layer is not reproduced by the model of this connector (the container of the 2208 Service Layer grows while items are inserted), which does not matter for the injected items. The writer of the `$metadata` v4 of the 2208 Service Layer was checked in its code, not against a live 2208. No end-to-end run against a real 2208 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

`AcknowledgementNumbers`, `BrazilServiceCodes`, `BrazilTaxAdjustments`, `ChecksForPaymentDrafts` (child CHD2), `CountWidgets`, `Counties`, `DataImportTemplates`, `DetailedCheckingRules`, `DistributionLists`, `EJABCompanyDetails`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `FolioNumberingSeries` (FNS1), `GstRegnTypes`, `ISDCreditMemos`, `ISDInvoices`, `ISDRecipientCreditMemos`, `ISDRecipientInvoices`, `IncomingPaymentOrders`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `ManualDistributionRules` (MDR1), `MenuAbbreviations`, `OutgoingPaymentOrders`, `POSCashRegisters`, `PointsOfIssues`, `PostdatedDeposits`, `ProcessChecklistInstances`, `ProcessChecklistTemplates`, `ProductSourceCodes`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances`, `WorkflowTemplateManagers`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves the date as `T00:00:00Z`. Not seen with live data.
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its `PageSize` of `b1s.conf` follows the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and is not measured against a 2208.
- Reads go through SQL: the B1 authorizations of the user are not applied.

## FP2308 (`B11000_2308`)

Build id: `10.0.220.0-7b134f389818-4de0723a3fd8-9a522b22fd4c`. Total share of the 2608 sets: 94.8% (308 native + 126 extended = 434 of 458; the extended read alone adds 27.5%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2308 Service Layer**.

140 sets in the package: 126 served, 14 not available in this build (they answer 400 "Not available in this version"). None of the 126 is exposed under the same name by the 2308 Service Layer, so the extended read serves all of them.

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 86 |
| Not available in this build (400 by design) | 14 |
| Expected differences | 2: `PersonalFieldsSetups` (column `IsSystemDefined` not returned) and `TaxDeclarationBoxes` (column `Remarks` not returned; length of `BoxDefinitionRows`) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below and in [SP2408](#sp2408-b11000sp_2408) and [FP2208](#fp2208-b11000_2208), whose conventions apply here too.

- **Tables that do not exist in 2308 (14 sets not available, they answer 400 "Not available in this version"):** `ATDocumentTypes` (OSDT), `CAEAInformations` (CAEA), `DatevRuns` (ODTV), `DatevTransactionsDeliveriesAndServices` (ODTS), `ElsterRun` (OELS, ELS1-3), `ISDDocuments` (ISD3), `ImportToExcels` (OIMP, IMP1-3), `OfficialAccountCodes` (OOAC), `PersonalDataRetentionPeriods` (ODRP), `SAFTProductTypes` (OSPT), `SAFTTaxCodes` (OSTX), `SAFTTransactionTypes` (OSTE), `WTaxCategories` (OWCT) and `WTaxRelationTypes` (OWRT). There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** `PersonalFieldsSetups.SysDefined` (the property `IsSystemDefined`) and `TaxDeclarationBoxes.Remarks`. The 2608 returns them; the 2308 tables have no such column, so they are omitted instead of invented.
- **Difference with 2208:** 2308 serves five sets more, because their tables exist here: `DeductibleTaxes` (ORTG) and four `EBooks` sets (`EBooksFeesCategories`, `EBooksOtherTaxCategories`, `EBooksRecordTypes`, `EBooksStampDutyCategories`). **Difference with 2408 SP:** 2308 serves five sets less (`ATDocumentTypes` and the three `SAFT*` sets, `OfficialAccountCodes`); against 2402 SP it serves one less (`OfficialAccountCodes`). 126 served here, against 121 in 2208, 127 in 2402 SP and 131 in 2408 SP.
- **Alias:** the engine of this build calls the table `DeductibleTaxs` and the 2608 calls it `DeductibleTaxes`, so the package answers to both names and the 2308 Service Layer already exposes `DeductibleTaxs` natively. Which of the two routes serves a request on a real 2308 session was not measured (the connector decides by the exact name in the `$metadata` of the session).
- **Expected parity differences against the 2608 test company:** `PersonalFieldsSetups` (`IsSystemDefined` only in the 2608) and `TaxDeclarationBoxes` (`Remarks` only in the 2608, and the length of `BoxDefinitionRows`). Both are the two DIFF of the parity run (86 OK, 38 no data, 14 unavailable as designed, 2 DIFF); the 38 sets without rows are the same as in 2208.
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent; the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`). The same defect exists in the 2308 engine. In 2308 `OBOX` has no `Remarks` column, which the connector omits.
- **Physical types specific to 2308** (they only change the informative SQL type of the column, not the JSON): `BPLName` is `A(100)` here (`A(200)` in 2402 SP and 2408 SP) in `OCHD`, `OICD`, `OIOD`, `OIPD`, `OIRI`, `OIRR`, `OISC` and `OISI`, and `VendorName` of `OCHD` is `A(100)`; `OBOX.Formula` (`TaxDeclarationBoxes`) is `ntext` (it is `A(250)` in 2208).
- **Sets that the 2308 Service Layer does not expose but the 2408 SP does, not covered by this package (not served):** `CIGCodes`, `CUPCodes`, `Genders`, `SelfCreditMemos` and `SelfInvoices`. They are outside the scope of the extended read, so a request for them answers as in the native Service Layer.
- **Hotfix:** `B110002308HF_1` (SP2308 HF1) is another build with another build id. This package does not serve it; check the build id of your installation against the one above.
- **Not measured against a real 2308 Service Layer (all of it):** the 38 sets without rows (list below), the order of the child lines of CHD2, FNS1 and MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `PageSize` of `b1s.conf`, `$batch` (not intercepted) and the authorizations of the user. The `$metadata` injected for the extended sets goes at the end of its section; the order of the native entity types, entity sets and complex types of the 2308 Service Layer was not reproduced, which does not matter for the injected items. The rules of the read path were checked in the code of this build, but not all of them instruction by instruction: the order of the child lines (`GOGetArray`) was compared by its sequence of calls and by its result (the 26 child collections are the same as in 2208, 2402 SP and 2408 SP), the writer of the `$metadata` v4 only by the order of its sections, and the user-field JSON serializer and the query handler (`OnQuery`) were not compared in depth. No end-to-end run against a real 2308 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

`AcknowledgementNumbers`, `BrazilServiceCodes`, `BrazilTaxAdjustments`, `ChecksForPaymentDrafts` (child CHD2), `CountWidgets`, `Counties`, `DataImportTemplates`, `DetailedCheckingRules`, `DistributionLists`, `EJABCompanyDetails`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `FolioNumberingSeries` (FNS1), `GstRegnTypes`, `ISDCreditMemos`, `ISDInvoices`, `ISDRecipientCreditMemos`, `ISDRecipientInvoices`, `IncomingPaymentOrders`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `ManualDistributionRules` (MDR1), `MenuAbbreviations`, `OutgoingPaymentOrders`, `POSCashRegisters`, `PointsOfIssues`, `PostdatedDeposits`, `ProcessChecklistInstances`, `ProcessChecklistTemplates`, `ProductSourceCodes`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances`, `WorkflowTemplateManagers`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves the date as `T00:00:00Z`. Not seen with live data.
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its `PageSize` of `b1s.conf` follows the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and is not measured against a 2308.
- Reads go through SQL: the B1 authorizations of the user are not applied.

## FP2202 (`B11000_2202`)

Build id: `10.0.190.0-16386a021d32-3d5447646399-acc3726c1513`. Total share of the 2608 sets: 93.0% (305 native + 121 extended = 426 of 458; the extended read alone adds 26.4%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2202 Service Layer**.

140 sets in the package: 121 served, 19 not available in this build (they answer 400 "Not available in this version"). None of the 121 is exposed by the 2202 Service Layer, so the extended read serves all of them.

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 81 |
| Not available in this build (400 by design) | 19 |
| Expected differences | 2: `PersonalFieldsSetups` (column `IsSystemDefined` not returned) and `TaxDeclarationBoxes` (column `Remarks` not returned; length of `BoxDefinitionRows`) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below and in [SP2408](#sp2408-b11000sp_2408) and [FP2208](#fp2208-b11000_2208), whose conventions apply here too.

- **Tables that do not exist in 2202 (19 sets not available, they answer 400 "Not available in this version"):** `ATDocumentTypes` (OSDT), `CAEAInformations` (CAEA), `DatevRuns` (ODTV), `DatevTransactionsDeliveriesAndServices` (ODTS), `DeductibleTaxes` (ORTG), `EBooksFeesCategories` (OEFC), `EBooksOtherTaxCategories` (OEOT), `EBooksRecordTypes` (OREC), `EBooksStampDutyCategories` (OSDC), `ElsterRun` (OELS, ELS1-3), `ISDDocuments` (ISD3), `ImportToExcels` (OIMP, IMP1-3), `OfficialAccountCodes` (OOAC), `PersonalDataRetentionPeriods` (ODRP), `SAFTProductTypes` (OSPT), `SAFTTaxCodes` (OSTX), `SAFTTransactionTypes` (OSTE), `WTaxCategories` (OWCT) and `WTaxRelationTypes` (OWRT). There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** `PersonalFieldsSetups.SysDefined` (the property `IsSystemDefined`) and `TaxDeclarationBoxes.Remarks`. The 2608 returns them; the 2202 tables have no such column, so they are omitted instead of invented.
- **Expected parity differences against the 2608 test company:** `PersonalFieldsSetups` (`IsSystemDefined` only in the 2608) and `TaxDeclarationBoxes` (`Remarks` only in the 2608, and the length of `BoxDefinitionRows`). Both are the two DIFF of the parity run (81 OK, 38 no data, 19 unavailable as designed, 2 DIFF).
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent; the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`). The same defect exists in the 2202 engine. In 2202 `OBOX` has no `Remarks` column, which the connector omits.
- **Physical types and columns specific to 2202** (they only change the informative SQL type or the nullability of the column, not the JSON; the 26 child collections are identical to 2208): `OICD` (`InventoryCountingDrafts`) has no `UpdateTime` column (2208 has it; the property is not exposed, so nothing is lost); `OPPA.PwdExample` (`PasswordAdministrations`) is `nvarchar(10)` (`nvarchar(128)` in 2208); `EffecDate` and `ReportType` of `BoxSets` are NOT NULL here because they belong to a unique key (nullable in 2208). The rest equals 2208: `OCHD` has 59 columns, without `EnAcctNum`, `EncryptIV` and `VersionNum`; `BPLName` is `A(100)` and `VendorName` of `OCHD` is `A(100)`; `OBOX.Formula` is `A(250)`.
- **Sets that the 2202 Service Layer does not expose but the 2408 SP does, not covered by this package (not served):** `BEMReplicationPeriods`, `CIGCodes`, `CUPCodes`, `DeductibleTaxs`, `Genders`, `SelfCreditMemos`, `SelfInvoices`, `TSRExceptionalEvents` and `TaxExemptReasons`. They are outside the scope of the extended read, so a request for them answers as in the native Service Layer. Against 2208, the 2202 Service Layer also lacks `BEMReplicationPeriods` and `TSRExceptionalEvents`.
- **Hotfix:** `B110002202HF1_1` (FP2202 HF1) is another build with another build id (`10.0.191.0-1a8b2764c5c9-fbd934954ba9-a81ba9b2116f`). This package does not serve it; check the build id of your installation against the one above.
- **Not measured against a real 2202 Service Layer (all of it):** the 38 sets without rows (list below), the order of the child lines of CHD2, FNS1 and MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `PageSize` of `b1s.conf`, `$batch` (not intercepted) and the authorizations of the user. The `$metadata` injected for the extended sets goes at the end of its section; the order of the native entity types, entity sets and complex types of the 2202 Service Layer is not reproduced by the model of this connector (the container of the 2202 Service Layer grows while items are inserted), which does not matter for the injected items. The writer of the `$metadata` v4 of the 2202 Service Layer was checked in its code (identical to 2208), not against a live 2202. No end-to-end run against a real 2202 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

`AcknowledgementNumbers`, `BrazilServiceCodes`, `BrazilTaxAdjustments`, `ChecksForPaymentDrafts` (child CHD2), `CountWidgets`, `Counties`, `DataImportTemplates`, `DetailedCheckingRules`, `DistributionLists`, `EJABCompanyDetails`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `FolioNumberingSeries` (FNS1), `GstRegnTypes`, `ISDCreditMemos`, `ISDInvoices`, `ISDRecipientCreditMemos`, `ISDRecipientInvoices`, `IncomingPaymentOrders`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `ManualDistributionRules` (MDR1), `MenuAbbreviations`, `OutgoingPaymentOrders`, `POSCashRegisters`, `PointsOfIssues`, `PostdatedDeposits`, `ProcessChecklistInstances`, `ProcessChecklistTemplates`, `ProductSourceCodes`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances`, `WorkflowTemplateManagers`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves the date as `T00:00:00Z`. Not seen with live data.
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its `PageSize` of `b1s.conf` follows the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and is not measured against a 2202.
- Reads go through SQL: the B1 authorizations of the user are not applied.

## FP2108 (`B11000_2108`)

Build id: `10.0.170.0-a343684ff5ba-dbb1d3fec4f7-abc37fe997bd`. Total share of the 2608 sets: 92.1% (302 native + 120 extended = 422 of 458; the extended read alone adds 26.2%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2108 Service Layer**.

140 sets in the package: 120 served, 20 not available in this build (they answer 400 "Not available in this version"). None of the 120 is exposed by the 2108 Service Layer, so the extended read serves all of them.

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 71 |
| Not available in this build (400 by design) | 20 |
| Expected differences | 11 (below): 9 because the 2108 writes dates without a time, 3 because of columns that 2108 does not have (`TaxDeclarationBoxes` has both) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. Details are in the sections below and in [SP2408](#sp2408-b11000sp_2408), [FP2208](#fp2208-b11000_2208) and [FP2202](#fp2202-b11000_2202), whose conventions apply here too.

- **Dates are written as `yyyy-mm-dd`, without `T00:00:00Z`.** The serializer of the 2108 Service Layer has no code that appends a time to a date: the literal `T00:00:00Z` is not in its `ServiceLayer.dll` (it appears from 2202 on; the 2111 and earlier builds do not have it either), and the function that writes the properties lacks the branch that adds it (found by decompiling it and comparing it with 2208). The package of this build therefore carries `dateJson: "date-only"` and the connector serves `"RateDate":"2012-02-29"` where the 2608 answers `"2012-02-29T00:00:00Z"`. This applies to every date, including user fields of type D. It is the reason of the date differences in the parity run. Not seen with live data; the type that the 2108 `$metadata` declares for a date (`Edm.DateTimeOffset` in the 2608 contract that is injected) was not compared with a real 2108.
- **Tables that do not exist in 2108 (20 sets not available, they answer 400 "Not available in this version"):** the 19 sets that 2202 and 2208 lack too (`ATDocumentTypes` (OSDT), `CAEAInformations` (CAEA), `DatevRuns` (ODTV), `DatevTransactionsDeliveriesAndServices` (ODTS), `DeductibleTaxes` (ORTG), `EBooksFeesCategories` (OEFC), `EBooksOtherTaxCategories` (OEOT), `EBooksRecordTypes` (OREC), `EBooksStampDutyCategories` (OSDC), `ElsterRun` (OELS, ELS1-3), `ISDDocuments` (ISD3), `ImportToExcels` (OIMP, IMP1-3), `OfficialAccountCodes` (OOAC), `PersonalDataRetentionPeriods` (ODRP), `SAFTProductTypes` (OSPT), `SAFTTaxCodes` (OSTX), `SAFTTransactionTypes` (OSTE), `WTaxCategories` (OWCT) and `WTaxRelationTypes` (OWRT)) plus `BlockExecutables` (OATB), which 2202 serves. There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** `ChecksForPaymentDrafts.ECheck`, `EBooksInvoiceTypes.SubmitedBy` (the property `SubmittedBy`) and `EBooksInvoiceTypes.SelfPrice` (the property `SelfPricing`), `PersonalFieldsSetups.SysDefined` (the property `IsSystemDefined`) and `TaxDeclarationBoxes.Remarks`. The 2608 returns them; the 2108 tables have no such column, so they are omitted instead of invented. The first three are new compared with 2202.
- **Expected parity differences against the 2608 test company (11 DIFF in the parity run: 71 OK, 38 no data, 20 unavailable as designed, 11 DIFF):** dates without `T00:00:00Z` in `ApprovalAuthorizorReplacements`, `BoxSets`, `ExchangeRates`, `ExchangeRatesForReports`, `InterestRates`, `OpeningBalance1099s`, `PaymentWizards`, `TaxDeclarationBoxes` and `Thresholds`; `EBooksInvoiceTypes` (`SubmittedBy` and `SelfPricing` only in the 2608); `PersonalFieldsSetups` (`IsSystemDefined` only in the 2608); `TaxDeclarationBoxes` (also `Remarks` only in the 2608 and the length of `BoxDefinitionRows`). Every difference line of the report is one of these causes.
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent; the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`). The code that decides it (`GOGetArray`, `GOGetArray4SL`, `DefaultGetByKey`) is identical to 2202, so the defect exists in the 2108 engine.
- **Physical types specific to 2108** (they only change the informative SQL type, not the JSON; the 26 child collections have the same joins and order as 2202): the `Fax` column of the child of `DistributionLists` (`DistributionListLines`) is `nvarchar(20)` (`nvarchar(50)` in 2202). The rest equals 2202.
- **Sets that the 2108 Service Layer does not expose, not covered by this package (not served):** `AssetRevaluations`, `BEMReplicationPeriods`, `CIGCodes`, `CUPCodes`, `EventNotifications`, `EventSubscriptions`, `Genders`, `IndiaHsn`, `SBOUDT`, `SelfCreditMemos`, `SelfInvoices`, `ShortLinkMappings`, `TSRExceptionalEvents`, `TaxExemptReasons`, `U_SBOBULK` and `U_SBOCTXT`. They are outside the scope of the extended read, so a request for them answers as in the native Service Layer. Against 2202 the 2108 Service Layer also lacks `AssetRevaluations`, `IndiaHsn` and `ShortLinkMappings`.
- **Hotfix:** `B110002108HF1_1` (FP2108 HF1) is another build with another build id (`10.0.170.0-aafa54020e42-08fbdd25e4d0-3c9f1de0a38f`). This package does not serve it; check the build id of your installation against the one above.
- **Not measured against a real 2108 Service Layer (all of it):** the 38 sets without rows (list below), the order of the child lines of CHD2, FNS1 and MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `PageSize` of `b1s.conf`, `$batch` (not intercepted) and the authorizations of the user. The `$metadata` injected for the extended sets goes at the end of its section; the order of the native entity types, entity sets and complex types of the 2108 Service Layer is not reproduced by the model of this connector, which does not matter for the injected items. The writer of the `$metadata` of the 2108 Service Layer has the same sections and strings as 2202, but its code is restructured (the `ActionImport` and `FunctionImport` blocks of the container appear in another order) and it was compared by its sequence of strings, not instruction by instruction. `OnQuery` of 2108 was not compared in depth. No end-to-end run against a real 2108 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

`AcknowledgementNumbers`, `BrazilServiceCodes`, `BrazilTaxAdjustments`, `ChecksForPaymentDrafts` (child CHD2), `CountWidgets`, `Counties`, `DataImportTemplates`, `DetailedCheckingRules`, `DistributionLists`, `EJABCompanyDetails`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `FolioNumberingSeries` (FNS1), `GstRegnTypes`, `ISDCreditMemos`, `ISDInvoices`, `ISDRecipientCreditMemos`, `ISDRecipientInvoices`, `IncomingPaymentOrders`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `ManualDistributionRules` (MDR1), `MenuAbbreviations`, `OutgoingPaymentOrders`, `POSCashRegisters`, `PointsOfIssues`, `PostdatedDeposits`, `ProcessChecklistInstances`, `ProcessChecklistTemplates`, `ProductSourceCodes`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances`, `WorkflowTemplateManagers`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves only the date (see the first particularity). Not seen with live data.
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its `PageSize` of `b1s.conf` follows the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and is not measured against a 2108.
- Reads go through SQL: the B1 authorizations of the user are not applied.

## FP2105 (`B11000_2105`)

Build id: `10.0.160.0-03077f50d654-fdec5332093a-e8a97efa0067`. Total share of the 2608 sets: 91.0% (297 native + 120 extended = 417 of 458; the extended read alone adds 26.2%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2105 Service Layer**.

140 sets in the package: 120 served, 20 not available in this build (they answer 400 "Not available in this version"). None of the 120 is exposed by the 2105 Service Layer, so the extended read serves all of them. The package has the same sets, unavailable list, omitted columns, child joins and `dateJson` as [FP2108](#fp2108-b11000_2108); the only differences are two tables with fewer physical columns (below).

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 71 |
| Not available in this build (400 by design) | 20 |
| Expected differences | 11 (below): 9 because the 2105 writes dates without a time, 3 because of columns that 2105 does not have (`TaxDeclarationBoxes` has both) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. The conventions of [FP2108](#fp2108-b11000_2108), [SP2408](#sp2408-b11000sp_2408) and [FP2202](#fp2202-b11000_2202) apply here too.

- **Dates are written as `yyyy-mm-dd`, without `T00:00:00Z`.** The literal `T00:00:00Z` is not in the `ServiceLayer.dll` of this build (neither as UTF-8 nor as UTF-16), and the function that writes the properties is instruction-identical to the one of 2108, which lacks the branch that appends the time (compared function by function; the literal appears from 2202 on). The package therefore carries `dateJson: "date-only"` and the connector serves `"RateDate":"2012-02-29"` where the 2608 answers `"2012-02-29T00:00:00Z"`, in every date including user fields of type D. Not seen with live data.
- **Tables that do not exist in 2105 (20 sets not available, they answer 400 "Not available in this version"):** the same 20 as 2108: `ATDocumentTypes`, `BlockExecutables` (OATB), `CAEAInformations`, `DatevRuns`, `DatevTransactionsDeliveriesAndServices`, `DeductibleTaxes`, `EBooksFeesCategories`, `EBooksOtherTaxCategories`, `EBooksRecordTypes`, `EBooksStampDutyCategories`, `ElsterRun`, `ISDDocuments`, `ImportToExcels`, `OfficialAccountCodes`, `PersonalDataRetentionPeriods`, `SAFTProductTypes`, `SAFTTaxCodes`, `SAFTTransactionTypes`, `WTaxCategories` and `WTaxRelationTypes`. There is nothing to serve: the engine of this build has no such table.
- **Columns that do not exist (not announced, not returned):** `ChecksForPaymentDrafts.ECheck`, `EBooksInvoiceTypes.SubmitedBy` (the property `SubmittedBy`) and `EBooksInvoiceTypes.SelfPrice` (the property `SelfPricing`), `PersonalFieldsSetups.SysDefined` (the property `IsSystemDefined`) and `TaxDeclarationBoxes.Remarks`. The 2608 returns them; the 2105 tables have no such column, so they are omitted instead of invented. Same five as 2108.
- **Physical columns that 2105 lacks and 2108 has** (they are not exposed as properties, so the JSON does not change): `LogInstanc`, `UpdateDate` and `UserSign2` in the tables of `ExchangeRates` (ORTT) and `ExchangeRatesForReports` (ORTS). The other 138 sets have the same columns, types and nullability as 2108; the 26 child collections have the same joins and order.
- **Expected parity differences against the 2608 test company (11 DIFF in the parity run: 71 OK, 38 no data, 20 unavailable as designed, 11 DIFF):** dates without `T00:00:00Z` in `ApprovalAuthorizorReplacements`, `BoxSets`, `ExchangeRates`, `ExchangeRatesForReports`, `InterestRates`, `OpeningBalance1099s`, `PaymentWizards`, `TaxDeclarationBoxes` and `Thresholds`; `EBooksInvoiceTypes` (`SubmittedBy` and `SelfPricing` only in the 2608); `PersonalFieldsSetups` (`IsSystemDefined` only in the 2608); `TaxDeclarationBoxes` (also `Remarks` only in the 2608 and the length of `BoxDefinitionRows`). It is the same list as 2108.
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent; the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`). `GOGetArray`, `GOGetArray4SL` and `DefaultGetByKey` are instruction-identical to 2108, so the defect exists in the 2105 engine.
- **Sets that the 2105 Service Layer does not expose, not covered by this package (not served):** `AssetRevaluations`, `BEMReplicationPeriods`, `CIGCodes`, `CUPCodes`, `EBooks`, `EmploymentCategorys`, `EventNotifications`, `EventSubscriptions`, `ExceptionalEvents`, `Genders`, `IndiaHsn`, `PaymentReasonCodes`, `SBOUDT`, `SelfCreditMemos`, `SelfInvoices`, `ShortLinkMappings`, `TSRExceptionalEvents`, `TaxExemptReasons`, `U_SBOBULK`, `U_SBOCTXT` and `WTaxTypeCodes`. They are outside the scope of the extended read, so a request for them answers as in the native Service Layer. Against 2108 the 2105 Service Layer also lacks `EBooks`, `EmploymentCategorys`, `ExceptionalEvents`, `PaymentReasonCodes` and `WTaxTypeCodes`.
- **Hotfix:** `B110002105HF1_1` (FP2105 HF1) is another build with another build id (`10.0.160.0-0081a4cd7c9a-505983efec3f-f8f5f972aa4c`). This package does not serve it; check the build id of your installation against the one above.
- **Not measured against a real 2105 Service Layer (all of it):** the 38 sets without rows (list below), the order of the child lines of CHD2, FNS1 and MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `PageSize` of `b1s.conf`, `$batch` (not intercepted) and the authorizations of the user. The `$metadata` injected for the extended sets goes at the end of its section; the order of the native items of the 2105 Service Layer is not reproduced, which does not matter for the injected ones. The writer of the `$metadata` of the 2105 Service Layer emits the same 88 strings in the same order as 2108 but was not compared instruction by instruction, and `OnQuery` has the same 22 string references as 2108 but was not compared in depth. No end-to-end run against a real 2105 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

`AcknowledgementNumbers`, `BrazilServiceCodes`, `BrazilTaxAdjustments`, `ChecksForPaymentDrafts` (child CHD2), `CountWidgets`, `Counties`, `DataImportTemplates`, `DetailedCheckingRules`, `DistributionLists`, `EJABCompanyDetails`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `FolioNumberingSeries` (FNS1), `GstRegnTypes`, `ISDCreditMemos`, `ISDInvoices`, `ISDRecipientCreditMemos`, `ISDRecipientInvoices`, `IncomingPaymentOrders`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `ManualDistributionRules` (MDR1), `MenuAbbreviations`, `OutgoingPaymentOrders`, `POSCashRegisters`, `PointsOfIssues`, `PostdatedDeposits`, `ProcessChecklistInstances`, `ProcessChecklistTemplates`, `ProductSourceCodes`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances` and `WorkflowTemplateManagers`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves only the date (see the first particularity). Not seen with live data.
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its `PageSize` of `b1s.conf` follows the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and is not measured against a 2105.
- Reads go through SQL: the B1 authorizations of the user are not applied.


## FP2011 (`B11000_2011`)

Build id: `10.0.140.0-8d3df26fae3e-529fef948965-e1cbf097ef30`. Total share of the 2608 sets: 89.7% (295 native + 116 extended = 411 of 458; the extended read alone adds 25.3%; formula in [supported-builds.md](supported-builds.md#extended-support-and--of-2608)).

Status: implemented, **not yet measured against a real 2011 Service Layer**.

140 sets in the package: 116 served, 24 not available in this build (they answer 400 "Not available in this version"). None of the 116 is exposed by the 2011 Service Layer, so the extended read serves all of them. The package is the one of [FP2105](#fp2105-b11000_2105) minus four sets (below): same columns, types, nullability, joins and order in the other 116, same `dateJson`.

| Result against the 2608 test company | Sets |
|---|---|
| Identical JSON, with rows | 68 |
| Not available in this build (400 by design) | 24 |
| Expected differences | 10 (below): 8 because the 2011 writes dates without a time, 3 because of columns that 2011 does not have (`TaxDeclarationBoxes` has both) |
| **Not checked: no rows to compare** | 38 (below) |

### Particularities

Everything specific to this build, in one place. The conventions of [FP2105](#fp2105-b11000_2105), [FP2108](#fp2108-b11000_2108) and [SP2408](#sp2408-b11000sp_2408) apply here too.

- **Dates are written as `yyyy-mm-dd`, without `T00:00:00Z`.** The literal `T00:00:00Z` is not in the `ServiceLayer.dll` of this build (neither as UTF-8 nor as UTF-16). The date writer of that DLL (decompiled) formats with `%.4s-%.2s-%.2s` and appends nothing, and the property writer is the same code as in 2108 (compared after normalising addresses). The package therefore carries `dateJson: "date-only"` and the connector serves `"RateDate":"2012-02-29"` where the 2608 answers `"2012-02-29T00:00:00Z"`, in every date including user fields of type D. Not seen with live data.
- **Tables that do not exist in 2011 (23 sets not available, they answer 400 "Not available in this version"):** the 20 of 2105 (`ATDocumentTypes`, `BlockExecutables`, `CAEAInformations`, `DatevRuns`, `DatevTransactionsDeliveriesAndServices`, `DeductibleTaxes`, `EBooksFeesCategories`, `EBooksOtherTaxCategories`, `EBooksRecordTypes`, `EBooksStampDutyCategories`, `ElsterRun`, `ISDDocuments`, `ImportToExcels`, `OfficialAccountCodes`, `PersonalDataRetentionPeriods`, `SAFTProductTypes`, `SAFTTaxCodes`, `SAFTTransactionTypes`, `WTaxCategories`, `WTaxRelationTypes`) plus `ApprovalAuthorizorReplacements` (OAAR), `LegalTextFormats` (OLTF, LTF1) and `LegalTextItems` (OLTI), which exist from 2105. There is nothing to serve: the engine of this build has no such table.
- **A set whose key column does not exist (1 more set not available): `SpecificWTHAmountsService`.** Its table OSWA exists but has no `CUSplit` and no `SmRnNtWTx`, and `CUSplit` is part of the key of the 2608 contract (`PaymentReasonCode`, `CardCode`, `CUSplit`); in 2011 the PRIMARY key of OSWA is only (`PymntRsnCd`, `CardCode`). With another key the contract of the 2608 would not hold (key, `$metadata`, `GetByKey`), and the contract of a set that only the 2608 has cannot be read from the 2011 DLL, so the set is not served instead of serving it with a shortened key. It answers 400 "Not available in this version ... table OSWA lacks the columns CUSplit, SmRnNtWTx". The rule in the generator is general: a set is unavailable when a column of its key is absent from the table of the build.
- **Columns that do not exist (not announced, not returned):** `ChecksForPaymentDrafts.ECheck`, `EBooksInvoiceTypes.SubmitedBy` (the property `SubmittedBy`) and `EBooksInvoiceTypes.SelfPrice` (the property `SelfPricing`), `PersonalFieldsSetups.SysDefined` (the property `IsSystemDefined`) and `TaxDeclarationBoxes.Remarks`. The 2608 returns them; the 2011 tables have no such column, so they are omitted instead of invented. Same five as 2105.
- **Expected parity differences against the 2608 test company (10 DIFF in the parity run: 68 OK, 38 no data, 24 unavailable as designed, 10 DIFF):** dates without `T00:00:00Z` in `BoxSets`, `ExchangeRates`, `ExchangeRatesForReports`, `InterestRates`, `OpeningBalance1099s`, `PaymentWizards`, `TaxDeclarationBoxes` and `Thresholds`; `EBooksInvoiceTypes` (`SubmittedBy` and `SelfPricing` only in the 2608); `PersonalFieldsSetups` (`IsSystemDefined` only in the 2608); `TaxDeclarationBoxes` (also `Remarks` only in the 2608 and the length of `BoxDefinitionRows`). It is the list of 2105 without `ApprovalAuthorizorReplacements`, which 2011 does not have.
- **Engine defects replicated, not fixed:** in `TaxDeclarationBoxes` the child BOX1-4 is joined by position and, because its PRIMARY key has another order, it joins `SeqNum`/`Account`/`Value` with `BosCode` of the parent; the length of `BoxDefinitionRows` follows that join (`engineQuirk` `join-posicional-orden-distinto`). `GOGetArray`, `GOGetArray4SL` and `DefaultGetByKey` are instruction-identical to 2105, so the defect exists in the 2011 engine.
- **Sets that the 2011 Service Layer does not expose, not covered by this package (not served):** `AssetRevaluations`, `BEMReplicationPeriods`, `CESTCodes`, `CIGCodes`, `CUPCodes`, `EBooks`, `EmploymentCategorys`, `EventNotifications`, `EventSubscriptions`, `ExceptionalEvents`, `Genders`, `IndiaHsn`, `PaymentReasonCodes`, `SBOUDT`, `SelfCreditMemos`, `SelfInvoices`, `ShortLinkMappings`, `TSRExceptionalEvents`, `TaxExemptReasons`, `TaxReplStateSubs`, `U_SBOBULK`, `U_SBOCTXT` and `WTaxTypeCodes`. They are outside the scope of the extended read, so a request for them answers as in the native Service Layer. Against 2105 the 2011 Service Layer also lacks `CESTCodes` and `TaxReplStateSubs`.
- **Hotfixes:** `B1100002011HF_1` (FP2011 HF1, `10.0.141.0-4c400c268031-48c9cfd2d7b2-6a7f4b0d4ee4`) and `B1100002011HF_2` (FP2011 HF2, `10.0.141.0-2fda1c6773f6-b50a9de0fcc6-d95b8015a90a`) are other builds with other build ids. This package does not serve them; check the build id of your installation against the one above.
- **Not measured against a real 2011 Service Layer (all of it):** the 38 sets without rows (list below, the same as 2105), the order of the child lines of CHD2, FNS1 and MDR1, user fields D with time, N with time and B with another edit type, `image` columns, dates with a time, `PageSize` of `b1s.conf`, `$batch` (not intercepted) and the authorizations of the user. The `$metadata` injected for the extended sets goes at the end of its section; the order of the native items of the 2011 Service Layer is not reproduced, which does not matter for the injected ones. The `DataToDAG` function of the 2011 engine (which drops the time of a date column) is compiled differently from 2105 (other registers and loop shape): it uses the same `%4.4d%2.2d%2.2d` format but was not shown equivalent instruction by instruction. The writer of the `$metadata` and `OnQuery` of the 2011 Service Layer were not compared. No end-to-end run against a real 2011 yet.

### Sets whose retrieval could not be checked for lack of data

For these the empty answer is identical to the native one, but the values (types, dates, enums, nulls, child lines, order) were never compared. If a customer uses one, test it.

`AcknowledgementNumbers`, `BrazilServiceCodes`, `BrazilTaxAdjustments`, `ChecksForPaymentDrafts` (child CHD2), `CountWidgets`, `Counties`, `DataImportTemplates`, `DetailedCheckingRules`, `DistributionLists`, `EJABCompanyDetails`, `FixedAssetFiscalYearChanges`, `FolioNumberingDocuments`, `FolioNumberingSeries` (FNS1), `GstRegnTypes`, `ISDCreditMemos`, `ISDInvoices`, `ISDRecipientCreditMemos`, `ISDRecipientInvoices`, `IncomingPaymentOrders`, `InventoryCountingDrafts`, `InventoryOpeningBalanceDrafts`, `InventoryPostingDrafts`, `ItemMaterialTypes`, `MRPScenarios`, `ManualDistributionRules` (MDR1), `MenuAbbreviations`, `OutgoingPaymentOrders`, `POSCashRegisters`, `PointsOfIssues`, `PostdatedDeposits`, `ProcessChecklistInstances`, `ProcessChecklistTemplates`, `ProductSourceCodes`, `RecurringTransactionTemplates`, `RetailChains`, `UnencumberedICMSExemptionReasons`, `WorkflowInstances` and `WorkflowTemplateManagers`.

The order of the child lines of CHD2, FNS1 and MDR1 follows the rule decompiled from the engine, but it was not seen working: those are the first to check.

### Other things not measured

- User fields (`U_*`): the JSON rule is decompiled for every type, but the test company only has types A, B (scale), D, M and N. Types D with time, N with time (`TimeOfDay`) and B with another edit type were not seen working; a column whose physical type does not match the rule answers 501 instead of a guess.
- The JSON of `image` columns and filters not measured answer 501 by design, and so does a `$filter` with a date literal that has a time other than 00:00:00.
- Dates: the native Service Layer drops the time of a `datetime` column when it loads the value, so the connector serves only the date (see the first particularity). Not seen with live data.
- `$batch` is not intercepted: an extended set read inside a `$batch` goes to the Service Layer, which answers as if the set did not exist. MCP clients read with plain GET.
- The `$metadata` injected for the extended sets has no navigation properties, bindings or user fields; its `PageSize` of `b1s.conf` follows the same conventions and the same manual option as in [SP2408](#sp2408-b11000sp_2408), and is not measured against a 2011.
- Reads go through SQL: the B1 authorizations of the user are not applied.
