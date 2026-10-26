# AI Augmentation Log

A record of how AI (the Kiro agent) was used to produce the assignment deliverables, what it did
well, where it was wrong or needed correcting, and what a human drove. Written to be honest
rather than flattering — the same spirit as the "Where the agent was wrong or useless" section in
the ADR.

## Environment and tools

- **Agent:** Kiro, driving file edits, shell commands, and sub-agents.
- **MCP servers used:** Mermaid MCP (`mcp-mermaid`, launched via `npx`/node) to validate and
  render every diagram; Playwright MCP was configured in the repo but not used for these
  artifacts. Config committed in `.mcp.json` and `.kiro/settings/mcp.json`; setup in
  `docs/mcp-setup.md`.
- **Toolchain installed during the work:** .NET SDK 8.0.426 (installed mid-session to run the
  architecture test; the machine had no `dotnet` initially).
- **Grounding sources:** the `backend/` source tree, `AGENTS.md`, and the `ops/` evidence packs
  (`incidents/INC-2041`, `incidents/INC-2047`, `pipeline/`, `evals/`).

## Per-deliverable augmentation record

### 1. As-built + target-state diagrams (R1)
- **AI did:** verified every grounding symbol against `backend/`, authored both Mermaid diagrams
  for each doc, wrote the Citations, Inferred, and Corrections sections, and validated all four
  diagrams through the Mermaid MCP (zero syntax errors).
- **Correction the AI made to itself:** initially described `ApprovalService.EnsurePending` as a
  callable/public precondition; the code shows it is `private static`. Recorded in the
  Corrections section (X1).
- **Human drove:** the instruction to actually *use the Mermaid MCP* (the AI first validated with
  editor diagnostics only and had to be redirected).

### 2. Architecture test (R1.7 / R2-adjacent)
- **AI did:** wrote `DomainDependencyRuleTests` (Domain must not depend on web/storage
  assemblies), documented the command, and ran it green three times for determinism.
- **Blocker the AI hit and surfaced honestly:** no .NET SDK on the machine. The AI did **not**
  fabricate a pass; it flagged the blocker, and only after the SDK was installed did it record a
  real green result (3/3, exit 0).

### 3. Exactly-once ADR (R2)
- **AI did:** drafted the full ADR grounded in INC-2041 (synchronous PayEx call → socket
  exhaustion) and INC-2047 (silent Service Bus filter mismatch), including the three options, the
  three stress tests, revisit triggers, and the self-critique.
- **Where the AI was weak (its own admission):** cost figures are guesses; the PayEx idempotency
  contract is unverified (no PayEx code in the repo); it produced no worker-sizing arithmetic.
  These are documented in the ADR's "Where the agent was wrong or useless" section.

### 4. Autonomy policy (R3)
- **AI did:** wrote the one-page month-end log-triage policy (L0–L3, controls, promotion
  evidence, approvers, day-one level = L1), grounded in the two real incidents.

### 5. Review evaluation (R4)
- **AI did:** acted as the reviewer across 12 cases × 3 runs for two prompt versions, scored
  caught/missed/false-alarm, added two cases (case-11 defect, case-12 clean), changed one
  variable (prompt v1→v2), and reported rates with an explicit low-confidence statement.
- **Honest limitation the AI flagged:** it was ground-truth author, reviewer, and scorer at once,
  and v2 was tuned on the test set — both recorded as threats to validity.

### 6 & 7. Risk audit + measurement plan (R5)
- **AI did:** audited the setup against the lethal trifecta (all three legs present), recorded the
  decision to sever leg 3 plus the accepted residual and revisit trigger, wrote the baseline plan
  with exact queries, and captured two real numbers from `ops/pipeline/run-timings.csv` (change
  failure rate; deployment cadence).
- **Honesty point:** `ops/README.md` has no literal "agent setup" section; the AI said so and
  stated the setup it was auditing rather than inventing text. It declined to report a lead-time
  median from 6 seeded commits.

## Where AI genuinely helped vs. where a human was essential

**Helped:** fast grounding (resolving dozens of symbols against the source), consistent diagram
authoring + MCP validation, mechanical rate computation across 72 case-runs, and pulling the
specific failure shapes out of the incident packs (e.g. the `InvoiceApproved` vs
`invoice.approved` filter mismatch).

**Human essential:** redirecting the AI to use the Mermaid MCP; deciding to install the SDK rather
than accept an unverified test; and — most importantly — the judgement calls in the ADR, autonomy
policy, and risk audit, which the AI can draft but a named engineer must own and ratify.

## Known AI failure modes observed this session (specifics)

1. **Validated diagrams the wrong way first** — used editor diagnostics instead of the required
   Mermaid MCP until corrected.
2. **Assumed tooling was present** — did not check for the .NET SDK before claiming the test
   could run; caught and corrected.
3. **Needed the evidence to overturn a prior** — leaned toward the simpler synchronous PayEx
   option until INC-2041's metrics forced the outbox decision.
4. **Conflict-of-interest in the eval** — scored its own work; a second human scorer is needed to
   trust R4's numbers.

## Provenance note

This log is a human-readable summary of the session, not a verbatim transcript. The authoritative
evidence of what was produced is the committed files themselves (see `docs/SUBMISSION.md`), the
git history, and the recorded test/eval outputs.
