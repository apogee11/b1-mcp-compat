# Check your build

The connector's compatibility is declared as **release + exact build**, for example FP2608 and a given build id. The connector cannot detect your build: the Service Layer `Login` answers only `Version` (for example `1000340` for FP2608), the same for every patch level. So **you declare the release and the build to the connector** with two parameters, and the connector loads the knowledge base that matches them.

## Why it matters

The standard dictionary (labels, tables, column sizes and types, enum values) is mapped from one specific build. Between two releases 0.2 to 0.8 % of the columns they share change and thousands are added. A patch inside a release can change the dictionary too. A wrong size, label or type reaches the AI as a wrong annotation, so the connector serves only builds it has a knowledge base for, unless you tell it otherwise.

## Steps (IT manager)

You do not generate or compile anything.

1. On the Service Layer server run (read-only, no credentials, no network; copy `tools/collect/check-build.ps1` and `docs/covered-builds.json` there, or run it from a clone):

   ```powershell
   .\tools\collect\check-build.ps1 -ServiceLayerPath 'C:\Program Files\sap\SAP Business One\ServerTools\ServiceLayer'
   ```

   It prints the build id of your Service Layer, compares them with the [covered builds](../supported-builds.md) and ends with the values for the connector:

   ```
   B1McpCompat__Release = 2608
   B1McpCompat__Build   = 10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194
   ```

   The build id is an opaque value that identifies the exact build; copy it as printed.

2. Set them as environment variables of the connector (`B1McpCompat__Release`, `B1McpCompat__Build`; see the configuration table in the [README](../../compat/README.md#configuration)).

3. Read the result of the script:

| Result | Meaning | What to do |
|---|---|---|
| `COINCIDE` | Your files are the ones a knowledge base was extracted from. | Nothing more. The connector serves it as verified. |
| `DISTINTA` | Same file version as a covered release, different files (another patch). | Contact the maintainer (below). |
| `NO CUBIERTA` | No covered release has your file version. | Contact the maintainer (below). |

## If your build or release is not covered

The connector answers the metadata requests with HTTP 502 and always the same message:

> b1-mcp-compat: this Service Layer release or build is not supported: there is no knowledge base for it. Contact the maintainer of this repository and send the output of tools/collect/check-build.ps1 (file versions and SHA-256 hashes) so that the knowledge base for your build is generated. To keep working meanwhile with the knowledge base of the same release, without verification, set B1McpCompat__AllowUnsupportedBuild=true (this does not help for a release that has no knowledge base at all).

- **Contact the maintainer of this repository** and send the output of `check-build.ps1`. The maintainer is the only one who generates knowledge bases: they extract the dictionary from the same files, verify it and add the build to the [covered builds](../supported-builds.md).
- **Meanwhile**, if your release has a knowledge base (same release, slightly different build), you may set `B1McpCompat__AllowUnsupportedBuild=true`. The connector then does not fail: it serves the knowledge base of that release and writes a warning to the log (declared build, build of the knowledge base used, "not verified"). The differences are expected to be few, but they are not measured for your patch. If a 502 names a property, your build has it and the knowledge base lacks it. For a release with no knowledge base at all (for example one newer than FP2608) this setting changes nothing: there is nothing to serve.

## Other errors

| Situation | Behaviour |
|---|---|
| `B1McpCompat__Release` or `B1McpCompat__Build` with a wrong format | The connector does not start and says which one. |
| The `Login` Version does not belong to `B1McpCompat__Release` (you declared FP2608 but the Service Layer is FP2408) | HTTP 502 naming both, always, also with `B1McpCompat__AllowUnsupportedBuild=true`: serving another release's dictionary is wrong in known places. |
| `B1McpCompat__Build` is the build of a different release than the one in use | HTTP 502 (contradictory declaration), also with `AllowUnsupportedBuild`. |
| `B1McpCompat__Release` not set | The release is the one of the `Login` Version. |
| `B1McpCompat__Build` not set | The build counts as not declared: unsupported (fixed message) unless `AllowUnsupportedBuild=true`. |

Requests that need no knowledge base (everything the connector passes through) are not affected.
