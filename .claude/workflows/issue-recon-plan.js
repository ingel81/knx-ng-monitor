export const meta = {
  name: 'issue-recon-plan',
  description: 'Recon a GitHub issue against the knx-ng-monitor codebase and produce one vetted implementation plan',
  whenToUse: 'Phase 1 of the /issue skill: the issue text has been fetched, no code has been written yet. Returns a plan for the user to approve.',
  phases: [
    { title: 'Recon', detail: 'three lenses read the codebase for this issue' },
    { title: 'Draft', detail: 'three independent plan candidates' },
    { title: 'Judge', detail: 'score the candidates and synthesize the winner' },
  ],
}

// args: { number, title, body, labels: string[], comments: string, repoRoot }
const issue = args || {}
const issueBlock = [
  `Issue #${issue.number}: ${issue.title}`,
  `Labels: ${(issue.labels || []).join(', ') || '(none)'}`,
  '',
  'Body:',
  issue.body || '(empty)',
  '',
  'Comments:',
  issue.comments || '(none)',
].join('\n')

const HOUSE_RULES = `
Repository: knx-ng-monitor (D:/Source/knx-ng-monitor), .NET 9 backend + Angular 20 frontend.
Read CLAUDE.md at the repo root FIRST — it is the authority on architecture, commands and house rules.
Non-negotiable house rules:
- Clean Architecture: Core has no dependencies; ProjectParser is a pure library (no EF, no Infrastructure).
- Never put test fixtures into backend/KnxMonitor.ProjectParser.Tests/TestData/ (build sink, gitignored).
  Sample fixtures live in docs/samples/ only. Tests needing docs/samples/own/ must use [SkippableFact].
- Never commit proprietary sample files.
- ng serve is already running in parallel; do not start another dev server.
- EF schema changes need a migration under backend/KnxMonitor.Infrastructure/Data/Migrations/.
`

const RECON_SCHEMA = {
  type: 'object',
  required: ['summary', 'relevantFiles', 'risks'],
  additionalProperties: false,
  properties: {
    summary: { type: 'string', description: 'What the existing code does today in the area this issue touches' },
    relevantFiles: {
      type: 'array',
      items: {
        type: 'object',
        required: ['path', 'why'],
        additionalProperties: false,
        properties: {
          path: { type: 'string' },
          why: { type: 'string' },
          keySymbols: { type: 'array', items: { type: 'string' } },
        },
      },
    },
    existingPatterns: { type: 'array', items: { type: 'string' }, description: 'Conventions in this repo that a fix must follow' },
    risks: { type: 'array', items: { type: 'string' } },
    notInScope: { type: 'array', items: { type: 'string' } },
  },
}

const PLAN_SCHEMA = {
  type: 'object',
  required: ['approach', 'steps', 'filesToTouch', 'testStrategy', 'risks'],
  additionalProperties: false,
  properties: {
    approach: { type: 'string', description: 'One paragraph: the shape of the change' },
    steps: { type: 'array', items: { type: 'string' } },
    filesToTouch: {
      type: 'array',
      items: {
        type: 'object',
        required: ['path', 'change'],
        additionalProperties: false,
        properties: {
          path: { type: 'string' },
          change: { type: 'string' },
          isNew: { type: 'boolean' },
        },
      },
    },
    testStrategy: { type: 'string' },
    migrationNeeded: { type: 'boolean' },
    risks: { type: 'array', items: { type: 'string' } },
    outOfScope: { type: 'array', items: { type: 'string' } },
    openQuestions: { type: 'array', items: { type: 'string' }, description: 'Only genuine blockers a human must answer' },
  },
}

const VERDICT_SCHEMA = {
  type: 'object',
  required: ['winner', 'rationale', 'plan', 'branch', 'prTitle', 'changeKind'],
  additionalProperties: false,
  properties: {
    winner: { type: 'integer', description: 'Index of the strongest candidate (0-based)' },
    rationale: { type: 'string' },
    changeKind: { type: 'string', enum: ['fix', 'feat', 'refactor', 'docs', 'perf', 'chore'] },
    branch: { type: 'string', description: 'e.g. fix/9-aspnetcore-urls — kebab-case, includes the issue number' },
    prTitle: { type: 'string', description: 'Conventional-commit style subject, no trailing period' },
    plan: PLAN_SCHEMA,
    graftedIdeas: { type: 'array', items: { type: 'string' }, description: 'Good ideas taken from the losing candidates' },
    confidence: { type: 'string', enum: ['high', 'medium', 'low'] },
  },
}

const LENSES = [
  {
    key: 'backend',
    prompt: `Recon lens: BACKEND (.NET 9 — KnxMonitor.Core / Infrastructure / Api / ProjectParser).
Find every backend file this issue touches or depends on. Read the actual code, do not guess.
Note DI registrations in Program.cs, EF entities/migrations, SignalR hubs, DTOs and interfaces that would have to change.
If the issue is purely frontend, say so plainly and return an empty relevantFiles list rather than inventing work.`,
  },
  {
    key: 'frontend',
    prompt: `Recon lens: FRONTEND (Angular 20 standalone components, Angular Material, AG-Grid, SignalR client).
Find every frontend file this issue touches: components, services under core/, feature modules, shared models, routes, styles.
Note how similar UI already works (there is usually a pattern to copy). Check the shared models for DTO shapes the backend sends.
If the issue is purely backend, say so plainly and return an empty relevantFiles list rather than inventing work.`,
  },
  {
    key: 'conventions',
    prompt: `Recon lens: CONVENTIONS, TESTS and DOCS.
Read CLAUDE.md fully. Establish: which test project covers this area and how tests are written there
(xUnit, SkippableFact, TestSamples helper), whether docs/ai/*.md or README.md must be updated for this change,
whether an EF migration is required, and which house rules could be violated by a careless fix.
Also check git log for how similar changes were done before (git log --oneline -30, and git log -S<symbol> where useful).
List concrete rules the implementer must obey.`,
  },
]

const ANGLES = [
  {
    key: 'minimal',
    prompt: `Design the SMALLEST correct change that fully resolves the issue. Touch as few files as possible.
No refactoring, no new abstractions, no adjacent cleanups. If the minimal change is genuinely inadequate, say why in risks.`,
  },
  {
    key: 'idiomatic',
    prompt: `Design the change a maintainer of THIS repo would make: follow the existing patterns exactly
(Clean Architecture layering, repository + DI, standalone components, existing service shapes).
Prefer reusing what is already there over adding new pieces. Slightly larger than minimal is fine if it removes duplication.`,
  },
  {
    key: 'test-first',
    prompt: `Design the change test-first. Start from the failing test or the reproduction, then the production change.
For a bug: name the exact test that must be red before the fix and green after, and where it lives.
For a feature: name the coverage that proves the feature works. Then describe the production change that satisfies it.`,
  },
]

phase('Recon')
log(`Recon on issue #${issue.number}: ${issue.title}`)

const recon = (await parallel(LENSES.map(l => () =>
  agent(
    `${HOUSE_RULES}\n\n${issueBlock}\n\n${l.prompt}\n\nYou are read-only: do NOT edit any file.`,
    { label: `recon:${l.key}`, phase: 'Recon', schema: RECON_SCHEMA },
  ).then(r => ({ lens: l.key, ...r })),
))).filter(Boolean)

if (!recon.length) {
  return { error: 'All recon agents failed — nothing to plan from.' }
}

const reconBlock = recon.map(r => [
  `### Lens: ${r.lens}`,
  r.summary,
  '',
  'Relevant files:',
  ...(r.relevantFiles || []).map(f => `- ${f.path} — ${f.why}${f.keySymbols?.length ? ` [${f.keySymbols.join(', ')}]` : ''}`),
  ...(r.existingPatterns?.length ? ['', 'Patterns to follow:', ...r.existingPatterns.map(p => `- ${p}`)] : []),
  ...(r.risks?.length ? ['', 'Risks:', ...r.risks.map(x => `- ${x}`)] : []),
].join('\n')).join('\n\n')

log(`Recon done: ${recon.reduce((n, r) => n + (r.relevantFiles?.length || 0), 0)} relevant files across ${recon.length} lenses`)

phase('Draft')

const candidates = (await parallel(ANGLES.map(a => () =>
  agent(
    `${HOUSE_RULES}\n\n${issueBlock}\n\n## Recon findings from the codebase\n${reconBlock}\n\n` +
    `## Your task\n${a.prompt}\n\n` +
    `Verify the recon claims against the real files before you rely on them — read the code yourself.\n` +
    `You are read-only: produce a PLAN, do NOT edit any file.\n` +
    `Scope discipline: solve exactly what the issue asks. List anything you deliberately leave alone under outOfScope.\n` +
    `Only put a genuine blocker into openQuestions — a question that, answered either way, changes the implementation.`,
    { label: `plan:${a.key}`, phase: 'Draft', schema: PLAN_SCHEMA },
  ).then(p => ({ angle: a.key, ...p })),
))).filter(Boolean)

if (!candidates.length) {
  return { error: 'All plan agents failed.', recon }
}

const candidateBlock = candidates.map((c, i) => [
  `### Candidate ${i} (${c.angle})`,
  c.approach,
  '',
  'Steps:',
  ...(c.steps || []).map((s, n) => `${n + 1}. ${s}`),
  '',
  'Files:',
  ...(c.filesToTouch || []).map(f => `- ${f.path}${f.isNew ? ' (new)' : ''} — ${f.change}`),
  '',
  `Tests: ${c.testStrategy}`,
  ...(c.risks?.length ? ['Risks: ' + c.risks.join('; ')] : []),
  ...(c.outOfScope?.length ? ['Out of scope: ' + c.outOfScope.join('; ')] : []),
  ...(c.openQuestions?.length ? ['Open questions: ' + c.openQuestions.join('; ')] : []),
].join('\n')).join('\n\n')

phase('Judge')

const verdict = await agent(
  `${HOUSE_RULES}\n\n${issueBlock}\n\n## Recon findings\n${reconBlock}\n\n## Candidate plans\n${candidateBlock}\n\n` +
  `## Your task\nPick the strongest candidate and synthesize the FINAL plan from it, grafting the best ideas from the others.\n` +
  `Judge on: (1) does it actually resolve what the issue asks, (2) does it fit this repo's architecture and house rules,\n` +
  `(3) is it verifiable by a test or a concrete manual check, (4) is it free of scope creep.\n` +
  `Read the files the plan touches before you commit to it — reject steps that do not match the real code.\n` +
  `Produce a branch name (kebab-case, contains the issue number, prefix fix/ or feat/ matching changeKind) and a\n` +
  `conventional-commit PR title. Set confidence to 'low' if the issue text is too vague to implement without guessing.\n` +
  `You are read-only: do NOT edit any file.`,
  { label: 'judge:synthesize', phase: 'Judge', schema: VERDICT_SCHEMA, effort: 'high' },
)

if (!verdict) {
  return { error: 'Judge failed.', recon, candidates }
}

log(`Plan ready — ${verdict.branch} (confidence: ${verdict.confidence})`)

return {
  issue: { number: issue.number, title: issue.title },
  verdict,
  recon,
  candidateSummaries: candidates.map((c, i) => ({ index: i, angle: c.angle, approach: c.approach })),
}
