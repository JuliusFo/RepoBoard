# RepoBoard

All your Git repositories on one page: which branch each one is on, how far it is ahead of or behind its upstream, what
changed locally – and auto-fetch and auto-pull per repository, so the ones you choose are always up to date.

Built with .NET 10, a WPF host and a Blazor UI (the same stack as FerretSharp). Git runs through your installed `git`, so
sign-in, SSH, hooks and LFS work exactly as in your terminal.

## Features

- **Overview:** every repository with its branch (feature branches stand out, a detached HEAD is marked), commits ahead
  and behind, staged, changed, new and conflicted files, the time of the last fetch and an unfinished merge or rebase.
- **Filter and search:** chips with counts – changes, behind, ahead, auto-sync, problems – and a search over name, path and
  branch.
- **Git actions per repository:** fetch, pull (fast-forward only), push (the first push sets the upstream), fetch all.
- **Commit:** open a row to stage or unstage files, commit the staged files or everything, commit and push.
- **Diff:** click a changed file to see its staged or unstaged changes right next to the list – git’s own diff, with line
  numbers and the changed words highlighted; line endings and binary files show as git sees them.
- **Worktrees:** a repository’s linked worktrees (`git worktree`) as rows below it, behind a count you click to show
  them – with branch, ahead/behind and changes, and the same actions; worktrees made by Claude Code sessions are marked.
  Remove one, or clean up those that look done (branch merged, upstream deleted, folder gone): RepoBoard asks first,
  lists the ignored files git deletes with it, and never removes a worktree with changes. The branch always stays.
- **Branches:** switch to a local or remote branch, create a new one; the last 15 commits with their refs.
- **Auto-fetch and auto-pull** per repository, at an interval of 1 to 60 minutes. Auto-pull only fast-forwards a branch
  that has only fallen behind – never a merge, never over your local commits, never during a merge or rebase – and says
  why when it keeps out. Background syncs never ask for credentials.
- **Activity log** of what happened while you were not looking.
- **Notification area:** closing the window keeps RepoBoard and auto-sync running next to the clock; the icon’s menu
  opens it, fetches all, pauses auto-sync (until the next start) and exits. Windows notifications for auto-pulls and
  problems, gathered per run – a click opens the repository. Optionally start with Windows, in the notification area
  only. All in the settings menu at the top right.
- **Updates:** RepoBoard looks for a newer version at start and while it runs and tells you – in the top bar and once as
  a Windows notification – with what is new; downloading and restarting wait for your click. Switch it off or check now
  in the settings.
- **Repositories:** add one, or scan a folder (three levels deep); open it in the Explorer, a terminal or VS Code. A
  worktree you add or scan shows below its repository.
- English and German, light and dark.

## Install

Download from the [releases](https://github.com/JuliusFo/RepoBoard/releases) (Windows 10/11, x64):

- `RepoBoard-win-Setup.exe` installs RepoBoard for your user (no admin rights) to `%LocalAppData%\RepoBoard`.
- `RepoBoard-win-Portable.zip` runs from any folder without installing.

Both update themselves. The installer is not signed: Windows SmartScreen warns once (More info › Run anyway).

## Requirements

- Windows 10/11 with the WebView2 runtime, [Git for Windows](https://git-scm.com/download/win), .NET 10 SDK to build.

## Build and run

```powershell
dotnet run --project src/RepoBoard.App
```

```powershell
dotnet test --project tests/RepoBoard.Core.Tests
```

Options: `--data-dir=<folder>` (own repository list and logs), `--lang=en|de`, `--theme=dark|light`, `--minimized` (start
in the notification area), `--exit` (end the RepoBoard running on that data folder).
The repository list is kept in `%APPDATA%\RepoBoard\config.json`, logs in `%APPDATA%\RepoBoard\logs`.

## Docs

`CLAUDE.md` (how the project is built and worked on), `docs/architecture.md`, `docs/decisions/` (ADRs),
`docs/roadmap.md`, `docs/backlog.md`.

## License

[MIT](LICENSE). The icons come from [Lucide](https://lucide.dev) (ISC, see `src/RepoBoard.UI/wwwroot/lib/lucide/LICENSE.txt`).
