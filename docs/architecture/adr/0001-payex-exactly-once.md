# ADR 0001 — Exactly-once submission of approved invoices to PayEx

- **Status:** Accepted (planning). Applies to the January tier-2 release. No PayEx, Azure SQL, or Azure Service Bus code exists in the repository today; this record is written before the integration is built.
- **Date:** 2026-10-23
- **Deciders:** Senior engineer (author), to be ratified by the team of six before the January cutover.

---

## Context

In January the Invoice Approval service becomes a tier-2, 24x7 service and gains a new
responsibility: every approved invoice must be submitted to an external payment provider,
**PayEx**. Getting this wrong has money attached to it — a duplicate submission can mean a
duplicate payment, and a dropped submission means a supplier is never paid.

### What the system does today (grounded in code)

- Approval is a synchronous, in-process operation. `ApprovalService.Approve`
  (`backend/src/InvoiceApp.Domain/ApprovalService.cs`) moves an invoice from
  `PendingApproval` to `Approved` after `EnsurePending` passes, then calls
  `IInvoiceRepository.Update` (`backend/src/InvoiceApp.Domain/IInvoiceRepository.cs`).
- Persistence is **in-memory only** — `InMemoryInvoiceRepository`
  (`backend/src/InvoiceApp.Api/InMemoryInvoiceRepository.cs`) backs the store with a
  `List<Invoice>`. There is no database.
- Lifecycle states are exactly `Draft`, `PendingApproval`, `Approved`, `Rejected`
  (`backend/src/InvoiceApp.Domain/InvoiceStatus.cs`).

### What is planned but does not exist yet (`ops/` material, not code)

Per `ops/README.md` and `AGENTS.md`, the production deployment adds Azure SQL (persistence),
an already-provisioned-but-unused Azure Service Bus topic, and the PayEx integration. **None
of these are in the code.** This ADR designs against them; it does not describe current
behavior.

### The constraints that actually shape this decision

| Fact | Source | Why it matters here |
| --- | --- | --- |
| PayEx returns a reference on success; times out at **30 seconds**; **a timeout does not reliably mean the submission failed** | scenario / `ops/README.md` | The core problem is *ambiguity*, not failure. We can never assume a timed-out submission did not go through. |
| ~40,000 invoices/month, **60% in the last 3 working days**, peaking near **80 approvals/minute** | scenario | The design must survive a month-end spike, not the average. |
| SOC 2 Type II in 6 months; approval actions auditable for **7 years** | scenario | Submission attempts and outcomes must be durably recorded, not just logged. |
| Azure Service Bus is provisioned but **nothing uses it yet** | scenario | A queue/worker option is available "for free" infrastructurally, but unproven in this system. |
| Two of six engineers are new to the codebase; one-week on-call rotation | scenario | The 3 a.m. operability of the chosen option matters as much as its theoretical correctness. |

### Two incidents in the evidence pack that this decision must respect

These are the failure shapes this service has *already* demonstrated. They are the most
important input to the decision because they are real for this system, not generic.

- **INC-2041 (14 Jan, second-to-last working day).** Release 2.14.0 caused
  `outbound_connections` to climb from a steady ~68 to **over 2,900**, with p95 latency going
  from ~130 ms to **17,000+ ms** and the 5xx rate crossing the 2% alert threshold
  (`ops/incidents/INC-2041/metrics.csv`, `alert.md`). A restart of one instance helped for
  ~3 minutes, then the error rate returned. The shape is **synchronous outbound calls under
  month-end load exhausting a connection pool**. Any design that puts a 30-second
  synchronous PayEx call on the approval request path will reproduce this.
- **INC-2047 (29 Jan, month-end).** Approvals returned 200, the audit trail was complete,
  but **no notifications went out for two days** and no error was logged
  (`ops/incidents/INC-2047/alert.md`). The read-only Service Bus inspection shows why: the
  `notification-worker` subscription has a `CorrelationFilter` on
  `eventType = "InvoiceApproved"`, while the messages the publisher actually emits carry
  `eventType = "invoice.approved"` — the catch-all `audit-archive` subscription (`1=1`)
  received them, proving the publisher worked (`ops/incidents/INC-2047/servicebus-status.txt`).
  The shape is a **silent drop**: a success to the user, a config/contract mismatch between
  publisher and consumer, and zero errors. Any asynchronous option inherits this failure
  class and must be designed to make silent loss *loud*.

---

## Decision

**Adopt the transactional outbox with a worker (Option B), with an idempotency key carried
to PayEx (Option A folded in as the dedupe mechanism).** Submission is decoupled from the
approval request; the approval transaction writes the invoice state and an outbox row in the
**same Azure SQL transaction**; a background worker drains the outbox and calls PayEx using a
**stable idempotency key per invoice**; PayEx's returned reference is the record of success.

Concretely:

1. `Approve` commits, in one Azure SQL transaction, both the invoice row (`Status=Approved`)
   and an `outbox` row `(invoice_id, idempotency_key, status='pending', attempts=0)`. The
   idempotency key is derived deterministically from the invoice identity (e.g. a UUID stored
   on the invoice at approval), **not** generated per attempt.
2. The approval HTTP response returns as soon as that transaction commits. **No PayEx call is
   on the request path.** (This is the direct lesson of INC-2041.)
3. A worker polls the outbox (or is driven by Service Bus — see Consequences), and for each
   pending row calls `PayEx.Submit(invoice, idempotencyKey)` with a 30 s timeout.
4. On a reference returned: store the reference, mark the row `submitted`.
5. On a 30 s timeout (ambiguous): **do not** mark failed and **do not** blindly resubmit with
   a new identity. Leave the row `pending` and retry *with the same idempotency key*, so PayEx
   de-duplicates if the first attempt actually landed. Exhausted retries move the row to
   `needs_reconciliation`, which is an alert, not a silent state.

The invoice lifecycle (`Draft/PendingApproval/Approved/Rejected`) is **unchanged**; submission
status is tracked separately on the outbox row, not as a new lifecycle state.

Idempotency is **required regardless of option**, because the 30-second ambiguous timeout means
retries are inevitable and PayEx is the only party that can safely collapse two attempts into
one payment. The outbox is what makes the attempt *durable and auditable* (SOC 2, 7-year
retention) and keeps the 30-second call off the request path.

---

## Options considered

All three were evaluated against the same four questions: behavior on a 30 s ambiguous timeout,
behavior at ~80 approvals/min on the last day of the month, build cost and run cost, and the
single dominant failure mode at peak.

### Option A — Idempotency key with a dedupe table

A dedupe table in Azure SQL keyed by idempotency key. Before submitting, check/insert the key;
PayEx also receives the key so it can dedupe server-side.

- **30 s ambiguous timeout:** Safe to retry with the same key — PayEx collapses duplicates.
  But if submission is still **synchronous on the approval request**, the caller waits up to
  30 s, and the dedupe table alone does nothing to move that wait off the request path.
- **At 80/min month-end:** If synchronous, this is **INC-2041 again** — up to 80 concurrent
  30-second outbound calls per minute exhausting the connection pool. The dedupe table adds DB
  round-trips per approval on the hottest path.
- **Build cost:** Low–medium. One table, a uniqueness constraint, a check-insert, pass the key
  to PayEx.
- **Run cost:** Low storage; but high *latency* cost if synchronous, and contention on the
  dedupe table at peak.
- **Dominant failure at peak:** Connection-pool / thread-pool exhaustion from synchronous
  30 s calls under spike (the INC-2041 shape). Correct-once, but not available under load.

### Option B — Transactional outbox with a worker (chosen)

Approval writes invoice + outbox row in one transaction; a worker drains the outbox and calls
PayEx out-of-band, carrying the idempotency key.

- **30 s ambiguous timeout:** The worker owns the timeout, not the user. Row stays `pending`;
  retry uses the same key so PayEx dedupes; exhausted retries → `needs_reconciliation` (an
  alert). Ambiguity is contained and made visible.
- **At 80/min month-end:** Approvals commit fast (one local SQL transaction) and return; the
  worker drains at a controlled concurrency independent of the arrival spike. Backlog grows
  and drains rather than knocking the API over. **This is the option that does not reproduce
  INC-2041.**
- **Build cost:** Medium–high. Outbox table, a worker, at-least-once processing, retry/backoff,
  a reconciliation/alert path, and a dashboard. More moving parts, two of which (worker,
  messaging) are new to a team where two engineers are new to the codebase.
- **Run cost:** Medium. A continuously running worker, outbox storage (prunable after
  submission is confirmed, within the 7-year audit rules), and monitoring.
- **Dominant failure at peak:** **Silent backlog or silent drop** — the INC-2047 shape. A
  worker that is alive but not processing (wrong Service Bus filter, poisoned message, paused
  consumer) returns no error and the user still sees 200. This is the failure we must engineer
  loudly against (see "how it fails at 3 a.m." below).

### Option C — Optimistic submission with nightly reconciliation

Submit on a best-effort basis; a nightly job reconciles the service's view against PayEx and
submits anything missing.

- **30 s ambiguous timeout:** Tolerated by deferral — the nightly job is the safety net. But
  the window between an ambiguous timeout and the nightly run is a window of **unknown payment
  state**, up to ~24 hours.
- **At 80/min month-end:** The optimistic path has the same synchronous-exposure risk as A
  unless also queued; worse, the reconciliation job runs **hardest exactly at month-end** when
  the backlog of ambiguous cases is largest — reconciling tens of thousands of invoices against
  PayEx on the night of the last working day.
- **Build cost:** Medium. The nightly reconciliation job and a reliable "what did PayEx
  actually receive" query are the hard parts and depend on PayEx's reconciliation API, which is
  an **unverified assumption** (see self-critique).
- **Run cost:** Low most days; a heavy, time-boxed batch at month-end.
- **Dominant failure at peak:** A **24-hour blind spot** colliding with the heaviest window —
  the reconciliation job doesn't finish before the next business day, and nobody knows which
  invoices are actually paid during the exact window the business cares about most.

---

## How the recommendation holds up under stress

The brief asked for the decision to be stressed three ways. Here is each, honestly.

### 1. "I'd prefer Option C (optimistic + nightly reconciliation) — it's the least code."

It is the least code, and that is a real pull for a six-person team adding a 24x7 rotation.
But the preference does **not** move the recommendation, for one decisive reason: Option C's
failure mode is a multi-hour blind spot that peaks *precisely* at month-end (60% of volume in
3 days). On the last working day, "we'll know tonight" can mean a supplier-visible delay across
the single most important window of the cycle, with no durable per-attempt record until the
batch runs — which is awkward against a SOC 2 audit that wants submission outcomes traceable.
I would accept Option C *only* if PayEx offered a cheap, authoritative "list what you received
since X" API and the business accepted a defined reconciliation lag. Absent a verified such API,
C trades a bounded, visible backlog (B) for an unbounded, invisible one. Recommendation stands.

What *would* move me: if we learned the worker/messaging path (Option B) could not be made
reliably observable before January, C-with-a-tight-intraday-reconciliation becomes the safer
*interim* bet, because a blind spot you schedule is better than a silent drop you don't detect.

### 2. "How does your choice fail at 3 a.m. on the last day of the month?"

Not with a bang — INC-2047 is the template. The API is healthy and returning 200. Approvals
commit. But the worker is **alive and not submitting**: a config drift between the publisher's
event name and the worker's Service Bus filter (exactly the `InvoiceApproved` vs
`invoice.approved` mismatch in `ops/incidents/INC-2047/servicebus-status.txt`), or a poisoned
message blocking an ordered partition, or a consumer paused after a deploy. The outbox backlog
grows silently, suppliers aren't paid, and the health check says "ok, 0 processed in 12h" —
which is what the INC-2047 worker logged for two days. The on-call engineer, possibly one of
the two people new to the codebase, is paged for "payments behind" with a green API dashboard.

Because this is the predictable 3 a.m. failure, the design has to pay for defending against it,
or the recommendation isn't honest:

- Alert on **outbox age**, not just throughput: "oldest `pending` row > N minutes" pages
  someone. (INC-2047's "0 processed" health check was not an alert — that is the bug to avoid.)
- Treat `processed == 0` during business hours as a **page**, not an INFO health line.
- A startup/deploy check that asserts the worker's subscription filter matches the publisher's
  emitted event contract — the specific thing that silently broke INC-2047.
- `needs_reconciliation` is an alerting state with a runbook, reachable by a tired engineer.

If we are not willing to build that observability, Option B is *more* dangerous than C, because
it fails silently. The recommendation is B **with** this monitoring, not B alone.

### 3. "What does it cost to build and to run?"

- **Build:** the largest of the three. Outbox table + migration, the transactional write in the
  approval path, a worker (new component for this team), at-least-once handling with
  retry/backoff and the same-key retry discipline, the reconciliation/alert path, and the
  observability above. Rough order: a few engineer-weeks including tests and a month-end load
  rehearsal — notably more than A's table or C's batch job.
- **Run:** a continuously running worker, outbox storage (prunable post-confirmation within the
  7-year audit constraint), Service Bus (already provisioned, currently unused), and the
  dashboards/alerts. Moderate and steady, with no month-end spike on the API itself.

The build cost is the strongest argument against B. I am accepting it because the alternative
costs — A's reproduction of INC-2041, C's month-end blind spot — are paid in *incidents and
possibly duplicate or missed payments*, which are more expensive than engineer-weeks.

---

## Revisit triggers

Reconsider this decision if any of the following becomes true:

1. **PayEx ships an authoritative reconciliation/query API** ("what did you receive since X?")
   that is cheap and fast. That materially de-risks Option C and could simplify B's recovery
   path; re-score both.
2. **PayEx changes its idempotency contract** — if it stops honoring a client idempotency key,
   the "safe retry with the same key" assumption collapses and the whole ambiguity-handling
   design must change.
3. **Observed month-end volume materially exceeds the ~80/min assumption** (say, sustained
   >150/min), which changes worker concurrency sizing and may require partitioned processing.
4. **We cannot deliver the outbox-age / zero-throughput alerting before January.** Without it,
   B's silent-backlog failure is unacceptable and an interim intraday-reconciliation (C-ish)
   bridge is safer.
5. **SOC 2 evidence requirements** turn out to demand per-attempt submission records we are not
   capturing, forcing schema changes to the outbox.
6. **Two consecutive month-ends pass with zero ambiguous timeouts and zero reconciliation
   hits** — then the machinery may be heavier than the risk warrants, and we should ask whether
   it can be simplified.

---

## Consequences

- **Positive:** The 30-second PayEx call leaves the approval request path, directly avoiding the
  INC-2041 connection-exhaustion shape. Exactly-once is enforced by the idempotency key at PayEx;
  the outbox gives a durable, auditable record of every attempt (SOC 2 / 7-year). Ambiguous
  timeouts become a bounded, visible `needs_reconciliation` state rather than a guess.
- **Negative / cost:** The most code and the most new concepts for a team with two newcomers;
  introduces a worker and messaging as new operational surfaces. **Its failure mode is silent**
  (INC-2047), so the option is only safe *with* the outbox-age and zero-throughput alerting — the
  monitoring is not optional, it is part of the decision.
- **Open question for the team:** poll the outbox table directly vs. drive the worker from the
  already-provisioned Azure Service Bus. Service Bus is "free" infrastructurally but is exactly
  where INC-2047's silent drop lived; polling the SQL outbox is simpler to reason about and
  harder to misconfigure. Leaning toward **SQL-outbox polling first**, Service Bus later if
  throughput demands it.

---

## Where the agent was wrong or useless

Written with specifics, as required. This section is about the AI assistant's contribution to
this ADR, not about the team.

1. **It initially under-weighted the request-path problem and had to be corrected by the
   evidence, not by reasoning.** The first pass toward "idempotency key + dedupe table" (Option
   A) read cleanly until INC-2041's `metrics.csv` showed outbound connections going 68 → ~2,900
   under synchronous load. The decisive input was a CSV in the evidence pack, not the model's
   prior — without INC-2041 it would likely have recommended the simpler synchronous option that
   reproduces the outage.
2. **It cannot verify PayEx's actual contract.** Every claim about PayEx honoring a client
   idempotency key, and the entire feasibility of Option C, rests on an **assumed** PayEx API.
   The repository contains no PayEx client, SDK, or spec (confirmed: no PayEx code anywhere in
   `backend/`). These are assumptions, labeled as such, and must be checked against real PayEx
   documentation before build. The ADR is only as sound as that unverified assumption.
3. **The cost figures are hand-wavy.** "A few engineer-weeks" is a guess with no basis in this
   team's velocity, no breakdown, and no estimate of the month-end load-rehearsal effort. Treat
   it as a placeholder for a real estimate, not an estimate.
4. **It has no numbers for the one quantity that decides worker sizing.** I asserted the worker
   can "drain at a controlled concurrency" but produced no model of PayEx latency distribution,
   so I cannot say how many concurrent workers clear an 80/min spike with a 30 s per-call
   ceiling. That arithmetic (the thing that actually sizes the solution) is missing.
5. **It pattern-matched INC-2047 to Option B's risk, which is useful, but it would not have
   found the root cause unaided.** The `InvoiceApproved` vs `invoice.approved` filter mismatch
   is obvious once you read `servicebus-status.txt` and notice the catch-all subscription
   received the messages — but that is retrieval from provided evidence, not insight the model
   generated. On a live incident without such a clean evidence pack, do not expect this.
6. **General limitation:** everything here is design-time reasoning over documents. None of it is
   tested against a running PayEx, a real Azure SQL outbox, or a month-end load profile. The one
   thing in this whole body of work that actually *runs* is the structural test
   (`DomainDependencyRuleTests`); this ADR does not run, and its confidence should be discounted
   accordingly until the integration exists and is load-rehearsed.
