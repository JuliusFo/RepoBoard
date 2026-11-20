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
- **Branches:** switch to a local or remote branch, create a new one; the last 15 commits with their refs.
- **Auto-fetch and auto-pull** per repository, at an interval of 1 to 60 minutes. Auto-pull only fast-forwards a branch
  that has only fallen behind – never a merge, never over your local commits, never during a merge or rebase – and says
  why when it keeps out. Background syncs never ask for credentials.
- **Activity log** of what happened while you were not looking.
- **Repositories:** add one, or scan a folder (three levels deep); open it in the Explorer, a terminal or VS Code.
- English and German, light and dark.

## Requirements

- Windows 10/11 with the WebView2 runtime, [Git for Windows](https://git-scm.com/download/win), .NET 10 SDK to build.

## Build and run

```powershell
dotnet run --project src/RepoBoard.App
```

```powershell
dotnet test --project tests/RepoBoard.Core.Tests
```

Options: `--data-dir=<folder>` (own repository list and logs), `--lang=en|de`, `--theme=dark|light`.
The repository list is kept in `%APPDATA%\RepoBoard\config.json`, logs in `%APPDATA%\RepoBoard\logs`.

## Docs

`CLAUDE.md` (how the project is built and worked on), `docs/architecture.md`, `docs/decisions/` (ADRs),
`docs/roadmap.md`, `docs/backlog.md`.

## License

[MIT](LICENSE). The icons come from [Lucide](https://lucide.dev) (ISC, see `src/RepoBoard.UI/wwwroot/lib/lucide/LICENSE.txt`).
