# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Fixed
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
