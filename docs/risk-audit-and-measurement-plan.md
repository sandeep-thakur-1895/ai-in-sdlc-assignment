# Risk Audit (Lethal Trifecta) + Pre-Rotation Measurement Baseline

Two separate deliverables in one document:

1. **Part A — Risk audit** of the agent setup against the *lethal trifecta*.
2. **Part B — Measurement plan**: the baseline to capture before the rotation starts, with the
   exact source/query for each metric, and the two I could capture from this repo today, captured.

---

## Part A — Lethal-trifecta risk audit

### What "the agent setup" is here (stated, because the file doesn't spell one out)

`ops/README.md` does **not** contain an explicit "agent setup" section — it ends at "No answers
here." So I state the setup I am auditing, drawn from what this repo actually configures plus
the planned production described in `ops/`:

- A **review/triage agent** that is handed diffs and logs and asked to analyse them
  (`ops/evals/README.md`: "Give the agent the case diff and the review prompt below"); the
  month-end log-triage agent in `docs/autonomy-policy-month-end-log-triage.md` reads production
  logs/metrics.
- An **MCP-enabled agent** configured in this repo: `.mcp.json`
  define a **Playwright MCP** (drives a real browser) and a **Mermaid MCP** (`npx`-launched),
  both running locally with whatever the developer's shell can reach.
- The **planned production service** (`ops/`): Azure SQL (invoice + approval data, 7-year
  audit trail), an Azure Service Bus topic, and the **PayEx** integration holding a **live API
  key** (`appsettings.Production.json` in the INC-2041 diff carries `PayEx:ApiKey`; the key was
  rotated by `s.brennan` on 2026-01-23 per INC-2047 `recent-changes.md`).

The lethal trifecta (Simon Willison's framing): an agent is dangerous when it simultaneously has
**(1) access to sensitive/private data**, **(2) exposure to untrusted content**, and **(3) a way
to communicate externally / exfiltrate**. Any two are survivable; all three together mean
untrusted content can direct the agent to take private data and send it out.

### Which legs are present

| Leg | Present? | Evidence in this setup |
| --- | --- | --- |
| **1. Access to private/sensitive data** | **YES** | The triage/review agent reads production logs, metrics, and diffs; the planned service holds invoice financial data, a 7-year approval audit trail, and the **PayEx API key** in production config. An agent operating over this material has the sensitive data in reach. |
| **2. Exposure to untrusted content** | **YES** | Triage reads **production logs**, which contain attacker-influenceable content: customer names, invoice fields, request paths, and error messages all flow into logs (see `INC-2041/app-logs.txt`). Case-07 in the evals even logs a full invoice payload. The Playwright MCP additionally renders **arbitrary web pages**. Log lines and page content are classic prompt-injection carriers. |
| **3. Ability to exfiltrate / communicate externally** | **YES (latent)** | The Playwright MCP can make outbound web requests (navigate to any URL) — a direct exfiltration channel. The planned PayEx integration is an outbound HTTPS caller. `npx -y mcp-mermaid` fetches from the network on launch. Nothing in the current config restricts egress. |

**All three legs are present.** This is a full lethal trifecta, mostly latent today (the agent is
used read-only for triage/review) but armed the moment the triage agent is given any tool that
acts on its reasoning — and the Playwright MCP already is such a tool.

The concrete attack this enables: a crafted string planted in an invoice field (customer name,
decision note) flows into the production logs → the month-end triage agent reads it as
"untrusted content" → the injected instruction tells the agent to, say, read the PayEx key from
config/logs and navigate the Playwright browser to an attacker URL with the secret in the query
string. Leg 1 + leg 2 + leg 3, end to end. This is not hypothetical given that case-07 shows
sensitive payloads already reaching the logs the triage agent is designed to read.

### Decision: redesign to remove one leg

I am **removing leg 3 (external communication)** rather than accepting the risk, because it is
the cheapest leg to cut and the one fully in our control. Removing any single leg breaks the
trifecta; leg 3 is the right one here because legs 1 and 2 are intrinsic to the job (triage
*must* read sensitive logs, and logs *will* carry untrusted content).

Redesign (all enforceable today):

1. **No network-acting tools in the triage/review agent's toolset.** The triage agent gets
   read-only log/metric/diff access and **no** browser, no HTTP tool, no shell with egress. The
   **Playwright MCP is removed from the triage agent's configuration** (keep it only in the
   isolated e2e test runner, which never sees production data — that keeps leg 1 away from the
   browser). This is the single most important change: it severs the one general-purpose
   exfiltration channel the agent has today.
2. **Egress allowlist** on any environment where an agent runs against real data: outbound
   network restricted to known endpoints (the model API, PayEx's domain for the service itself),
   everything else denied by default. An injected "navigate to evil.example" then fails at the
   network layer even if a tool slips in.
3. **Secrets out of agent reach.** The PayEx key lives in a secret store the triage agent cannot
   read; it is never logged (this also fixes the case-07 class of defect). The agent cannot
   exfiltrate what it cannot see.
4. **Pin MCP launch** (already partly done): `mcp-mermaid@0.4.1` is version-pinned and can be
   pre-installed so it is not fetching from the network at run time in a sensitive context.

With leg 3 severed, the remaining legs 1+2 are the normal, survivable state of a log-triage
tool: it can read sensitive data and it can be shown untrusted content, but it has no way to send
anything out.

### What I am NOT removing, and the residual risk I accept in writing

I am **not** removing leg 1 or leg 2 — they are inherent to log triage and review. I accept that
residual (legs 1+2 coexisting) in writing:

> **Accepted risk:** The triage/review agent will continue to read sensitive production data
> (leg 1) that contains untrusted, potentially injected content (leg 2). We accept this because
> it is the irreducible nature of the task, and we rely on severing leg 3 (no egress, no
> network-acting tools, secrets out of reach) to keep the trifecta broken.
>
> **Trigger to revisit:** reintroduce a full audit the moment **any** of these becomes true —
> (a) a network-acting tool (browser, HTTP, unrestricted shell) is added to an agent that also
> reads production data; (b) the egress allowlist is removed, widened to `*`, or bypassed;
> (c) secrets become readable from a surface the agent can see (e.g. the PayEx key logged again,
> repeating the case-07 defect); or (d) the agent is promoted to act on production (per the
> autonomy policy, anything above read-only triage). Any one of these re-arms leg 3 and the
> trifecta is live again.

---

## Part B — Pre-rotation measurement baseline

Capture these **before** the January rotation starts, so there is a before/after line when the
24x7 process and the AI tooling change how the team works. Three DORA-style metrics, each with
its **exact source or query**.

### The plan (all three metrics, named sources)

| Metric | Definition | Exact source / query |
| --- | --- | --- |
| **Lead time for changes** | Time from first commit on a change to that change running in production. | **Source:** git + deploy log. **Query:** for each merge to `main`, `git log --merges --first-parent main --format='%H %cI'` for the merge time, minus the change's first commit time `git log <feature-first-sha> --format='%cI' | tail -1`; subtract. Production deploy time comes from the App Service deployment history (Azure portal → Deployment Center, or `az webapp deployment list`). Median over the trailing 8–12 weeks. |
| **Change failure rate** | Share of deploys to production that cause a failure (rollback, hotfix, or incident). | **Source:** CI run history + incident log. **Query:** from `ops/pipeline/run-timings.csv` (and GitHub Actions API `GET /repos/{o}/{r}/actions/runs?branch=main&event=push`), count `conclusion=failure` on `main` pushes ÷ total `main` pushes; cross-check against `ops/incidents/` for deploys that triggered an incident (e.g. 2.14.0 → INC-2041). |
| **Review turnaround** | Time from PR opened/ready to PR approved (or merged). | **Source:** GitHub PR API. **Query:** `GET /repos/{o}/{r}/pulls?state=closed&base=main`; per PR, `merged_at` (or first approving review `submitted_at` from `GET .../pulls/{n}/reviews`) minus `created_at` (or `ready_for_review` time). Median over trailing 8–12 weeks. |

### The two I can capture today, captured

I captured these **now**, from the repository and its CI history. Each is reported with its real
source and an honest note on sample size.

#### Captured metric 1 — Change failure rate (from `ops/pipeline/run-timings.csv`)

**Source:** `ops/pipeline/run-timings.csv` (the last 20 CI runs), computed with
`Import-Csv` grouping by `run_number` and reading each run's `conclusion`.

- **All runs:** 20 runs, **2 failures → 10.0%** overall.
- **Production-relevant (`push` to `main`):** 8 runs, **0 failures → 0.0%** on `main`.
- **PR runs:** 12 runs, 2 failures → 16.7%.
- The two failures were both on **PR branches**, not `main`: run 6 (`fix/notification-filter`,
  2026-01-23) and run 14 (`fix/approval-audit`, 2026-01-29, the month-end window).

**Baseline to record:** change failure rate ≈ **0% on `main`** over this window, **10% across
all CI runs**. *Honest caveat:* "change failure rate" in the DORA sense is failures *in
production after deploy*, which CI pass/fail only approximates. Using CI-on-`main` as the proxy,
the number is 0/8 — but that is a tiny sample and CI green ≠ no production incident (2.14.0
passed CI and still caused INC-2041). The real metric needs the incident log joined to deploys;
this is the proxy available today.

#### Captured metric 2 — Pipeline duration (proxy for the CI portion of review turnaround / lead time)

**Source:** same CSV, summing `duration_seconds` across the 19 steps of each run.

- **Per-run wall-time:** min **1702s (28.4 min)**, max **2108s (35.1 min)**, mean **1880s
  (31.3 min)**, median **1882s (31.4 min)** across 20 runs.
- The slowest single step every run is **Static analysis (CodeQL)**: 350–647s, ~25–30% of each
  run. That is the first lever if CI time needs cutting before the rotation.

**Baseline to record:** CI pipeline ≈ **31 min median** per run. This is the mechanical floor of
review turnaround (a reviewer cannot approve-and-merge faster than one green pipeline), and a
component of lead time. Full review turnaround (human time from PR-ready to approval) needs the
GitHub PR API and is **not** in the repo, so it is left for live capture per the plan above.

#### Captured metric 3 — Deployment cadence on `main` (lead-time-adjacent, from the CI timestamps)

Because true lead time needs a deploy log the repo lacks (see below), I captured the closest
real number the CI history *does* support: how often change actually reaches `main`.

**Source:** `ops/pipeline/run-timings.csv`, `started_at` of each `push`-to-`main` run.

- **8 production deploys** (`push` to `main`) over an **11.3-day** span (2026-01-22 → 2026-02-02).
- **Median gap between consecutive `main` deploys ≈ 58 hours (~2.4 days).**
- This is DORA **deployment frequency** (a lead-time-adjacent flow metric): roughly a deploy
  every 2–2.5 days in this window. It is a genuine measured number, not a proxy for *duration*.

Together, metrics 1–3 give the pre-rotation baseline the two-number requirement asks for
(**change failure rate** and a **flow/lead-time number**), both computed from CI history today.

#### Why true "lead time for changes" still needs live capture (stated honestly)

Full DORA lead time (first-commit → running-in-production) needs commit timestamps joined to a
deploy log. The repo's **`git log`** has only **6 commits on three dates** (2026-10-05,
2026-10-12) with no merge-to-deploy history, and there is **no production deploy log** in the
repo. The CI timestamps give deploy *cadence* (metric 3 above) but not the per-change
first-commit-to-deploy elapsed time. So the precise lead-time *distribution* is deferred to live
capture using the Azure-deploy-history + git query named in the plan table. I am not reporting a
first-commit-to-deploy median from 6 seeded commits because that would be an invented figure.

### Summary of what was captured vs deferred

| Metric | Status | Value / reason |
| --- | --- | --- |
| **Change failure rate** | **Captured** (number 1) | 0/8 on `main`, 2/20 (10%) across all CI runs, from `run-timings.csv`. Caveat: CI-based proxy, not post-deploy incidents. |
| **Deployment cadence on `main`** | **Captured** (number 2) | 8 deploys / 11.3 days; **median ~58 h (~2.4 days)** between `main` deploys, from `run-timings.csv` timestamps. Lead-time-adjacent flow metric. |
| CI pipeline duration | Captured (supporting) | Median 31.4 min/run (min 28.4, max 35.1), from `run-timings.csv`. Mechanical floor of review turnaround. |
| Review turnaround (human) | Deferred | Needs GitHub PR API (`created_at`→`merged_at`); not in repo. |
| Lead time for changes (full) | Deferred | First-commit→deploy needs Azure deploy history + git; repo has 6 commits / no deploy log. Not inventing a number. |
