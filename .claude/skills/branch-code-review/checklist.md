# Review checklist (RepoBoard)

Shared by `branch-code-review` and `complete-code-review`. Hand this file to every reviewer agent; it lists what is specific to this project. General review craft (logic errors, edge cases, resource leaks) applies on top.

## Severity

- **S** lost or damaged work in a repository, data loss in RepoBoard's own files, security hole, hang
- **M** wrong behaviour in realistic cases, or a clear maintenance burden
- **K** minor

## Correctness

- Async: every git call takes the `CancellationToken` and has a time limit; nothing blocks on `.Result`; a component may be disposed while an await is pending.
- Concurrency: one operation per repository (`RepoState.Gate`), at most four network operations; refresh and auto-sync skip a busy repository, clicks wait. `RepoState` properties replaced as a whole (read from the UI thread); events unsubscribed on dispose.
- Git semantics: detached HEAD, no upstream, initial repository without commits, unfinished merge/rebase/cherry-pick/revert, conflicts, renames (`-z`, original path as its own field), paths with spaces and umlauts, worktrees and submodules (`.git` as a file), a repository folder that disappears.
- Offline, sign-in needed, slow remote: what does the user see, does auto-sync log the same failure only once, can the user still use the rest of the board and quit?

## Invariants (`CLAUDE.md` §2 and §4)

- Nothing that can discard uncommitted work (`reset --hard`, `clean`, `checkout -- <path>`, `restore` on the worktree, `push --force`, `branch -D`, `stash drop`) without its own narrow `GitClient` method, a confirmation in the UI, an ADR and tests.
- Auto-pull only fast-forward and only when `PullCheck` finds nothing against it (ADR 0003); background calls never prompt (`GitCallMode.Background`).
- Processes only in `GitRunner` and the host (`ArchitectureTests`); every git command text in `GitClient`; values from outside after `--` or checked (`IsBranchNameAsync`).
- No `System.Windows` or Windows-only APIs in Core and UI; host-only features behind interfaces implemented in `RepoBoard.App`.
- Own files: write via `AtomicJsonFile`, read via `VersionedJsonFile`; a file this version cannot read is never overwritten.
- Recursive delete only via `SafeDelete.DirectoryBelow` / `TestFolder`.

## Security: follow outside input to where it lands

A fixed pass, not a general impression: for every source the change touches, trace the value to every sink it reaches.

Sources (untrusted):
- Repositories found by a scan: their `.git/config` (`core.fsmonitor`, `core.hooksPath`, aliases) and hooks run with every git call RepoBoard makes there.
- Git output: branch and remote names, file paths, commit messages and author names (shown in the UI, passed back to git).
- The config file edited by hand; paths in it.
- Text the user types: branch names, commit messages, the search field.

Sinks (never unchecked):
- git arguments: an outside value never in option position (`--`, `IsBranchNameAsync`).
- Processes in the host: `ArgumentList` only; `cmd /c` only with a checked path (`WpfDesktop.OpenInEditor`).
- WebView: no `MarkupString` with outside text (commit messages, branch names); `title` as an attribute value only.
- Paths: relative paths and duplicates from the config, UNC shares (a scan or an entry on `\\host\share` makes git log in to a foreign host).
- Log and activity log: no credentials (a remote URL with a token in it, `https://user:token@host`).

A security finding names the concrete path from source to sink, the check at the boundary and a test that fails without the fix.

## UI

- Every visible text in English and German (typed resx, `ResourceTests`); no hard-coded strings in markup.
- Colours via the tokens in `repoboard.css`; no hard-coded colours, light fills mixed from tokens.
- Icons only from `Icons.cs`, one meaning per icon; tooltips as `title`.
- WebView2 host traps from `.claude/rules/ui.md` (no `@ondblclick`, culture set synchronously).

## Tests

- Core logic has unit tests; git behaviour is tested against real repositories in `GitSandbox` (isolated from the machine's git config).
- A bug fix comes with a test that fails without it, where feasible.
- Rules in `.claude/rules/tests.md` (UI culture, number formats, Linux paths, `TestFolder`, redundant tests).

## Docs

- `CHANGELOG.md`, work-package protocol plus index line, ADR for non-trivial decisions, `docs/architecture.md`, `.claude/rules/` and `README.md` (every visible function) still true after the change.
- No names of the user's private repositories or remotes in code, tests, docs or commits beyond what `CLAUDE.md` already names; neutral examples only.

## Not a finding

- Decisions the user already made ("Entscheidung des Nutzers" in `docs/work-packages/`, ADRs).
- Items already in `docs/backlog.md` or `docs/roadmap.md`, unless the change makes them worse.
- Style preferences without a concrete cost.
