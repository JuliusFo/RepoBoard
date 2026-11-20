# Agent briefs (complete-code-review)

Each agent gets its block below verbatim, in addition to what `SKILL.md` step 2 lists for every prompt. The blocks say what
to check at least – an agent may go further within its topic. "Leave to" names the agent that owns a neighbouring
question, so a finding is reported once.

Common to all: read the files in question, never guess from names. A finding needs a failure scenario someone could
reproduce (input, steps, wrong result), not "could be a problem". Check the "Ohne Befund geprüft" list of the last review
and say whether it still holds for your topic.

## GI – Git and auto-sync

- Every git call goes through `GitRunner` and is a method of `GitClient`; time limit, `GitCallMode` (background never prompts).
- Parsing: porcelain v2 with `-z` (renames, umlauts, spaces), `for-each-ref`, `log` separators; initial repository, detached
  HEAD, no upstream, gone upstream, worktree (`.git` as a file), submodules.
- ADR 0003: auto-pull only fast-forward, every case of `PullCheck`; nothing that can discard work (`CLAUDE.md` §2).
- Messages: `GitResult.Message`, the same failure logged once, texts from `SyncText`.
- Leave to: ST (who calls when), SE (where the input comes from).

## ST – State and concurrency

- Gates: one operation per repository, refresh/auto-sync skip a busy one, clicks wait; the network semaphore; what a
  hanging git (no answer, sign-in window nobody sees) blocks and for how long.
- `RepoState` written from background threads, read by the UI: replaced as a whole, no torn reads that matter.
- `AutoSyncService`: interval, a tick that throws, cancellation at shutdown, a repository removed during its sync.
- Config: concurrent saves, a file this version cannot read, a save that fails.
- Leave to: GI (git correctness), UI (rendering).

## UI – UI and CSS

- Components follow `.claude/rules/ui*.md`: `BoardComponent` guards, `EventCallback` for child changes, icons only from
  `Icons.cs` with one meaning each, `title` tooltips, no hard-coded texts.
- WebView2 host traps (no `@ondblclick`, `<select>` value, culture). A click path tested only via CDP is a finding with
  "mit echter Maus prüfen".
- CSS: tokens only, dark and light, light fills mixed from tokens, no dead classes; render cost (the shell renders on every
  `BoardService.Changed`).
- Leave to: ST (lifetimes), SE (markup with outside text).

## PH – Host, processes, build

- App host: `Program.Main` (culture, instance lock, data folder), `App` start and exit, `MainWindow`, `WindowTheme`,
  `WpfDesktop` (process starts, paths).
- Build: `Directory.Build.props`, central package versions (outdated or vulnerable: `dotnet list package --vulnerable
  --include-transitive` only reads), versions in step with FerretSharp, project references against the layer rules.
- CI workflow: jobs, permissions, pinned actions where a job may push, what runs on Linux vs Windows.
- Leave to: SE (input from outside into processes), DO (`docs/release.md`).

## SE – Security

- Follow the checklist's "Security" section for every source listed there, each traced to every sink; name source,
  boundary check and the test that fails without it.
- Repositories found by a scan run their own config and hooks with RepoBoard's git calls: is that visible to the user?
- Credentials: remote URLs with tokens in logs and the activity log.
- Leave to: nobody – a security question is always reported here.

## RE – Reuse and dead code

- Duplicates and copies that drift apart; helpers that exist but are not used (also those taken from FerretSharp).
- Dead code: unused types, members, resx keys, CSS classes.
- Readability and structure: files in the wrong folder, types in the wrong layer.
- Leave to: TE (duplicate tests), DO (docs that drift).

## TE – Tests

Only TE builds and runs the tests: `dotnet build -warnaserror` and `dotnet test` on the snapshot once at the start.

- Gaps: every fix since the last review has a test that fails without it; untested branches in `Core` and `UI/State` that
  decide something (gates, `PullCheck`, config states, parsing).
- Weak and flaky: asserts that cannot fail, fixed waits, real clocks instead of `TimeProvider`, tests that depend on
  culture, order, the machine's git config or Windows paths (`.claude/rules/tests.md`). The ten slowest tests and whether
  each needs to be slow (git tests start processes).
- Redundant: what can become a `[Theory]` case or be removed – for every proposal, which test still catches the failure.
- Leave to: RE (duplicate production code).

## DO – Docs

- `README.md` matches the app; `CLAUDE.md` still true and only what always applies (estimate its tokens).
- `.claude/rules/`: every `paths` glob matches files, no rule without `paths`, contradictions, traps no longer needed.
- `docs/architecture.md`, ADRs (numbering, status, "Vorschlag" vs. decided), protocols and index, roadmap and backlog.
- All relative Markdown links resolve; CHANGELOG sections match the tags.
- Leave to: RE (code comments).
