# Arbeitspakete – Index

Was in den abgeschlossenen Paketen gebaut und entschieden wurde, mit Nachträgen, Fallen und E2E-Erkenntnissen: je Paket eine Datei. Vor Änderungen an einem Bereich das Paket lesen, das ihn gebaut hat – die Spalte „Bereiche“ führt vom Code zum Paket. Geplante Pakete: `docs/roadmap.md`; übergreifende Fallen: `.claude/rules/`, `docs/e2e-testing.md`. Begründungen: `docs/decisions/`.

## Versionen

| Version | Inhalt | Pakete |
|---|---|---|
| **0.x – Fundament** | Übersicht aller Repos, Git-Grundaktionen, Auto-Fetch/Auto-Pull je Repo | WP-01 … |

## Übersicht

Reihenfolge wie die Releases.

| Paket | Datei | Inhalt | Bereiche | Release | ADR |
|---|---|---|---|---|---|
| WP-01 | [wp-01-foundation.md](wp-01-foundation.md) | Projekt nach dem Muster von FerretSharp, Spike übernommen: Übersicht, Fetch/Pull/Push, Stage/Commit, Branches, Auto-Fetch/Auto-Pull, Aktivität | alles (`Core/Git`, `Core/Board`, `Core/Sync`, `Core/Repos`, `UI`, `App`) | 0.1.0 | 0001–0005 |
| WP-02 | [wp-02-tray.md](wp-02-tray.md) | Infobereich (Schließen versteckt, Menü), Benachrichtigungen bei Auto-Sync, Autostart, Pause, Einstellungsmenü, Programmsymbol, `--minimized`/`--exit` | `App/Services` (`TrayIcon`, `RegistryAutostart`, `InstanceSignal`), `Core/Sync/SyncNotifier`, `Core/Autostart`, `Repos/BoardConfig` (`AppSettings`), `UI/Components/Board/SettingsMenu`, `UI/State` (`BoardNavigation`, `TrayStatus`) | 0.1.0 | 0006 |
| WP-10 | [wp-10-installer.md](wp-10-installer.md) | Installer und Updates mit Velopack: Setup und Portable, Release-Workflow, automatische Prüfung (Start und Intervall), Hinweis in der Kopfzeile, Dialog mit Notizen, Benachrichtigung je Version, Neustart in die neue Version | `App/Program`, `App/Services/VelopackUpdater`, `UI/State` (`UpdateState`, `ReleaseNotes`, `AppExit`), `UI/Components/Update`, `Repos/BoardConfig` (`AppSettings`), `.github/workflows/release.yml` | 0.1.0 | 0007 |
| WP-05 | [wp-05-diff.md](wp-05-diff.md) | Diff-Ansicht: Klick auf eine geänderte Datei zeigt `git diff` (gestaged/ungestaged) inline in der geöffneten Zeile, Wort-Markierung, folgt der Datei beim Stagen | `Core/Git` (`FileDiff`, `DiffWords`, `GitClient.DiffAsync`), `Core/Board/BoardService.DiffAsync`, `UI/Components/Board` (`DiffPanel`, `RepoDetailsPanel`), `UI/State/DiffSelection` | 0.2.0 | 0008 |
| WP-04 | [wp-04-worktrees.md](wp-04-worktrees.md) | Worktrees als Unterzeilen ihres Repos (eingeklappt mit Zähler, „Claude“-Badge), dieselben Aktionen, Entfernen und Aufräumen (gemergt, gone, detached, Ordner fehlt) ohne `--force` mit Rückfrage und Liste der ignorierten Dateien; „Upstream gelöscht“ in der Sync-Spalte | `Core/Git` (`WorktreeInfo`, `GitClient` Worktree-Befehle, `RepoStatus.UpstreamGone`), `Core/Board` (`BoardService` Worktrees, `RepoState.Worktrees`, `WorktreeCleanup`), `Core/Repos/RepoScanner`, `UI/Components/Board` (`RepoRow`, `WorktreeDialog`), `UI/Shell`, `UI/State/BoardFilter` | 0.2.0 | 0010 |
| Fix | [fix-webview-copy-fence.md](fix-webview-copy-fence.md) | Klicks und Hover verspätet: auf die GPU-Kopie jedes WebView-Bildes warten, Bilder bei gesperrtem `D3DImage` zurückhalten (aus FerretSharp), Messskript für das Fensterbild | `App/Services` (`CopyFence`, `WebViewCapture`), `tests/RepoBoard.App.Tests`, `tools/e2e/Measure-PictureDelay.ps1` | 0.2.0 | 0009 |
| WP-03 | [wp-03-file-watch.md](wp-03-file-watch.md) | Dateiwächter: Status etwa eine Sekunde nach jeder Änderung in IDE oder Terminal (Commit, Branchwechsel, Fetch, Datei, neuer Worktree), ein Wächter je Wurzelordner, Routing nach Pfad, Entprellen; der Takt liest beobachtete Repos nur alle 2 min | `Core/Watch` (`WatchMap`, `ChangeDebouncer`, `RepoWatchService`), `Core/Board` (`BoardService.RefreshStaleAsync`, `RepoState.StatusReadAt`/`Watched`), `Core/Sync/AutoSyncService`, `App` | 0.3.0 | 0011 |
| WP-25 | [wp-25-hunk-staging.md](wp-25-hunk-staging.md) | Hunks und Zeilen stagen/unstagen aus der Diff-Ansicht: Knopf am Hunk-Kopf, Zeilen per Klick am Rand (Shift: Bereich), auch Teile neuer und gelöschter Dateien; nur der Index über `git apply --cached` | `Core/Git` (`HunkPatch`, `GitClient.ApplyToIndexAsync`, `GitRunner` stdin, `FileDiff` Dateimodus), `Core/Board/BoardService.StageHunkAsync`, `UI/Components/Board` (`DiffPanel`, `DiffLineView`), `UI/State/LineSelection` | 0.4.0 | 0012 |
| WP-09 | [wp-09-history.md](wp-09-history.md) | Historie als Graph (HEAD oder alle Branches, nachladen bis 1 000), Commit-Details mit Dateien und Diff in der Zeile, Amend (nur ungepusht), Revert und Cherry-Pick mit Prüfung im Tor | `Core/Git` (`CommitGraph`, `CommitDetails`, `GitClient` Log/Show/Amend/Revert/Cherry-Pick), `Core/Board` (`BoardService.SetLogAsync`/`CommitDetailsAsync`/`PickAsync`, `RepoState.LogLength`/`LogAll`), `UI/Components/Board` (`HistoryRow`, `CommitPanel`, `RepoDetailsPanel`), `UI/State` (`GraphDrawing`, `CommitTools`) | 0.4.0 | 0013 |
