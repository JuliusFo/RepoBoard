# RepoBoard

All your Git repositories on one page: which branch each one is on, how far it is ahead of or behind its upstream, what
changed locally – and auto-fetch and auto-pull per repository, so the ones you choose are always up to date.

Built with .NET 10, a WPF host and a Blazor UI (the same stack as FerretSharp). Git runs through your installed `git`, so
sign-in, SSH, hooks and LFS work exactly as in your terminal.

## Features

- **Overview:** every repository with its branch (feature branches stand out, a detached HEAD is marked), commits ahead
  and behind, staged, changed, new and conflicted files, the time of the last fetch and an unfinished merge or rebase.
- **Always current:** a commit, branch switch, fetch or saved file in your IDE or terminal shows within about a second –
  RepoBoard watches the repository folders (build output like `bin`, `obj` and `node_modules` aside) and reads the
  status only when something changed, with a fallback read every two minutes. A new worktree of a Claude Code session
  shows up the same way.
- **Filter and search:** chips with counts – changes, behind, ahead, auto-sync, problems – and a search over name, path and
  branch.
- **Git actions per repository:** fetch, pull (fast-forward only), push (the first push sets the upstream), fetch all.
- **Commit:** open a row to stage or unstage files, commit the staged files or everything, commit and push. “Amend last
  commit” replaces the last commit with the staged changes and a new message – only while it is not pushed.
- **Diff:** click a changed file to see its staged or unstaged changes right next to the list – git’s own diff, with line
  numbers and the changed words highlighted; line endings and binary files show as git sees them.
- **Stage hunks and lines:** “Stage hunk” at each hunk of the diff (“Unstage hunk” on the staged side); click lines at
  the left edge (Shift+click for a range) to stage or unstage only them – also part of a new file. Only the index
  changes, never your files.
- **Worktrees:** a repository’s linked worktrees (`git worktree`) as rows below it, behind a count you click to show
  them – with branch, ahead/behind and changes, and the same actions; worktrees made by Claude Code sessions are marked.
  Remove one, or clean up those that look done (branch merged, upstream deleted, folder gone): RepoBoard asks first,
  lists the ignored files git deletes with it, and never removes a worktree with changes. The branch always stays.
- **Branches:** switch to a local branch, or to a remote one as a new local branch that tracks exactly it; create a new
  one. The list shows how far each branch is ahead of or behind its upstream, and when it last changed. Hover a branch to
  merge it into the current one or rebase the current one onto it – only with no staged or changed files, and a rebase
  only of commits not pushed yet.
- **Finish a merge or rebase:** an unfinished merge, rebase, cherry-pick or revert – started in RepoBoard, your IDE or
  your terminal – shows above the files of the opened row with where it stands (“commit 2 of 5”). Conflicted files have
  their own group: open one in VS Code (with its merge editor), then mark it resolved – RepoBoard asks if git still finds
  conflict markers. “Continue” commits with git’s message and goes on; “Skip” leaves out the commit it stopped at;
  “Abort” goes back to the state before. Skip and abort ask first and list every changed file that may go back.
- **History:** the commits of the current branch – or of all branches – with a graph of their branches and merges, and
  their refs; “Load 50 more” goes further back. Click a commit for its whole message, author, parents, the files it changed
  and their diff. Revert a commit of the branch (a new commit that undoes it) or cherry-pick one of another branch onto
  it – only with no staged or changed files; if git stops at a conflict, finish it right there (see above).
- **Auto-fetch and auto-pull**, switched on per repository; one interval of 1 to 60 minutes for all. Auto-pull only fast-forwards a branch
  that has only fallen behind – never a merge, never over your local commits, never during a merge or rebase, and never with
  git’s autostash, which would put your changes back with conflict markers – and says why when it keeps out. Background
  syncs never ask for credentials, and the same failure is reported once.
- **Activity log** of what happened while you were not looking.
- **Notification area:** closing the window keeps RepoBoard and auto-sync running next to the clock; the icon’s menu
  opens it, fetches all, pauses auto-sync (until the next start) and exits. Windows notifications for auto-pulls and
  problems, gathered per run – a click opens the repository. Optionally start with Windows, in the notification area
  only. All in the settings menu at the top right.
- **Updates:** RepoBoard looks for a newer version at start and while it runs and tells you – in the top bar and once as
  a Windows notification – with what is new; downloading and restarting wait for your click. Switch it off or check now
  in the settings.
- **Repositories:** add one, or scan a folder (three levels deep) and tick which to add; open one in the Explorer, a
  terminal or VS Code; remove one from the board (its folder stays). A worktree you add or scan shows below its
  repository.
- **Trust:** adding a repository means trusting its configuration – RepoBoard runs git there, with that repository’s
  hooks and settings, as your terminal would. A scan reads what each one would run (an fsmonitor, hooks, an ssh command,
  included configuration) before git runs anywhere, and leaves those unticked.
- English and German, light and dark.

## Install

Download from the [releases](https://github.com/JuliusFo/RepoBoard/releases) (Windows 10/11, x64):

- `RepoBoard-win-Setup.exe` installs RepoBoard for your user (no admin rights) to `%LocalAppData%\RepoBoard`.
- `RepoBoard-win-Portable.zip` runs from any folder without installing.

Both update themselves. The installer is not signed: Windows SmartScreen warns once (More info › Run anyway).

## Requirements

- Windows 10/11 with the WebView2 runtime, [Git for Windows](https://git-scm.com/download/win) 2.27 or newer, .NET 10 SDK to build.

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

[`CLAUDE.md`](CLAUDE.md) (how the project is built and worked on), [`CHANGELOG.md`](CHANGELOG.md),
[`docs/architecture.md`](docs/architecture.md), [`docs/decisions/`](docs/decisions) (ADRs),
[`docs/work-packages/`](docs/work-packages/README.md) (what each package built and decided),
[`docs/release.md`](docs/release.md), [`docs/roadmap.md`](docs/roadmap.md), [`docs/backlog.md`](docs/backlog.md).

## License

[MIT](LICENSE). The icons come from [Lucide](https://lucide.dev) (ISC, see `src/RepoBoard.UI/wwwroot/lib/lucide/LICENSE.txt`).
