export const meta = {
  name: 'issue-implement-verify',
  description: 'Implement an approved issue plan in the working tree, run the build/test gates and adversarially review the diff',
  whenToUse: 'Phase 2 of the /issue skill: the plan is approved and the feature branch is checked out. Leaves the changes UNCOMMITTED for the user to approve.',
  phases: [
    { title: 'Implement', detail: 'write the code, iterate until the gates are green' },
    { title: 'Review', detail: 'three adversarial lenses over the diff' },
    { title: 'Fix', detail: 'apply confirmed findings' },
    { title: 'Gate', detail: 'final build + test run and PR body' },
  ],
}

// args: { number, title, branch, changeKind, plan, gates?: {backend, tests, frontend}, needsRegressionTest?: boolean, maxRounds?: number }
const a = args || {}
const plan = a.plan || {}
const MAX_ROUNDS = a.maxRounds || 2

const GATES = Object.assign({
  backend: 'dotnet build backend/KnxMonitor.sln -c Debug --nologo',
  // Solution-wide on purpose: the per-project command in CLAUDE.md covers ProjectParser.Tests only,
  // so a new test project (or a regression pin living in Infrastructure.Tests) would never be run.
  tests: 'dotnet test backend/KnxMonitor.sln --nologo --verbosity quiet',
  frontend: 'cd frontend && npm run build -- --configuration production',
}, a.gates || {})

const HOUSE_RULES = `
Repository: knx-ng-monitor, .NET 9 backend + Angular 20 frontend. Repo root is the working directory.
CLAUDE.md at the repo root is the authority on architecture and house rules — read it before touching code.
Hard rules:
- Clean Architecture: Core has no dependencies; ProjectParser stays a pure library (no EF, no Infrastructure).
- Test fixtures NEVER go into backend/KnxMonitor.ProjectParser.Tests/TestData/ (gitignored build sink).
  Fixtures live in docs/samples/ and are linked into the test project via <None Include=... Link=...>.
  Tests that need docs/samples/own/ must use [SkippableFact] + Skip.IfNot(TestSamples.Exists(...)) so CI stays green.
- Never add files under docs/samples/own/ or docs/samples/other/ to git.
- ng serve is already running in parallel — never start another dev server, never run 'ng serve'.
- EF schema changes require a migration in backend/KnxMonitor.Infrastructure/Data/Migrations/.
- Do NOT run git commit, git push, git checkout, git reset or gh pr create. Leave every change uncommitted in the working tree.
- Match the surrounding code: same naming, same comment density, same idiom. No decorative comments, no Console.WriteLine debug leftovers.
`

const GATE_BLOCK = `
Verification gates (run from the repo root, all must pass):
1. Backend build:   ${GATES.backend}
2. Backend tests:   ${GATES.tests}
3. Frontend build:  ${GATES.frontend}
Skip a gate only if the change provably cannot affect that side (e.g. a pure frontend change may skip gate 2,
a pure backend change may skip gate 3) — and say explicitly which gate you skipped and why.
If a gate fails, fix the cause and re-run it. Do not report success on a red gate.
`

const planBlock = [
  `Issue #${a.number}: ${a.title}`,
  `Branch (already checked out): ${a.branch}`,
  '',
  `Approach: ${plan.approach || '(none given)'}`,
  '',
  'Steps:',
  ...(plan.steps || []).map((s, i) => `${i + 1}. ${s}`),
  '',
  'Files to touch:',
  ...(plan.filesToTouch || []).map(f => `- ${f.path}${f.isNew ? ' (new)' : ''} — ${f.change}`),
  '',
  `Test strategy: ${plan.testStrategy || '(none given)'}`,
  ...(plan.migrationNeeded ? ['', 'An EF migration is required.'] : []),
  ...(plan.outOfScope?.length ? ['', 'Explicitly OUT OF SCOPE (do not touch):', ...plan.outOfScope.map(x => `- ${x}`)] : []),
].join('\n')

const IMPL_SCHEMA = {
  type: 'object',
  required: ['summary', 'changedFiles', 'gates', 'deviations'],
  additionalProperties: false,
  properties: {
    summary: { type: 'string', description: 'What was actually built, in a few sentences' },
    changedFiles: {
      type: 'array',
      items: {
        type: 'object',
        required: ['path', 'what'],
        additionalProperties: false,
        properties: { path: { type: 'string' }, what: { type: 'string' }, isNew: { type: 'boolean' } },
      },
    },
    gates: {
      type: 'array',
      items: {
        type: 'object',
        required: ['name', 'status'],
        additionalProperties: false,
        properties: {
          name: { type: 'string' },
          status: { type: 'string', enum: ['pass', 'fail', 'skipped'] },
          detail: { type: 'string', description: 'Failure tail, or the reason for skipping' },
        },
      },
    },
    testsAdded: { type: 'array', items: { type: 'string' }, description: 'Test names/files added or changed' },
    deviations: { type: 'array', items: { type: 'string' }, description: 'Where and why the implementation departed from the plan' },
    manualCheck: { type: 'string', description: 'How a human verifies this in the running app' },
  },
}

const FINDINGS_SCHEMA = {
  type: 'object',
  required: ['findings'],
  additionalProperties: false,
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        required: ['severity', 'file', 'problem', 'fix'],
        additionalProperties: false,
        properties: {
          severity: { type: 'string', enum: ['high', 'medium', 'low'] },
          file: { type: 'string' },
          line: { type: 'integer' },
          problem: { type: 'string', description: 'The concrete defect, with the input/state that triggers it' },
          fix: { type: 'string' },
          mustFix: { type: 'boolean', description: 'true = blocks the PR' },
        },
      },
    },
    verdict: { type: 'string', enum: ['ship', 'fix-first', 'rethink'] },
  },
}

const FIX_SCHEMA = {
  type: 'object',
  required: ['applied', 'skipped', 'gates'],
  additionalProperties: false,
  properties: {
    applied: { type: 'array', items: { type: 'string' } },
    skipped: { type: 'array', items: { type: 'object', required: ['finding', 'why'], additionalProperties: false, properties: { finding: { type: 'string' }, why: { type: 'string' } } } },
    gates: {
      type: 'array',
      items: {
        type: 'object',
        required: ['name', 'status'],
        additionalProperties: false,
        properties: { name: { type: 'string' }, status: { type: 'string', enum: ['pass', 'fail', 'skipped'] }, detail: { type: 'string' } },
      },
    },
  },
}

const REVIEW_LENSES = [
  {
    key: 'correctness',
    prompt: `Review lens: CORRECTNESS. Hunt for real defects in the diff: wrong conditions, off-by-one, null/empty handling,
async/await misuse, unawaited tasks, disposal, race conditions on the SignalR/cache path, EF query semantics,
RxJS subscription leaks in Angular components, change-detection assumptions.
For every finding, give the concrete input or state that produces the wrong behaviour. If you cannot name one, drop the finding.
Do NOT report style preferences.`,
  },
  {
    key: 'scope-and-rules',
    prompt: `Review lens: SCOPE and HOUSE RULES. Compare the diff against the issue and the approved plan.
Flag: changes outside the plan's scope, files staged that must never be committed (docs/samples/own/, docs/samples/other/,
backend/KnxMonitor.ProjectParser.Tests/TestData/, .claude/settings.local.json, data/, publish/, coverage-tmp/),
leftover debug output (Console.WriteLine, console.log, debugger), commented-out code, architecture-layer violations
(Core or ProjectParser gaining a dependency they must not have), and missing EF migrations for schema changes.
Also flag anything the issue asked for that the diff does NOT do.
Then verify every factual claim the diff adds to prose — README/CLAUDE.md tables, CHANGELOG entries, code comments —
against the code it describes. Check version numbers against CHANGELOG.md and the csproj <Version> (a fix filed under
[Unreleased] may not be attributed to a released version, and inventing the next tag's number is wrong because the
release skill will not rewrite that sentence). Check that any list of keys, flags or variables matches what the code
actually reads, key for key.`,
  },
  {
    key: 'coverage',
    prompt: `Review lens: VERIFICATION. Does the change prove itself?
For a bug fix: is there a regression test that would be RED without the production change? Verify that claim by reading the test —
if the test would pass on the old code, that is a high-severity finding.
For a feature: is the new behaviour covered, or at least is there a precise manual check?
Also check that new tests follow this repo's conventions (xUnit, [SkippableFact] + Skip.IfNot(TestSamples.Exists(...)) for
fixtures under docs/samples/own/, no fixtures written into the gitignored TestData/ sink).`,
  },
]

const DIFF_INSTRUCTIONS = `
The changes are UNCOMMITTED in the working tree. To see them:
  git status --porcelain          # includes untracked (??) files
  git diff                        # tracked modifications
Untracked new files do not show up in 'git diff' — read them directly with the Read tool.
You are read-only: do NOT edit, stage or commit anything.
`

phase('Implement')
log(`Implementing #${a.number} on ${a.branch}`)

const impl = await agent(
  `${HOUSE_RULES}\n\n## Approved plan\n${planBlock}\n\n` +
  (a.needsRegressionTest
    ? `## Regression test required\nThis is a bug fix. Write a test that fails on the current code and passes after your fix.\n` +
      `Run it red first (before the production change) so you can state that honestly, then make it green.\n\n`
    : '') +
  `## Your task\nImplement the plan on the currently checked-out branch. Read the files before editing them.\n` +
  `Follow the plan, but if the real code contradicts a step, do the correct thing and record it under deviations.\n` +
  `${GATE_BLOCK}\n` +
  `Report gate results honestly — a failing gate you could not fix goes in as status "fail" with the error tail, never as "pass".`,
  { label: 'implement', phase: 'Implement', agentType: 'general-purpose', schema: IMPL_SCHEMA, effort: 'high' },
)

if (!impl) {
  return { error: 'Implementation agent failed — inspect the working tree manually before doing anything else.' }
}

log(`Implementation done: ${impl.changedFiles?.length || 0} files, gates ${(impl.gates || []).map(g => `${g.name}=${g.status}`).join(' ')}`)

const allFindings = []
let round = 0
let lastFix = null

while (round < MAX_ROUNDS) {
  round++
  phase('Review')

  const reviews = (await parallel(REVIEW_LENSES.map(l => () =>
    agent(
      `${HOUSE_RULES}\n\n## The issue\nIssue #${a.number}: ${a.title}\n\n## The approved plan\n${planBlock}\n\n` +
      `## What the implementer says it did\n${impl.summary}\n` +
      (impl.deviations?.length ? `Deviations: ${impl.deviations.join('; ')}\n` : '') +
      `\n${DIFF_INSTRUCTIONS}\n## Your task (round ${round})\n${l.prompt}\n\n` +
      `Be adversarial but honest: report only defects you can point at in the diff. An empty findings list is a valid result.\n` +
      `Set mustFix=true for anything that would break the build, break behaviour, violate a house rule, leave the issue unresolved,\n` +
      `OR state something untrue in prose — a version number that does not exist, a precedence claim the code contradicts, an\n` +
      `enumeration in a doc table that is missing a case the code handles. Untrue documentation is the bug class this repo just\n` +
      `fixed; shipping a new instance of it inside the fix is not a nitpick.`,
      { label: `review:${l.key}#${round}`, phase: 'Review', schema: FINDINGS_SCHEMA },
    ).then(r => ({ lens: l.key, round, ...r })),
  ))).filter(Boolean)

  const fresh = reviews.flatMap(r => (r.findings || []).map(f => ({ ...f, lens: r.lens, round })))
  // 'medium' blocks too. Reviewers reserve mustFix for build/behaviour breakage, so factual defects
  // in docs and comments (invented version numbers, wrong precedence claims) came back as plain
  // 'medium' and were silently shipped until a human caught them. Fix them here instead.
  const blocking = fresh.filter(f => f.mustFix || f.severity === 'high' || f.severity === 'medium')
  allFindings.push(...fresh)

  log(`Review round ${round}: ${fresh.length} findings, ${blocking.length} blocking`)

  if (!blocking.length) break

  phase('Fix')

  lastFix = await agent(
    `${HOUSE_RULES}\n\n## The issue\nIssue #${a.number}: ${a.title}\n\n## Approved plan\n${planBlock}\n\n` +
    `## Review findings to address (round ${round})\n` +
    blocking.map((f, i) => `${i + 1}. [${f.severity}${f.mustFix ? ', mustFix' : ''}] ${f.file}${f.line ? `:${f.line}` : ''} (${f.lens})\n   Problem: ${f.problem}\n   Suggested fix: ${f.fix}`).join('\n') +
    `\n\n## Your task\nVerify each finding against the real code FIRST. Apply the ones that are genuinely wrong.\n` +
    `A finding that is mistaken goes into "skipped" with the reason — do not change working code to satisfy a bad review.\n` +
    `Stay inside the approved scope; do not opportunistically refactor.\n${GATE_BLOCK}`,
    { label: `fix#${round}`, phase: 'Fix', agentType: 'general-purpose', schema: FIX_SCHEMA, effort: 'high' },
  )

  if (!lastFix || !lastFix.applied?.length) {
    log('Nothing applied — stopping the review loop.')
    break
  }
  log(`Round ${round}: applied ${lastFix.applied.length}, skipped ${lastFix.skipped?.length || 0}`)
}

phase('Gate')

const [finalGate, prBody] = await parallel([
  () => agent(
    `${HOUSE_RULES}\n\n## Your task\nRun the verification gates from scratch on the current working tree and report the truth.\n${GATE_BLOCK}\n` +
    `Also run 'git status --porcelain' and list every path that would be picked up by a commit, so the user can spot\n` +
    `anything that must not be committed. Do not edit, stage or commit anything.`,
    {
      label: 'final-gate',
      phase: 'Gate',
      agentType: 'general-purpose',
      schema: {
        type: 'object',
        required: ['gates', 'workingTree'],
        additionalProperties: false,
        properties: {
          gates: {
            type: 'array',
            items: {
              type: 'object',
              required: ['name', 'status'],
              additionalProperties: false,
              properties: { name: { type: 'string' }, status: { type: 'string', enum: ['pass', 'fail', 'skipped'] }, detail: { type: 'string' } },
            },
          },
          workingTree: { type: 'array', items: { type: 'string' }, description: 'git status --porcelain lines' },
          suspiciousPaths: { type: 'array', items: { type: 'string' }, description: 'Paths that look like they must not be committed' },
        },
      },
    },
  ),
  () => agent(
    `## The issue\nIssue #${a.number}: ${a.title}\n\n## Approved plan\n${planBlock}\n\n` +
    `## What was implemented\n${impl.summary}\n${(impl.changedFiles || []).map(f => `- ${f.path} — ${f.what}`).join('\n')}\n` +
    (impl.testsAdded?.length ? `\nTests added: ${impl.testsAdded.join(', ')}\n` : '') +
    (impl.manualCheck ? `\nManual check: ${impl.manualCheck}\n` : '') +
    `\n${DIFF_INSTRUCTIONS}\n## Your task\nRead the actual diff, then write the pull-request body in English, GitHub Markdown.\n` +
    `Structure: a one-paragraph summary, '## Changes' (bullet list per area), '## Verification' (what was run / how to check manually),\n` +
    `and '## Notes' only if there is something a reviewer must know (deviations, follow-ups, deliberate out-of-scope items).\n` +
    `End with the line: Closes #${a.number}\n` +
    `No marketing language, no emoji, no "Generated with" footer. Describe only what the diff really does.\n` +
    `Never attribute the change to a version number — the CHANGELOG's [Unreleased] entry is the only place a version\n` +
    `gets stamped, and guessing the next tag ships a false claim. Phrase it relative to the last released version instead.\n` +
    `For anything no gate could prove (e.g. runtime behaviour needing a started app), write the check as a numbered recipe\n` +
    `under a '### Pending manual verification' subheading — the session runs it afterwards and replaces that block with the\n` +
    `real results, so do not present it as if it had already passed.`,
    { label: 'pr-body', phase: 'Gate', schema: { type: 'object', required: ['body', 'commitSubject'], additionalProperties: false, properties: { body: { type: 'string' }, commitSubject: { type: 'string', description: 'Conventional-commit subject, <= 72 chars, no trailing period' } } } },
  ),
])

const gates = finalGate?.gates || impl.gates || []
const green = gates.every(g => g.status !== 'fail')

log(`Final gates: ${gates.map(g => `${g.name}=${g.status}`).join(' ')} → ${green ? 'GREEN' : 'RED'}`)

return {
  issue: { number: a.number, title: a.title },
  branch: a.branch,
  green,
  implementation: impl,
  reviewRounds: round,
  findings: allFindings,
  lastFix,
  finalGate,
  pr: prBody,
}
