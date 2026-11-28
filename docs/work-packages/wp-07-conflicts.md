# WP-07 Merge und Rebase zu Ende bringen

Branch `wp/07-conflicts`, 2026-11-28, Release 0.5.0.

## Planung (aus `docs/roadmap.md`)
- Laufendes Merge/Rebase/Cherry-Pick/Revert mit Konfliktdateien zeigen; Abbrechen (`--abort`) und Fortsetzen (`--continue`); Datei als gelöst markieren.
- Konfliktdatei im Mergetool (`git mergetool`) oder in VS Code öffnen.
- Merge bzw. Rebase eines Branches selbst starten (bisher nur Fast-Forward).
- Invariante: `--abort` verwirft den Stand der Operation – Rückfrage.
- Offen war: Merge-Commit oder Rebase als Standard beim selbst gestarteten Zusammenführen?

## Entscheidungen des Nutzers (2026-11-28)
Der Nutzer hat den Plan mit diesen Empfehlungen freigegeben („Ja, umsetzen“):
- **Kein Standard:** „Mergen“ und „Rebasen“ sind getrennte Aktionen am Branch.
- **Mergetool nicht in WP-07**, nur „Im Editor öffnen“ (VS Code mit Merge-Editor); `git mergetool` in den Backlog.
- **Überspringen (`--skip`)** für Rebase, Cherry-Pick und Revert, mit Rückfrage; löst auch den leeren Cherry-Pick nach einem Squash.
- **Nachricht beim Fortsetzen:** die von git vorbereitete, unverändert; Bearbeiten in den Backlog.
- **Rebase gepushter Commits gesperrt** (kein Force-Push).
- **„Unsere/ihre Version“ nicht in WP-07** (Backlog).
- **„Als gelöst markieren“** fragt nach, wenn git noch Konfliktmarker findet.

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0014:
  - Schlüssel je Operation und Schritt, Dateiliste der Rückfrage im Tor geprüft.
  - `GIT_EDITOR=true` für jeden Aufruf; 3 min für Merge, Rebase, Fortsetzen und Überspringen.
  - Mergen und Rebasen ohne Rückfrage, nur mit sauberem Worktree.
- Commit-Box gesperrt, solange eine Operation läuft; „Alle stagen“ und „Alle unstagen“ lassen Konfliktdateien aus.
- „Mergen“ und „Rebasen“ erscheinen nur beim Überfahren eines Branches (sonst wäre die Liste voller Knöpfe); „Wechseln“ bleibt sichtbar.
- Die Art eines Konflikts (beide geändert, von uns gelöscht …) steht nur im Tooltip.

## Gebaut
- **Core:**
  - `OperationInfo` (liest Marker, `MERGE_MSG`, `rebase-merge`/`rebase-apply`, `REBASE_HEAD`), `RepoState.Unfinished` (`Operation` ist nur noch die Art).
  - `GitClient`: `RefOf`, `MergeAsync`, `RebaseAsync`, `ChangedPathsAsync`, `CountPushedToRewriteAsync`, `ContinueAsync`, `SkipAsync`, `AbortAsync`, `HasConflictMarkersAsync`.
  - `GitRunner`: `GIT_EDITOR=true`, `CommitTimeout`.
  - `BoardService`: `MergeAsync`, `RebaseAsync` (`StartAsync` mit den Prüfungen), `ContinueAsync`, `AbortAsync`, `SkipAsync` (`DiscardAsync`), `DiscardedFiles`, `MarkResolvedAsync`, `HasConflictMarkersAsync`; `IgnoredInTheWayAsync` nimmt jetzt Pfade.
  - Texte in `SyncText`; `Revert_Stopped`/`CherryPick_Stopped` durch `Operation_Stopped` ersetzt.
- **UI:**
  - `RepoDetailsPanel`: Leiste der Operation, Gruppe „Konflikte“, Rückfragen, „Mergen“/„Rebasen“ in der Branch-Liste, Commit-Sperre.
  - `ConfirmDialog` (Rückfrage mit Dateiliste, Esc bricht ab).
  - `UI/State/OperationTools` (Titel, Sperren, Art des Konflikts).
  - `IDesktop.OpenInEditor(path, file)`: VS Code mit Repo und Datei; die Datei muss im Repo liegen.
  - Texte in `BoardText`, CSS für Leiste, Konflikte und Branch-Knöpfe.
- **Doku:** ADR 0014 (ADR 0008 und 0013 ergänzt), `CLAUDE.md` §2, README, CHANGELOG, Architektur, Regel `git.md`, Backlog, Roadmap.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test` (444 Tests). Neu sind `OperationTests` (Core) und `OperationToolsTests` (UI).
  - `OperationTests` deckt ab: Merge mit Konflikt bis Fortsetzen (Nachricht „Merge branch 'feature'“, kein Editor, Marker vor und nach dem Lösen, Leerzeichen zählen nicht); Rebase in zwei Schritten mit Sperre bei ungestagter Änderung, Überspringen mit veraltetem Schlüssel, Überspringen; Abbrechen nur mit passendem Schlüssel und passender Dateiliste, neue Datei bleibt; nichts mehr zu tun, wenn das Terminal fertig war; Merge gesperrt bei Änderung, gestagter Datei, detached HEAD und laufender Operation; ignorierte Datei im Weg; Rebase gepushter Commits gesperrt, Merge eines Remote-Branches erlaubt; Markerdateien von Cherry-Pick und Revert.
  - `HistoryTests`: angehaltener Cherry-Pick nennt den Commit, leerer Cherry-Pick endet mit Überspringen.
