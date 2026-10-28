# SAP Business One 10.0 downloads

Source: SAP Software Center, SAP Business One 10.0 installations (46 packages). Each file is `<zip>-70004131.ZIP`, except the SP00 upgrades, which end in `-70004131_P1.ZIP`.

**Supported**: a knowledge base was generated from that exact package; it is served as is. **Verified**: also measured against a real Service Layer of that build ; otherwise `no`. `not planned`: will not be supported. **Build id**: value for `B1McpCompat__Build` (see `tools/collect/check-build.ps1`).

**Extended support**: the build also serves the sets its own Service Layer does not expose, from the read package embedded in the connector (`read-<package>.json.gz`). `yes` links to the particularities of that build in [build-notes.md](build-notes.md); read them before relying on a set.

### Extended support and % of 2608

**% of 2608** = total sets the build serves / sets of the native 2608 Service Layer. It is the figure to give a customer: "for your version we cover about X% of the 2608 interface".

- Numerator: the sets the build's own Service Layer already exposes (`sets` of its knowledge base, `kb-<package>.json.gz`) plus, if it has extended support, the sets served from `read-<package>.json.gz`. A set in both is counted once, and only if 2608 has a set with that name.
- Denominator: **458** EntitySets of the `$metadata` of the Service Layer FP2608 (`oracle-2608.json.gz`, captured from the test company).
- SP2408: 313 native + 131 extended = 444 / 458 = **96.9%** (native alone: 313 / 458 = 68.3%). The package has 140 extended sets; 9 are not available because their tables do not exist in that build (see [build-notes.md](build-notes.md)). 5 sets of 2608 are in neither list (`EventNotifications`, `EventSubscriptions`, `SBOUDT`, `U_SBOBULK`, `U_SBOCTXT`).
- Limits: the native part comes from the knowledge base of the build (what its engine declares), not from a Service Layer of that build; a set renamed between releases (`DeductibleTaxs` in 2305-2408, `DeductibleTaxes` in 2608) counts as missing unless the read package serves it. The extended part is "implemented, not yet measured against a real Service Layer of that build".
- The column is filled by `list_builds.py` from the knowledge bases and the read packages in `compat/src/B1.Mcp.Compat.Host/Resources/`, and `coverage.svg` (green cells for extended support, `#2ea043`) by `coverage_map.py`, so a new build appears in both when they are re-run.

> Versions older than `B11000_2008-70004131.ZIP` (Upgrade 1, Upgrade 2 and Patch 0) will not be supported.

| # | Package | Description | Type | Release date | Size (KB) | Build id | Supported | Verified | Extended support | % of 2608 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `B110002608HF_1` | FP2608 HF1 | hotfix | 2026-09-28 | 9,249,774 | `10.0.340.0-7955e360cba0-edd791a0c575-78d6096cf00a` | yes | no | no | 99.3% |
| 2 | `B11000_2608` | FP2608 | base | 2026-08-21 | 9,240,775 | `10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194` | yes | yes | no | 99.3% |
| 3 | `B110002605HF_1` | SP2605 HF1 | hotfix | 2026-06-25 | 9,455,165 | `10.0.331.0-09a205f1e40b-2d2625c56c99-823adeab8d59` | yes | no | no | 72.3% |
| 4 | `B11000SP_2605` | SP2605 | base | 2026-05-08 | 9,428,328 | `10.0.330.0-47b4ea54a506-216ffe3a90ed-825e572978fd` | yes | no | [yes](build-notes.md#sp2605-b11000sp_2605) | 98.9% |
| 5 | `B110002602HF_1` | FP2602 HF1 | hotfix | 2026-04-06 | 9,321,500 | `10.0.321.0-489d5b52d4ef-29d40c83424c-2c40c492f212` | yes | no | no | 72.3% |
| 6 | `B11000_2602` | FP2602 | base | 2026-02-03 | 9,324,393 | `10.0.320.0-1dbf7e7cfc75-a13b197811f4-92a0ab6f1a33` | yes | no | [yes](build-notes.md#fp2602-b11000_2602) | 98.9% |
| 7 | `B110002511HF_1` | SP2511 HF1 | hotfix | 2025-12-12 | 9,143,904 | `10.0.311.0-6996db56b83e-643202867cee-196b1b2d2ccb` | yes | no | no | 69.7% |
| 8 | `B11000SP_2511` | SP2511 | base | 2025-11-04 | 9,148,147 | `10.0.310.0-ee6913dffe5b-7b4838227ade-e4634e8a197c` | yes | no | [yes](build-notes.md#sp2511-b11000sp_2511) | 97.8% |
| 9 | `B11000_2508` | FP2508 | base | 2025-08-29 | 9,031,483 | `10.0.300.0-9569a4cb63be-521580d249f8-fe631fa0592c` | yes | no | no | 69.7% |
| 10 | `B110002505HF_1` | SP2505 HF1 | hotfix | 2025-07-29 | 9,012,569 | `10.0.291.0-5666ed630281-53c79ec4fbbc-b6580c59ecbe` | yes | no | no | 69.2% |
| 11 | `B11000SP_2505` | SP2505 | base | 2025-05-16 | 9,016,125 | `10.0.290.0-c9ae21daeed4-55fe44b82e4d-3443b8dca453` | yes | no | [yes](build-notes.md#sp2505-b11000sp_2505) | 97.2% |
| 12 | `B110002502HF_1` | FP2502 HF1 | hotfix | 2025-04-01 | 8,944,194 | `10.0.281.0-1bd67d9f1029-b1c83adcb76f-03936fb83dc1` | yes | no | no | 69.2% |
| 13 | `B11000_2502` | FP2502 | base | 2025-02-28 | 8,943,202 | `10.0.280.0-2eff74abbfbb-9d2a66382a73-68f879d84009` | yes | no | [yes](build-notes.md#fp2502-b11000_2502) | 97.2% |
| 14 | `B11000SP_2411` | SP2411 | base | 2024-11-15 | 8,897,321 | `10.0.270.0-78f27b8b9e29-79b907e3590e-ba272e18fe5c` | yes | no | no | 68.3% |
| 15 | `B110002408HF_1` | SP2408 HF1 | hotfix | 2024-10-25 | 8,752,012 | `10.0.261.0-e44bfcceec1c-fb0fef35fc12-3f10840fe99a` | yes | no | no | 68.3% |
| 16 | `B11000SP_2408` | SP2408 | base | 2024-08-23 | 8,748,689 | `10.0.260.0-ea80448581ff-fe6cbe19acb9-b03b314fdd6c` | yes | no | [yes](build-notes.md#sp2408-b11000sp_2408) | 96.9% |
| 17 | `B110002405HF_1` | FP2405 HF1 | hotfix | 2024-08-01 | 8,757,335 | `10.0.251.0-fceb66cf007a-84dfbcbf9907-1dbfe4d72949` | yes | no | no | 68.1% |
| 18 | `B11000_2405` | FP2405 | base | 2024-05-31 | 8,758,780 | `10.0.250.0-537dc1b598f3-965643cd4ef9-1ac38b8075b1` | yes | no | no | 68.1% |
| 19 | `B110002402HF_1` | SP2402 HF1 | hotfix | 2024-04-04 | 8,666,692 | `10.0.240.0-cfc78a25a91a-565dd22ffdb5-b08d9f7e79be` | yes | no | no | 67.7% |
| 20 | `B11000SP_2402` | SP2402 | base | 2024-02-02 | 8,666,143 | `10.0.240.0-b04e4db08c43-2bd6f6550ccb-2a7e7add7c82` | yes | no | [yes](build-notes.md#sp2402-b11000sp_2402) | 95.4% |
| 21 | `B110002311HF_1` | SP2311 HF1 | hotfix | 2024-01-24 | 8,689,832 | `10.0.230.0-89eae06e805b-d6a0b87cb2cb-2e429d562882` | yes | no | no | 67.7% |
| 22 | `B11000SP_2311` | SP2311 | base | 2023-12-15 | 8,687,849 | `10.0.230.0-678fafff8d3b-91016ef69a32-247035d964c4` | yes | no | no | 67.7% |
| 23 | `B110002308HF_1` | SP2308 HF1 | hotfix | 2023-10-27 | 8,317,554 | `10.0.220.0-628c9551d6c5-b23ed4cfbec8-712ee519faee` | yes | no | no | 67.2% |
| 24 | `B11000_2102` | FP2102 | base | 2023-09-25 | 7,869,538 | `10.0.150.0-dfa5d318c200-c420b722fa92-bad9538d3c9d` | yes | no | no | 64.8% |
| 25 | `B11000_2105` | FP2105 | base | 2023-09-23 | 8,315,774 | `10.0.160.0-03077f50d654-fdec5332093a-e8a97efa0067` | yes | no | [yes](build-notes.md#fp2105-b11000_2105) | 91.0% |
| 26 | `B11000_2011` | FP2011 | base | 2023-09-23 | 8,069,800 | `10.0.140.0-8d3df26fae3e-529fef948965-e1cbf097ef30` | yes | no | [yes](build-notes.md#fp2011-b11000_2011) | 89.7% |
| 27 | `B11000_2208` | FP2208 | base | 2023-09-22 | 8,903,314 | `10.0.200.0-24aeb23de137-2eb48c85224f-c15ab35d594d` | yes | no | [yes](build-notes.md#fp2208-b11000_2208) | 93.4% |
| 28 | `B11000_2202` | FP2202 | base | 2023-09-22 | 8,545,245 | `10.0.190.0-16386a021d32-3d5447646399-acc3726c1513` | yes | no | [yes](build-notes.md#fp2202-b11000_2202) | 93.0% |
| 29 | `B11000_2111` | FP2111 | base | 2023-09-22 | 8,250,636 | `10.0.180.0-aa308db3b547-227cb5ad83ae-4ff91d5b89ef` | yes | no | no | 65.9% |
| 30 | `B11000_2108` | FP2108 | base | 2023-09-22 | 8,310,235 | `10.0.170.0-a343684ff5ba-dbb1d3fec4f7-abc37fe997bd` | yes | no | [yes](build-notes.md#fp2108-b11000_2108) | 92.1% |
| 31 | `B11000_2308` | FP2308 | base | 2023-09-21 | 8,311,364 | `10.0.220.0-7b134f389818-4de0723a3fd8-9a522b22fd4c` | yes | no | [yes](build-notes.md#fp2308-b11000_2308) | 94.8% |
| 32 | `B11000_2305` | FP2305 | base | 2023-09-21 | 8,455,900 | `10.0.210.0-86df99d89132-3aade838ff2c-bafcc6c5b722` | yes | no | no | 67.2% |
| 33 | `B11000_2008` | FP2008 | base | 2023-09-21 | 8,031,155 | `10.0.130.0-57af7b02a946-4e49edb84071-704c80750289` | yes | no | no | 63.5% |
| 34 | `B110002305HF_1` | FP2305 HF1 | hotfix | 2023-08-11 | 8,455,987 | `10.0.210.0-4d0d2126916e-293599b7ac8f-09b27b87ee28` | yes | no | no | 67.2% |
| 35 | `B110002208HF_2` | FP2208 HF2 | hotfix | 2023-05-18 | 8,896,832 | `10.0.201.0-83c8fbccd13f-9a5b27475727-e5989639f365` | yes | no | no | 67.0% |
| 36 | `B110002208HF1_1` | FP2208 HF1 | hotfix | 2023-04-22 | 8,896,958 | `10.0.201.0-18dd93ed5a64-b00c425dbf81-0c421abc1e6e` | yes | no | no | 67.0% |
| 37 | `B110002202HF1_1` | FP2202 HF1 | hotfix | 2022-08-24 | 8,553,241 | `10.0.191.0-1a8b2764c5c9-fbd934954ba9-a81ba9b2116f` | yes | no | no | 66.6% |
| 38 | `B1100002111HF_1` | FP2111 HF1 | hotfix | 2022-05-27 | 8,271,846 | `10.0.181.0-86447be3e17b-ca3553afc6d1-74a4276eedf3` | yes | no | no | 66.2% |
| 39 | `B110002108HF1_1` | FP2108 HF1 | hotfix | 2021-10-01 | 8,309,952 | `10.0.170.0-aafa54020e42-08fbdd25e4d0-3c9f1de0a38f` | yes | no | no | 65.9% |
| 40 | `B110002105HF1_1` | FP2105 HF1 | hotfix | 2021-08-02 | 8,315,112 | `10.0.160.0-0081a4cd7c9a-505983efec3f-f8f5f972aa4c` | yes | no | no | 64.8% |
| 41 | `B1100002011HF_2` | FP2011 HF2 | hotfix | 2021-03-05 | 8,070,020 | `10.0.141.0-2fda1c6773f6-b50a9de0fcc6-d95b8015a90a` | yes | no | no | 64.4% |
| 42 | `B1100002011HF_1` | FP2011 HF1 | hotfix | 2021-01-29 | 8,070,094 | `10.0.141.0-4c400c268031-48c9cfd2d7b2-6a7f4b0d4ee4` | yes | no | no | 64.4% |
| 43 | `B110002008HF1_1` | FP2008 HF1 | hotfix | 2020-10-13 | 8,025,009 |  | no | no | no | - |
| 44 | `B11000_2` | Upgrade 2 | base | 2020-05-04 | 7,992,995 |  | not planned | no | no | - |
| 45 | `B11000_1` | Upgrade 1 | base | 2020-01-17 | 7,593,769 |  | not planned | no | no | - |
| 46 | `B11000P_0` | Patch 0 | base | 2019-11-18 | 6,811,049 |  | not planned | no | no | - |
