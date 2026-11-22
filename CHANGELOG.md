# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Security
- “Scan folder” no longer runs git in what it finds before you agree: it lists the repositories with what each one’s own configuration would run on RepoBoard’s git calls – a program on every status (`core.fsmonitor`), hooks, an ssh command, included configuration – and whether a worktree’s repository lies outside the folder. Only plain repositories inside the folder are ticked; you tick the others.
- Unicode direction controls in a diff or commit message show as ⟨U+202E⟩ instead of reordering the line (“Trojan Source”).
- A commit whose subject or author holds the old field separators can no longer add a made-up commit to the history or shift the fields of its row.
- Opening a folder whose name contains “;” in Windows Terminal no longer runs the rest of the name as a command.
- A worktree entry pointing at another computer’s share is left out unless the repository itself is on a share.

### Fixed
- Auto-pull and Pull never use git’s autostash: with `merge.autoStash` (or `pull.rebase` with `rebase.autoStash`) set, a fast-forward over overlapping local changes put them back with conflict markers and reported success. Now git refuses and your file stays as it was.
- A failing auto-pull (local changes in the way) is reported once instead of on every interval, with a Windows notification each time.
- Auto-sync and “Fetch all” no longer hold every repository busy while they wait for the network: offline, all rows showed a spinner and commit was locked for minutes. A network slot stays free for your clicks.
- Switching to a remote branch creates a local branch that tracks exactly that one; it used to switch to the local `main` of another remote or to an unrelated local branch of the same name.
- A repository folder whose `.git` was deleted counts as missing; before, RepoBoard showed, auto-synced and committed the repository around it.
- The auto-pull’s fast-forward has the network time limit and never asks for credentials in the background (Git LFS, partial clones).
- A clone of an empty repository no longer shows “upstream deleted” and gets the first commit pushed to it.
- A second start while RepoBoard is still starting (autostart at sign-in) brings the window instead of saying “already running”; an elevated running instance no longer crashes it.
- A fetch of a repository and a pull in its worktree at the same time no longer fail with “cannot lock ref”.
- “All branches” or “Load 50 more” while the status is read is no longer lost.
- Dates in the branch list and history, and how a branch stands to its upstream, are in the app’s language.
- Adding a repository git refuses (`safe.directory`, a broken config) shows git’s reason.
- A missing repository folder says so instead of “git was not found”.
- “In VS Code” without VS Code on the PATH says so.
- The checkbox of a file whose stage or unstage failed shows the file’s real state again, on both lists.
- A repository without a commit no longer counts as “ahead”.
- The amend tooltip says “unknown” instead of “already pushed” when git cannot tell.
- A commit message whose first paragraph spans two lines shows the same subject everywhere.

### Changed
- Removing a repository from the board has its own icon and an activity entry; the X only hides or closes.
- Dialogs and the settings menu close with Esc.
- The folder dialog starts where your repositories are.
- The window fits the screen’s work area; the page’s language follows the app’s, as does Blazor’s error bar.
- Light mode: warning, error and green texts have more contrast; the backdrop behind a dialog darkens in the dark mode.
- The tick no longer reads a status another reader just read; fewer git processes at start.

### Internal
- Review 0.4.0 (R1): `Paths` for path comparisons, `PickCheck` for the commit tools’ rules, `DiffBody`, `CommitText`, `UpdateComponent`, FerretSharp leftovers removed; the CI builds the self-contained release publish before a tag; tests for the auto-sync cases of ADR 0003, config states, `GitRunner`, the scanner and more.

## [0.4.0] - 2026-11-22

### Added
- Stage hunks and lines (WP-25): each hunk of the diff view has a button “Stage hunk” (“Unstage hunk” on the staged side). A click on a changed line at the left edge selects it, Shift+click selects a range; the button then stages or unstages only the selected lines. Works for new files (the index gets a new file with just those lines) and deleted ones. Only the index is changed, never the file on disk; if the file changed since the diff was read, git refuses the patch and nothing is staged.
- History and commit tools (WP-09): the history of an opened row shows a graph of branches and merges, “All branches” adds every local and remote branch, “Load 50 more” goes further back (up to 1,000 commits). A click on a commit shows its whole message, author and committer, parents (click to go there), refs, the files it changed and their diff against the first parent, in place of branches and history.
- Revert a commit of the current branch or cherry-pick one of another branch – only without staged, changed or conflicted files, not for merge commits and not when an ignored file lies at one of the commit’s paths (git would overwrite it without a word); a change the branch already has is not picked again. If git stops at a conflict – or a cherry-pick comes out empty, as after a squash merge – it stays unfinished for the IDE or terminal and the row says how to go on.
- “Amend last commit” in the commit box: starts from the last commit’s message and replaces that commit with the staged changes and the new message – only while no remote branch has it, so never a force push, and only the commit whose message it started from: if another commit becomes the last one meanwhile, the box unticks and says so.

### Internal
- Partial staging through `git apply --cached` (ADR 0012): `HunkPatch` builds the patch from the shown hunk and the selected lines, `GitRunner` can now pass input on stdin.
- Commit graph and commit tools (ADR 0013): `CommitGraph` lays out lanes from `git log --topo-order` with parents, `GitClient` reads commit details and diffs through `git show --diff-merges=first-parent`; commit ids are checked to be hex before any command takes them.

## [0.3.0] - 2026-11-21

### Added
- Instant refresh (WP-03): RepoBoard watches each repository’s folder and git folder and reads its status about a second after something changed – a commit, branch switch, fetch, staged or saved file in the IDE or terminal, or a new worktree. Build output (`bin`, `obj`, `node_modules`, `.vs`, `.idea`, `TestResults`) is left out; during a long series of changes the status is read at most every three seconds.

### Changed
- The status tick reads watched repositories and worktrees only every two minutes as a fallback instead of every 20 seconds, so many worktrees no longer mean a git process each every 20 seconds; repositories that cannot be watched (a network drive, a folder that is gone) are still read every 20 seconds.

### Internal
- File watchers (ADR 0011): `RepoWatchService`, `WatchMap` and `ChangeDebouncer` in Core; in the git folder only a positive list of files counts, so what RepoBoard's own `git status` writes with fsmonitor or Git LFS never triggers the next read. Git tests stop looking for a repository at the temp folder (`GIT_CEILING_DIRECTORIES`).

## [0.2.0] - 2026-11-21

### Added
- Worktrees (WP-04): a repository’s linked worktrees show as rows below it, closed behind a count in its row – each with its branch, ahead and behind, changes and an unfinished merge or rebase, and the same actions as a repository (open, stage, commit, diff, switch branch, pull, push, Explorer, terminal, VS Code). Worktrees made by Claude Code sessions are marked “Claude”, locked ones “locked”. The chips and the search look at worktrees too. A worktree you add or scan shows below its repository instead of on its own.
- Remove a worktree, or “Clean up …” the worktrees of a repository that look done – branch merged into the remote’s default branch, upstream deleted, detached on a merged commit, folder gone. RepoBoard asks first and lists, per worktree, every ignored file git deletes with it, and checks again right before removing; a worktree with changed or new files, a locked one, one with an unfinished merge or rebase and a detached one with commits no branch holds are shown but never removed. Only worktrees whose upstream was deleted or whose folder is gone are ticked from the start. The branch always stays.
- Diff view (WP-05): a click on a changed file in an opened row shows its staged or unstaged changes next to the list, in place of branches and history – git’s own diff, inline, with old and new line numbers, the changed words highlighted, Windows line endings and a missing newline at the end marked, binary files named. The diff follows the file when you stage or unstage it and is read again with each new status; Esc, the X or a second click closes it.

### Fixed
- A branch whose upstream was deleted on the remote (as after a merged pull request) showed “up to date”; it now shows “upstream deleted”.
- Hover effects and the result of a click often showed late or only with the next mouse move: RepoBoard now waits until each picture of the page is really copied before the window shows it.

### Internal
- `RepoBoard.App.Tests` (Windows): the Direct3D interfaces of the copy wait and the WebView2 members it relies on; `tools/e2e/Measure-PictureDelay.ps1` measures how long a hover or click takes to show in the window.

## [0.1.0] - 2026-11-21

### Security
- Credentials in git’s messages are masked before they reach the row, the activity log and Windows notifications: the password or token in a remote URL (`https://user:***@host`, `https://***@host`) and GitHub and GitLab tokens in a server’s `remote:` lines.

### Added
- One page for all repositories (WP-01): branch, commits ahead and behind, staged, changed, new and conflicted files, the time of the last fetch (also from the IDE or terminal) and a merge or rebase still in progress. Filter chips with counts (changes, behind, ahead, auto-sync, problems) and a search over name, path and branch.
- Fetch, pull (fast-forward only) and push per repository, fetch all; the first push of a branch sets its upstream.
- An opened row stages and unstages files, commits the staged files or everything, commits and pushes, switches to a local or remote branch and creates a new one, and shows the last 15 commits.
- Auto-fetch and auto-pull per repository, switched on and off in the table, at an interval of 1 to 60 minutes. Auto-pull only fast-forwards a branch that has only fallen behind; it leaves a detached HEAD, a branch without upstream or with local commits, conflicts and an unfinished merge or rebase alone and says why. Background syncs never ask for credentials.
- An activity log of what happened, above all what auto-sync did; the same failure is logged once.
- Add a repository or scan a folder for repositories (three levels deep); open a repository in the Explorer, a terminal or VS Code.
- English and German (`--lang=en|de`, otherwise the language of Windows), light and dark following Windows (`--theme=dark|light`).
- Notification area (WP-02): closing the window keeps RepoBoard and auto-sync running; the icon’s menu opens the window, fetches all, pauses auto-sync until the next start and exits, its tooltip counts repositories behind or with problems. Starting RepoBoard again brings the hidden window back.
- Windows notifications for auto-pulls, failures and skipped auto-pulls, one per auto-sync run; a click on the notification opens the repository’s row. Choose auto-pulls and problems, problems only or off.
- Start with Windows in the notification area (`--minimized`), and a settings menu for closing, start with Windows, notifications and the pause.
- `--exit` ends the RepoBoard running on a data folder; a program icon.
- Installer and updates (WP-10): `RepoBoard-win-Setup.exe` installs for your user without admin rights, `RepoBoard-win-Portable.zip` runs without installing; both update themselves. RepoBoard checks the GitHub releases at start and while it runs (hourly, every 4 hours or daily; switch off or “Check now” in the settings) and only reports: a button in the top bar, once a Windows notification per new version, a dialog with what is new. Downloading and the restart into the new version wait for your click.

### Internal
- Project set up like FerretSharp: .NET 10, WPF host with BlazorWebView, Razor class library, Core without Windows APIs; rules, skills, ADRs, CI on Linux and Windows.
- Release workflow on a version tag: Velopack packages (full and delta), release notes from this changelog.
- E2E: `Start-TestInstance.ps1 -Exe` starts a portable Velopack test version; `Invoke-TrayIcon.ps1` drives the notification-area icon without injected input; `Stop-TestInstance.ps1` ends through `--exit`.
