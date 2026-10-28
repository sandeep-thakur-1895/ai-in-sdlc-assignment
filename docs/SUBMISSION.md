# Submission Index

Maps each required hand-in item to the file(s) that satisfy it. All paths are relative to the
repository root.

| # | Hand-in item | File(s) |
| --- | --- | --- |
| 1 | Mermaid source for the as-built and target diagrams, with the citation list and the inferred-items list | `docs/architecture/as-built.md` (container + sequence Mermaid, **Citations**, **Inferred**, **Corrections**); `docs/architecture/target-state.md` (target container + sequence Mermaid, legend, citation tables) |
| 2 | One architecture test, with the command that runs it | `backend/tests/InvoiceApp.Tests/DomainDependencyRuleTests.cs`; command + recorded green run in `backend/tests/InvoiceApp.Tests/README.md` |
| 3 | The exactly-once decision record, including the "Where the agent was wrong or useless" section | `docs/architecture/adr/0001-payex-exactly-once.md` |
| 4 | A one-page autonomy policy, with the day-one level stated | `docs/autonomy-policy-month-end-log-triage.md` (day-one level: **L1**) |
| 5 | Your two added evaluation cases, and a results table covering three runs plus the changed-variable run | Added cases `ops/evals/cases/case-11.diff`, `ops/evals/cases/case-12.diff`; results `ops/evals/results.csv`; report + tables `ops/evals/report.md` |
| 6 | The risk audit, with a decision recorded | `docs/risk-audit-and-measurement-plan.md` — Part A (lethal-trifecta audit; decision: sever leg 3 + accepted residual with revisit trigger) |
| 7 | The measurement plan, with the two numbers you captured | `docs/risk-audit-and-measurement-plan.md` — Part B (change failure rate 0/8 on `main`, 10% overall; deployment cadence ~58 h median) |
| 8 | AI augmentation logs | `docs/ai-augmentation-log.jsonl` (structured, reviewer schema) + `docs/ai-augmentation-log.md` (readable summary) |

## Supporting material (not itemised but referenced above)

- `docs/mcp-setup.md` — MCP + toolchain setup (Mermaid MCP, .NET 8 SDK prerequisites).
- `.mcp.json` — committed MCP server configuration.
- `ops/` — the evidence packs the artifacts are grounded in (incidents, pipeline, evals).

## How to verify each item

- **Diagrams (1):** paste the Mermaid blocks into any Mermaid renderer, or the project's Mermaid
  MCP; every element resolves to a file/symbol in the Citations list.
- **Test (2):** `dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~DomainDependencyRuleTests`
  (also runs under `dotnet test backend/InvoiceApp.sln`). Requires the .NET 8 SDK.
- **Eval (5):** `ops/evals/report.md` has the ten-case baseline, the 12-case runs, and the
  per-run results table; `results.csv` is the raw data.
- **Captured numbers (7):** recomputable from `ops/pipeline/run-timings.csv`.
