# Autonomy Policy — Month-End Log Triage

**Workflow:** Month-end log triage for the Invoice Approval service. During the last three
working days of the month (~60% of volume, peaking near 80 approvals/min), an assistant reads
the application logs and platform metrics to surface what the on-call engineer should look at:
anomaly clusters, error bursts, latency creep, and correlation with recent deploys/config.

**Scope boundary:** This policy covers *triage of read-only evidence* (logs, metrics, deploy
diffs). It does **not** cover taking production action — restarts, rollbacks, scaling, config
changes. Those follow the incident process and always need a named human approver.

This policy is grounded in two real month-end incidents in `ops/incidents/`: **INC-2041**
(a deploy put a synchronous PayEx call on the approval path, exhausting sockets under load) and
**INC-2047** (an event-name rename silently broke a Service Bus subscription filter; approvals
returned 200 while notifications stopped for two days). Both are exactly what triage must catch,
and both show how confident-but-wrong triage would mislead a tired engineer.

---

## Levels of autonomy

| Level | Name | What the assistant does | Example tasks in this workflow |
| --- | --- | --- | --- |
| **L0** | Manual | No assistant. Engineer reads logs directly. | Baseline. |
| **L1** | Assist (suggest) | Reads logs/metrics and produces a **ranked, cited** triage summary. Every claim points at specific log lines/timestamps. Proposes *hypotheses and what to check next*, never a conclusion. Human reads everything. | Summarize the error window; cluster `SocketException`/`TaskCanceledException`; flag latency creep vs the 7-day baseline; correlate with the last deploy. |
| **L2** | Act-with-checkpoint | Everything in L1, **plus** the assistant may file the triage note as a draft incident comment / page annotation and open (not merge) a tracking ticket. A human reviews before it is shared beyond the on-call channel. | Draft the "what I see, what I'd check, what would disprove it" note and attach the cited evidence to the incident. |
| **L3** | Act-and-notify | Assistant posts the triage summary automatically to the incident channel and tags the on-call engineer, who reviews after the fact. No production action, still read-only. | Auto-post the month-end triage digest every N minutes during the peak window. |

There is deliberately **no level at which the assistant changes production** in this workflow.
Rollback/restart/scale stay with the human incident process (the INC-2047 pack is explicit:
"The decision is not yours to take alone, and neither is the action").

---

## Controls that make each level safe

- **L1 (Assist):** Read-only data access only. Every finding **must cite** the log lines or
  metric rows it rests on and **must name what would disprove it** — the INC-2041 pack's own
  test ("point at the lines that support it, and name what you would look at to prove yourself
  wrong"). Output is advisory; the human owns the conclusion. No write access anywhere.
- **L2 (Act-with-checkpoint):** All of L1, plus writes are confined to **draft/non-destructive
  surfaces** (a draft comment, an unmerged ticket). A named reviewer must approve before the
  note is treated as the incident's working theory. Rate-limited so it cannot spam the channel.
- **L3 (Act-and-notify):** All of L2, plus an explicit **"advisory only — not a root cause"**
  banner on every auto-posted digest, a kill switch the on-call can hit to silence it, and a
  standing rule that a digest never recommends an action, only observations. Audit-logged for
  the 7-year SOC 2 trail.
- **Cross-cutting:** No secrets or customer PII echoed into summaries. Every assistant output
  is itself logged (who/what/when) so triage activity is auditable.

---

## Evidence needed to move a task up a level

A task moves up **one** level only after it clears the bar below. Promotion is per-task-type
(e.g. "error-burst clustering"), not blanket.

1. **Accuracy track record:** Over a defined trial window (minimum **two month-end cycles**),
   the assistant's L1 summaries are scored against what the incident actually turned out to be.
   Target: **no false "all clear"** on a window that contained a real defect, and a low
   false-alarm rate. One missed real incident resets the clock.
2. **Grounding integrity:** Spot-checks confirm every cited line resolves and no finding is
   fabricated or mis-attributed. (INC-2047 is the cautionary case: the surface signal was
   "worker healthy, 200s everywhere" — a summary that stopped there would have been confidently
   wrong. Promotion requires the assistant demonstrably catches the *silent* failure, not just
   the loud one.)
3. **Disprovability:** Every promoted finding type includes its "what would disprove this" and
   has been shown to change its ranking when the disproving evidence is present.
4. **Reversibility of the new surface:** For L2→L3, the auto-post surface must have a working
   kill switch and rate limit, tested.

Evidence is recorded in a short promotion log (date, task type, trial scores, reviewer).

---

## Who approves the move

- **L0 → L1:** The service tech lead. Low risk (read-only, advisory); a lightweight sign-off.
- **L1 → L2:** The tech lead **and** the current incident commander, because L2 writes into the
  incident record. Both named in the promotion log.
- **L2 → L3:** The tech lead, the incident commander, **and** a sign-off that the SOC 2 audit
  trail requirement is met (auto-posted content is retained and attributable). Given the audit
  in six months, this is non-negotiable for L3.
- **Any level → down:** Any on-call engineer can **demote immediately** without approval if the
  assistant produces a wrong or misleading summary. Demotion is always unilateral and fast;
  promotion is always reviewed and slow.

---

## What level this team runs on day one

**Day one of the rotation: L1 (Assist), with L2 as an explicit, separate pilot for one task
type only.**

The reasoning, given that two of the six engineers are new to the codebase:

- The two newcomers are the people most likely to be paged into a month-end incident and least
  able to catch a confident-but-wrong summary. An assistant at L2/L3 that *writes into the
  incident record* would hand a newcomer a theory they cannot yet sanity-check. L1 keeps the
  human firmly in the loop while the summary is only ever advice.
- There is **no accuracy track record yet** for this service's logs. The promotion bar above
  requires two month-end cycles of scored L1 output; on day one we have zero. We cannot have
  earned L2 for anything.
- The real failure shapes here are subtle. INC-2041 looks like a platform problem until you
  notice the deploy swapped a pooled `HttpClient` for a per-call one; INC-2047 looks healthy
  until you notice a filter/schema mismatch the release notes actually warned about. An
  assistant needs to prove it catches these *before* its output is trusted enough to post
  automatically.

So on day one: **L1 everywhere.** Run one chosen task type (error-burst clustering with deploy
correlation) as a **supervised L2 pilot** — drafts reviewed by the tech lead every time —
through the first month-end. If it clears the evidence bar over two cycles, promote that one
task to L2 for real; revisit L3 only once the SOC 2 audit-trail controls are verified. Until
then, the assistant triages; the engineer decides.
