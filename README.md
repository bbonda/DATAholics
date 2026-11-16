# AccurateHire IQ

Windows desktop screening-operations application, built with C# / .NET 8, WPF and MVVM. Source screening decisions remain authoritative; the application does not decide eligibility.

## Start

For a source checkout from GitHub, run `Build.ps1` first with the .NET 8 SDK installed. The generated `Application` folder is intentionally excluded from Git history. Synthetic sample workbooks are included; user databases, API credentials and logs are not.

Double-click **Launch AccurateHire IQ.cmd**, or open **Application/AccurateHireIQ.exe**. The published Windows x64 application includes its .NET runtime. Windows 10 version 2004 or later / Windows 11 is required. Keep the Application folder and its Data folder together. The first launch imports the supplied sample automatically.

## Your supplied workbook

The latest workbook contains 1,000 records and 57 columns, including the overall-result column Final. The earlier check-only version has 56 columns. Neither version has a Candidate ID or job-role column. Individual check fields contain values such as Pass, references to other findings and finding descriptions. These are not overall decisions.

The latest **Final** column gives **700 PASS, 129 REVIEW, 171 STOP, and 0 UNCLASSIFIED**. Final is automatically mapped as the authoritative overall result. The original check-only workbook gives **700 PASS, 100 REVIEW and 200 REJECT (STOP)** under the roll-up rule. Exactly 29 records have Final=Review but an explicit Hard Stop in the violent-felony check; these are flagged as conflicting fields and Final is preserved. All 50 screening-check columns are evaluated; the six personal-information columns are excluded. Missing candidate IDs do not prevent classification. Batch ID, worksheet and Excel row remain the technical identifiers.

## Classification rules (updated)

When no overall-result column is mapped:

1. Any check explicitly labelled **STOP**, **Hard Stop**, **Reject** or **Rejected** puts the record in the **REJECT (STOP)** workflow queue, even if another check says Review or Pass.
2. Otherwise, an explicit **Review** check puts the record in **REVIEW**.
3. Otherwise, **every recognized screening check must explicitly say Pass** for **PASS**.
4. Blank, unknown or unresolved check values go to **REVIEW** for clarification. With no recognized check columns at all, the record remains **UNCLASSIFIED**.

Labels are case-insensitive and whitespace is trimmed. Status labels may be followed by a colon, dash, semicolon or slash and a source explanation, as in `Hard Stop: active CDL suspension`. A substring in arbitrary text, a column heading, or personal information never creates a STOP result. `See finding 35` does not itself mean Stop; explicit check labels elsewhere in the same row determine precedence. If there is only an unresolved reference, review is required. The application does not infer a result from criminal/medical narrative or the meaning of a check's title.

A mapped overall-result column remains authoritative; invalid or empty values in that mapped column remain UNCLASSIFIED. Leave this mapping blank only when you want check-level roll-up instead of the supplied Final result. Existing source values are never rewritten. The displayed Reject category is a screening workflow grouping of explicit source stop flags; it does not send rejection notices or execute an employment decision.

The update automatically reclassifies previously imported, UNCLASSIFIED records without an overall-result column. It preserves all source rows, original values, and existing non-UNCLASSIFIED results. Migration is transactional and idempotent. Re-uploading is unnecessary. Candidate details show classification basis, explanation and exact triggering columns/values. Exports append classification, basis and explanation after every original column.

## Use

1. **Upload Excel:** browse or drop XLSX/XLS/CSV, select a worksheet, inspect six preview records and map columns. Name mapping is required. Missing IDs are flagged; an absent overall-result mapping enables check-level classification. Every nonempty source row is retained; duplicates are not removed. Cancel rolls back the import.
2. **Dashboard:** cards open the corresponding candidate list. Charts show actual classification distributions and detected follow-up categories. A missing job role appears as Not supplied.
3. **Candidates:** combine status, role, location, finding, source-column/value and search filters. Blank filters mean all. Search checks ID, name and remarks. Apply filters; choose 25/50/100 rows per page. Sorting runs over all matching database rows. Double-click for original fields, source reference and calculated findings.
4. **AI Analysis Center:** view calculated summary, up to 500 evidence rows and actual execution history. Source findings and deterministic flags are clearly separate from AI-generated chat output. Blank verification fields require applicability review, not a negative decision.
5. **Reports:** export all records, a category, the complete filtered result, or an operational workbook containing every candidate and every finding. XLSX/CSV exports preserve original column values as text and append classification metadata; formula-like text is safely quoted. Export covers every matching page. Large XLSX output uses ClosedXML and needs memory proportional to the exported workbook; CSV is the lower-memory option.
6. **Upload History:** switch between independent batches or explicitly delete the active batch. Counts never combine batches silently.

Duplicate or empty spreadsheet headers are disambiguated during import so each physical column is retained. Entirely blank rows are ignored. Formula cells are read through their stored values; the application does not evaluate source Excel formulas.

## Configure the real AI agent

Microsoft Agent Framework is integrated, with 14 typed C# tools. The model selects tools dynamically; there is no simulated chatbot or hardcoded question-answer routing. Dashboard/import/export/local findings work without a model.

In **Settings**, set the endpoint and a tool-capable model:

- **Ollama:** `http://localhost:11434/v1`, using the exact name of an already installed model that supports tool calling. No API key is normally required.
- **Azure OpenAI v1:** `https://YOUR-RESOURCE.openai.azure.com/openai/v1/`, deployment name and API key.
- **Compatible provider:** the provider's HTTPS chat-completions base endpoint, model name and key.

Save settings. External endpoints are blocked until you enable **Allow external AI transmission**. That consent covers your chat messages, aggregates, IDs and bounded source excerpts sent to that configured endpoint. Changing provider settings resets the agent session. API keys are encrypted with Windows DPAPI for the current user; leaving the password field blank preserves the existing key.

Examples:

- “Analyze this upload and distinguish missing overall results from individual findings.”
- “How many candidates have pending verification tasks?”
- “Explain the source findings for row 3; cite the original columns.”
- “Prepare a REVIEW report with pending driving license verification.”

For specific check filtering, the agent can inspect source column names, combine exact source-column matching with a text-contains filter, then stage the report. Click **Review prepared report**, inspect filters and records, then **Export all matches**. Tool calls cannot save files, change statuses or make decisions. Tool results include exact totals plus explicitly bounded samples. A timeout is configurable from 15–180 seconds; output is bounded to 1,800 tokens per model call, evidence to 20–30 records per tool, and tool execution to 12 calls per request. The UI permits one operation at a time. Provider SDK transport retries remain within the request timeout.

Tool loop integration was tested with a local protocol fixture that requested a real database tool and consumed its result. No live-model quality, Azure credentials or installed Ollama model was available for end-to-end inference testing. Provider compatibility depends on its tool-calling support.

## Demonstration

`Data/Demo-10000.xlsx` is a separate fictional dataset with explicit source statuses: **7,500 PASS / 2,000 REVIEW / 500 STOP**. Import it to demonstrate category drill-down and export. This dataset does not replace or reinterpret your sample. Reports can also generate a new copy of the 10,000-row demo. It includes missing employment fields and pending license checks.

Suggested demo: import the demo workbook → click REVIEW → filter Pending verification → export all matches → configure a model → ask it to prepare the same report → inspect actual execution history and source references.

## Storage and security scope

Data is stored at `%LOCALAPPDATA%\AccurateHireIQ\accuratehire.db`. Logs are local, rotate daily and retain 14 files. Original fields, batches, findings, agent execution metadata, generated-report metadata and settings have persistent SQLite tables. Indexes support status/ID/role lookups and page queries.

This is a **single-user hackathon application**, not a production HR authorization system. Name masking is a display convenience, and the export toggle is a local workflow control, not role-based access control. Preview and exported original columns include personal fields. Tool output excludes dedicated name, gender, contact and address columns; regex masking in free-text excerpts is best effort and is not comprehensive de-identification. Review source text before external transmission. Spreadsheet content is treated as untrusted evidence and cannot register new tools or grant filesystem access.

The database is not encrypted at rest. Protect the Windows account and use device encryption for sensitive data. There is no enterprise login, role management, automated retention schedule, tamper-proof audit trail, signed installer or dark theme. Manual batch deletion is provided; previously exported reports and filesystem backups are not deleted. Consequential screening and employment decisions remain outside this application with authorized people.

## Build and verification

The source is in this folder. Install the .NET 8 SDK, then run `Build.ps1`, or:

```powershell
dotnet publish AccurateHireIQ.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Application
```

Dependencies are pinned in the project and NuGet lock file. Core packages: Microsoft Agent Framework, Microsoft.Data.Sqlite, ExcelDataReader (streamed XLS/XLSX input), ClosedXML (output), CsvHelper, LiveCharts2, WPF UI, DI and Serilog. The application uses explicit styles for its dashboard and data tables.

To run isolated integration checks, set `ACCURATEHIRE_DATA` to a new scratch directory and execute `Application/AccurateHireIQ.exe --self-test`. It writes `verification.json` there. Do not point self-tests at your real application data folder.

`Verification/verification.json` records successful functional assertions. Coverage includes the supplied workbook, 56-column retention, missing IDs, deterministic status classification, STOP-over-REVIEW precedence, all-PASS evaluation, missing/unknown check handling, automatic legacy-batch migration, duplicate retention, source-column filtering, SQL injection resistance, database pagination, exact XLSX/CSV export counts, formula quoting, transaction rollback, export restrictions, DPAPI, actual Agent Framework tool execution against a local protocol fixture, the 10,000-row demo, and a 100,000-row import with a 20,000-row CSV export. The 100k import/query/export sequence took approximately 13 seconds on the build machine. Desktop inspection confirmed sample loading, dashboard charts, category drill-down, pagination and findings. This is not a guarantee for every malformed legacy XLS or every 100k-row, 56-column workbook.

Framework references: [Microsoft Agent Framework](https://github.com/microsoft/agent-framework), [Azure OpenAI agent provider](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai), [ClosedXML](https://github.com/ClosedXML/ClosedXML).
