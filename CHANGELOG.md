# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

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
