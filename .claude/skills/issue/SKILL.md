---
name: issue
description: Take a GitHub issue (bug or feature) end to end — recon, plan, implement, verify, draft PR. Use when the user says "issue 12", "fix #9", "implement issue 15", "/issue", or asks to turn a GitHub issue into a pull request.
argument-hint: "<issue-number> [--ready] [--skip-frontend-gate] [--skip-backend-gate]"
---

# Issue → PR skill

Turns one GitHub issue in `ingel81/knx-ng-monitor` into a reviewed draft pull request.

The heavy lifting runs in two multi-agent workflow scripts under `.claude/workflows/`.
Workflows cannot pause for input, so the run is split into two invocations with a human
gate between them — and commit/push/PR never happens inside a workflow.

```
gh issue view N                   (this session — the user reads the issue)
Workflow issue-recon-plan         (7 agents)   → plan
   ── GATE 1: user approves the plan ──
git checkout -b <branch>          (this session)
Workflow issue-implement-verify   (6–14 agents) → uncommitted diff + gate results
triage leftovers, run manual checks, refresh the PR body   (this session)
   ── GATE 2: user approves commit + push + PR ──
git commit / git push / gh pr create   (this session)
```

## Arguments

- `<issue-number>` — required. Bare number or `#12`. If missing, run
  `gh issue list --state open --limit 20` and ask which one.
- `--ready` — open a normal PR instead of a draft.
- `--skip-frontend-gate` / `--skip-backend-gate` — drop that gate (only for changes that
  provably cannot touch that side).

## Hard rules for this skill

- **Never commit, push or open the PR without explicit approval at Gate 2.** The repo's house
  rule is "never commit on your own"; the user's OK at Gate 2 is the exception that unlocks it.
- **Never `git add -A`.** Stage the files the workflow reported, explicitly, by path.
  `docs/samples/own/`, `docs/samples/other/`, `.claude/settings.local.json`, `data/`,
  `publish/`, `coverage-tmp/` must never be committed.
- **Never work on `master`.** If the branch cannot be created, stop and ask.
- **Branch from what the PR will be diffed against.** If local `master` is ahead of
  `origin/master`, branching locally drags those commits into the PR. Ask first: push them,
  or branch from `origin/master`.
- **No finding gets dropped in silence.** Every review finding is either fixed, reported at
  Gate 2 as a deliberate non-fix with its reason, or written down as a follow-up.
- If the plan comes back with `confidence: "low"` or non-empty `openQuestions`, do not push
  through — surface them to the user at Gate 1 and get answers first.
- If the final gates are red (`green: false`), do **not** offer to open the PR. Report the
  failure, and either fix it in this session or ask the user how to proceed.

## Steps

### 1. Fetch the issue

```bash
gh issue view <N> --repo ingel81/knx-ng-monitor --json number,title,body,labels,state,comments
```

Stop if the issue is closed, or if it is a `question` with no actionable ask — say so and ask
the user what they want instead.

Show the user a compact summary (title, labels, the ask in 2–3 lines) before spending tokens.

### 2. Pre-flight

```bash
git status --short
git branch --show-current
git fetch origin && git rev-list --left-right --count origin/master...HEAD
git log origin/master..HEAD --oneline
```

Working tree must be clean apart from `.claude/settings.local.json`. On `master` and level with
`origin/master` is the expected starting point — if not, ask before continuing. Unpushed local
commits are the case worth naming explicitly: they end up inside the PR unless they are pushed
first or the branch is cut from `origin/master`.

### 3. Workflow A — recon and plan

```
Workflow({
  name: "issue-recon-plan",
  args: { number, title, body, labels: [...], comments: "<flattened comment text>" }
})
```

Comments: flatten to `author: body` lines, or `"(none)"`. Do not pass the raw JSON blob.

Returns `{ verdict: { plan, branch, prTitle, changeKind, confidence, rationale, graftedIdeas }, recon, candidateSummaries }`.

### 4. GATE 1 — plan approval

Present to the user, compactly:

- the chosen approach and why (`verdict.rationale`)
- the numbered steps
- the files that will be touched (new ones marked)
- the test strategy, and whether a migration is needed
- what is deliberately out of scope
- any `openQuestions`, and the `confidence` if it is not `high`

Then ask: approve / adjust / abort. **Wait for the answer.** If the user adjusts the plan,
patch the plan object in place and re-present it — do not re-run Workflow A for small edits.

### 5. Branch

```bash
git checkout -b <verdict.branch>
```

### 6. Workflow B — implement and verify

```
Workflow({
  name: "issue-implement-verify",
  args: {
    number, title,
    branch: verdict.branch,
    changeKind: verdict.changeKind,
    plan: verdict.plan,
    needsRegressionTest: <true when the issue is labelled 'bug'>,
    gates: { /* omit for defaults; pass "" for a gate the user skipped */ }
  }
})
```

Defaults baked into the script:

| Gate | Command |
|---|---|
| backend build | `dotnet build backend/KnxMonitor.sln -c Debug --nologo` |
| backend tests | `dotnet test backend/KnxMonitor.sln --nologo --verbosity quiet` |
| frontend build | `cd frontend && npm run build -- --configuration production` |

The test gate runs the **solution**, not the single project CLAUDE.md names. That command covers
`ProjectParser.Tests` only, so a regression pin added to `Infrastructure.Tests` — or any test project
added later — would never run. Only override `gates` when a gate provably cannot apply.

Bug issues get `needsRegressionTest: true` — the implementer must produce a test that is red
before the fix. The `coverage` review lens verifies that claim by reading the test.

Returns `{ green, implementation, findings, reviewRounds, finalGate, pr: { body, commitSubject } }`.
The changes are left **uncommitted** in the working tree. Findings of severity `medium` and up are
fixed inside the workflow; `low` ones come back unfixed for you to triage in step 7.

### 7. Triage the leftovers, then prove what no gate could

Two jobs before you show the user anything.

**Triage every unfixed finding yourself.** `low` findings never reach the workflow's fixer, and a
`medium` one can survive if the fixer judged it mistaken. Read each against the real code and sort it
into: fix it now (cheap and confirmed), report it at Gate 2 as a deliberate non-fix with the reason,
or note it as a follow-up ticket. Never let one pass silently — a finding nobody decided on is worse
than no review. Doc-vs-code claims (version numbers, key enumerations, precedence statements) are the
class most likely to be right and least likely to be marked `mustFix`; verify those against the code
rather than trusting either side.

**Run the manual checks.** If `pr.body` carries a `### Pending manual verification` block, or the plan
named behaviour no test can cover, run it here — in the main session, where the user sees each command.
Prefer the built binary over `dotnet run` (no launch profile in the way), redirect stdout to a log
(the app's `ShouldOpenBrowser` checks `Console.IsOutputRedirected`, so redirection keeps browser windows
from opening), and kill processes **by PID or by a command-line filter**, never a blanket
`taskkill //IM dotnet.exe`.

**Then update the PR body.** Anything you fixed or decided in this step happened after the body was
written, so it is now stale. Replace the pending-verification block with the real results, drop notes
about issues you just fixed, and re-read it once against the final diff before it goes out.

### 8. GATE 2 — commit / push / PR approval

Show the user:

```bash
git status --short
git diff --stat
```

plus, from the workflow result:

- what was built (`implementation.summary`) and any `deviations`
- gate results from `finalGate.gates` — name and status per gate
- the red-before-green evidence for a bug fix, quoted
- your step-7 triage: what you fixed, what you deliberately left, what became a follow-up
- the results of the manual checks you ran
- `finalGate.suspiciousPaths` if non-empty — **call these out explicitly**
- the proposed commit subject and PR body

If `green` is false: report the red gate and stop here.

Ask: commit + push + open the PR (draft unless `--ready`)? **Wait for the answer.**

### 9. Commit, push, PR

Stage explicitly — list the paths, never `-A`. Then check what actually got staged before
committing, because that is the last moment a stray file can be caught:

```bash
git add <path> <path> ...
git diff --cached --name-only
git commit -F -   # heredoc; -m mangles multi-line messages on Windows
git push -u origin <branch>
gh pr create --repo ingel81/knx-ng-monitor --draft \
  --title "<verdict.prTitle>" \
  --body-file <scratchpad>/pr-body.md
```

Commit message rules from CLAUDE.md: compact, Conventional Commits, **no Claude footer** —
that house rule overrides the harness default that appends `Co-Authored-By` / `Claude-Session`.
The PR body already ends with `Closes #<N>`, which auto-closes the issue on merge.
Pass the body via `--body-file` from the scratchpad — multi-line markdown through `--body`
gets mangled on Windows.

Finish by verifying rather than assuming:

```bash
gh pr view <N> --json number,state,isDraft,baseRefName,changedFiles,closingIssuesReferences
```

`closingIssuesReferences` must name the issue — if it is empty, the `Closes #N` line did not
take and the merge will not close anything. Print the PR URL and the branch name.

## Cost

Workflow A ≈ 7 agents. Workflow B ≈ 6 agents for a clean first round, up to 14 if both review
rounds trigger fixes. Roughly 13–20 agents per issue, ~30 minutes wall clock, ~1M subagent tokens
for a change of the size of #9. For a one-line typo fix that is overkill — say so and offer to
just do it inline instead.

## Failure handling

- Workflow A returns `{ error }` → report it, do not proceed to a branch.
- Workflow B's implementer dies → the working tree may hold partial edits. Show
  `git status --short` and ask before touching anything; never blind-`git checkout .`.
- A workflow can be resumed after a script edit with
  `Workflow({ scriptPath: "<path from the tool result>", resumeFromRunId: "<runId>" })` —
  unchanged agent calls come back from cache.
