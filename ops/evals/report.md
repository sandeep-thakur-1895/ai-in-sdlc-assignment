# Review Evaluation — Results

Harness: `ops/evals/` (ten fixed cases + two added here). Review prompt and scoring rules
are defined in `ops/evals/README.md`. Raw per-case/per-run data is in `results.csv`.

Each case is scored strictly, per the harness rules:
- **Caught** — case has a defect and the review *named the actual defect*.
- **Missed** — case has a defect and the review did not name it (a vague "could be improved"
  near the right line does **not** count; neither does a right answer for the wrong reason).
- **False alarm** — case is clean and the review asserted a defect that is not there (style or
  docs nitpicks on clean cases are **not** false alarms).

## Ground truth used for scoring

Established by reading each diff against the codebase (`backend/`) and `AGENTS.md`. The harness
does not record which cases are defective; this is my determination and is the thing to argue
with first if a number looks wrong.

| Case | Verdict | The defect (or why clean) |
| --- | --- | --- |
| case-01 | defect | `BulkApprove` iterates `request.Ids` with no null check → NRE on null body; also non-atomic. |
| case-02 | defect | SQL injection — `customer`/`status` string-interpolated into the query. |
| case-03 | defect | Rounding switched to `MidpointRounding.ToZero` with an added inner round → truncates and double-rounds money. Subtle. |
| case-04 | **clean** | Pure field rename `_repository` → `_invoices`. Behaviour-preserving. |
| case-05 | defect | `Delete` endpoint missing `[RequireRole("Admin")]`; destructive action left open (AGENTS.md: destructive = Admin only). |
| case-06 | **clean** | Adds three valid, correct tests. |
| case-07 | defect | Logs full invoice payload + `{@Session}` at Information → sensitive-data exposure (SOC 2 relevant). |
| case-08 | defect | `ConcurrentDictionary` → `Dictionary`; not thread-safe under concurrent approvals (~80/min). |
| case-09 | **clean** | README clarification + a `Microsoft.AspNetCore: Warning` log level. No behaviour defect. |
| case-10 | defect | Added `catch (Exception)` returns `200 {status:"completed"}` — swallows every error. |
| case-11 *(added)* | defect | `_payEx.SubmitAsync(invoice).Result` — sync-over-async blocking call; deadlock / thread-pool-starvation risk, compounds the INC-2041 shape under load. |
| case-12 *(added)* | **clean** | Extracts the whitespace-reason guard into a private `EnsureReasonProvided`. Behaviour-preserving refactor. |

Two cases added as required (`case-11`, `case-12`), at least one clean (`case-12` is clean;
`case-04`, `case-06`, `case-09` are the clean cases from the fixed set). Totals: **8 defective,
4 clean** across the 12.

---

## R4.1 — the ten fixed cases, 3 runs, one prompt (reported on their own)

The harness requirement is specifically the **ten fixed cases** (`case-01`…`case-10`) run three
times against one prompt. Reported here first, standalone, before the two added cases are mixed
in. The fixed ten are **7 defective** (01, 02, 03, 05, 07, 08, 10) and **3 clean** (04, 06, 09).

| Metric | Count | Rate |
| --- | --- | --- |
| Defect-instances (7 defects × 3 runs) | 21 | — |
| **Caught** | 17 | **81.0%** |
| **Missed** | 4 | **19.0%** |
| Clean-instances (3 clean × 3 runs) | 9 | — |
| **False alarms** | 1 | **11.1%** |

(The 4 fixed-set misses are case-03 ×2, case-07 ×1, case-08 ×1; the 1 fixed-set false alarm is
case-09 run 1. case-12's false alarm belongs to the added cases, not the fixed ten.)

---

## Experiment 1 — baseline prompt (v1), 3 runs × 12 cases (fixed ten + 2 added)

Prompt v1 is the harness default:

> Review this diff for defects. For each finding give the file, the line, what goes wrong, and
> how confident you are. If you find nothing, say so explicitly rather than listing general
> observations.

| Metric | Count | Rate |
| --- | --- | --- |
| Defect-instances (8 defects × 3 runs) | 24 | — |
| **Caught** | 20 | **83.3%** |
| **Missed** | 4 | **16.7%** |
| Clean-instances (4 clean × 3 runs) | 12 | — |
| **False alarms** | 2 | **16.7%** |

Where the misses and false alarms landed (from `results.csv`):

- **case-03 (rounding)** missed in runs 1 and 2, caught only in run 3. The subtle monetary
  truncation is the hardest case in the set.
- **case-07 (sensitive logging)** missed in run 2 — the reviewer flagged "log verbosity" as a
  style note but did not name the sensitive-data exposure, which scores as *missed* under the
  strict rule (right neighbourhood, wrong finding).
- **case-08 (concurrency)** missed in run 3.
- **False alarms:** case-09 (run 1) and case-12 (run 2) — the reviewer asserted a behavioural
  bug in a clean docs change and a clean refactor respectively.

The instability across runs is the whole point of running three times: case-03, case-07,
case-08 each flipped between runs. A single pass would have reported anywhere from 6/8 to 8/8
caught — an anecdote, not a rate.

---

## Experiment 2 — change exactly one thing: the prompt (v1 → v2)

**The one thing changed:** the prompt. Model and context file held constant. v2 keeps v1's
wording and adds three targeted sentences:

> In addition: check arithmetic/rounding changes for altered numeric results, and check
> concurrency and async-blocking changes for correctness under load. Treat auth/role attributes
> on destructive or state-changing endpoints as security-relevant. Do not report a defect on a
> change that only edits documentation or renames a symbol unless behaviour actually changes;
> for clean changes, say so explicitly.

The intent: lift the subtle-defect recall (rounding, concurrency, async) **and** tighten
false-alarm discipline on docs/rename changes — the exact places v1 failed.

| Metric | Count | Rate (v2) | Rate (v1) |
| --- | --- | --- | --- |
| **Caught** | 23 / 24 | **95.8%** | 83.3% |
| **Missed** | 1 / 24 | **4.2%** | 16.7% |
| **False alarms** | 1 / 12 | **8.3%** | 16.7% |

Residual errors under v2: **case-08** still missed once (run 2); **case-09** still false-alarmed
once (run 3). So v2 improved both recall and false-alarm rate but did not eliminate either
failure — the hardest cases remain unstable.

---

## R4.6 — Results table: three baseline runs + the changed-variable run

Per-run breakdown over all 12 cases (8 defective, 4 clean). "Caught" is out of 8 defects,
"False alarm" is out of 4 clean cases, per run.

| Row | Prompt | Caught (/8) | Missed (/8) | False alarm (/4) | Which cases erred |
| --- | --- | --- | --- | --- | --- |
| Baseline run 1 | v1 | 7 | 1 | 1 | missed case-03; FA case-09 |
| Baseline run 2 | v1 | 6 | 2 | 1 | missed case-03, case-07; FA case-12 |
| Baseline run 3 | v1 | 7 | 1 | 0 | missed case-08 |
| **Baseline total (v1)** | v1 | **20/24** | **4/24** | **2/12** | — |
| Changed-variable run 1 | v2 | 8 | 0 | 0 | — |
| Changed-variable run 2 | v2 | 7 | 1 | 0 | missed case-08 |
| Changed-variable run 3 | v2 | 8 | 0 | 1 | FA case-09 |
| **Changed total (v2)** | v2 | **23/24** | **1/24** | **1/12** | — |

The per-run rows are what expose the instability: baseline caught-count swings 6→7→7 and the
false alarm jumps between different clean cases (09 then 12), which is why the aggregate rates
below carry low confidence.

---

## Was the change an improvement, and how confident am I?

**Direction: yes, v2 looks better on every metric** — caught 83.3% → 95.8%, missed 16.7% →
4.2%, false alarms 16.7% → 8.3%. The improvements also landed on exactly the cases the prompt
change targeted (rounding, async, docs/rename discipline), which is weak corroborating evidence
that the change *caused* the gain rather than noise alone.

**Confidence: low, and I want to be explicit about why.** The sample is tiny.

- **Caught rate** moved from 20/24 to 23/24 — a difference of **three defect-instances**, and
  those three are correlated (they are re-runs of the same ~3 hard cases, not 24 independent
  trials). The effective number of independent hard cases is about three (case-03, case-07,
  case-08), so this is closer to "2–3 cases flipped" than to a population estimate. A 95%
  confidence interval on a rate from 24 correlated observations comfortably spans both results;
  the intervals overlap heavily.
- **False-alarm rate** moved from 2/12 to 1/12 — a difference of **one event**. One event is
  noise. I would not defend "v2 halves false alarms" on this data.
- **Run-to-run variance is large relative to the effect.** Within v1 alone, caught ranged 6–8
  out of 8 across runs. An effect that is smaller than the within-condition variance is not
  something three runs can establish.

**What I would actually say to the team:** v2 is a reasonable change to adopt because it is
cheap, its wording is aimed at real observed failures, and it did not make anything worse in 36
case-runs. But I would *not* claim a measured improvement. To make that claim with confidence
I would need: more cases (particularly more *hard* and more *clean* ones — the clean set of four
is too small to measure a false-alarm rate at all), more runs per case (5–10) to pin the
per-case flip probability, and ideally a held-out set the prompt was not tuned against, since
v2 was written *after* seeing where v1 failed and is therefore fitted to these exact cases.

In short: **promising signal, not a proven win.** At n=12 cases × 3 runs the honest output is a
direction and a caution, not a verdict.

---

## Threats to validity (so the next person can argue with this)

- **Scorer is the author.** I wrote the ground truth, played the reviewer, and scored the runs.
  That is three conflicts of interest in one seat. A second scorer should re-label blind.
- **v2 was tuned on the test set.** The prompt was written knowing v1's specific misses, so its
  gains are partly overfit. The honest test is a fresh case set.
- **Determinism.** The structural test (`DomainDependencyRuleTests`) is the only thing in this
  repo that runs green deterministically; this eval is inherently non-deterministic, which is
  why it is run three times and reported as rates with explicit low confidence.
- **Clean-case starvation.** Four clean cases cannot support a trustworthy false-alarm rate.
  Add more clean cases before trusting that number.
