# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Added
- One page for all repositories (WP-01): branch, commits ahead and behind, staged, changed, new and conflicted files, the time of the last fetch (also from the IDE or terminal) and a merge or rebase still in progress. Filter chips with counts (changes, behind, ahead, auto-sync, problems) and a search over name, path and branch.
- Fetch, pull (fast-forward only) and push per repository, fetch all; the first push of a branch sets its upstream.
- An opened row stages and unstages files, commits the staged files or everything, commits and pushes, switches to a local or remote branch and creates a new one, and shows the last 15 commits.
- Auto-fetch and auto-pull per repository, switched on and off in the table, at an interval of 1 to 60 minutes. Auto-pull only fast-forwards a branch that has only fallen behind; it leaves a detached HEAD, a branch without upstream or with local commits, conflicts and an unfinished merge or rebase alone and says why. Background syncs never ask for credentials.
- An activity log of what happened, above all what auto-sync did; the same failure is logged once.
- Add a repository or scan a folder for repositories (three levels deep); open a repository in the Explorer, a terminal or VS Code.
- English and German (`--lang=en|de`, otherwise the language of Windows), light and dark following Windows (`--theme=dark|light`).

### Internal
- Project set up like FerretSharp: .NET 10, WPF host with BlazorWebView, Razor class library, Core without Windows APIs; rules, skills, ADRs, CI on Linux and Windows.
