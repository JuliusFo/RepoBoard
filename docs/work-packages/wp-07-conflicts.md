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
- E2E (2026-11-28, Testinstanz mit `--data-dir` im Scratchpad, zwei Test-Repos dort: `proj` mit Bare-Remote und den Branches `feature`, `docs`, `hotfix`, `rebase-demo` mit `topic` aus zwei Commits; `Test-InjectedInput.ps1`: Eingaben kommen durch). Alle Klicks mit echter Maus (`Invoke-RealClick.ps1`):
  - Branch `feature` überfahren: „Mergen“ und „Rebasen“ erscheinen mit Tooltip („feature in main mergen“); ein Klick auf „Mergen“ hält am Konflikt. Die Leiste zeigt „Merge läuft: Merge branch 'feature'“ und „noch 1 Konflikt“, „Fortsetzen“ ist gesperrt mit Grund, „Alle stagen“/„Alle unstagen“ sind gesperrt, die Commit-Box zeigt den Hinweis.
  - „Gelöst“ mit Markern: Die Rückfrage „Noch Konfliktmarker“ erscheint als Dialog über der Seite; „Abbrechen“ schließt sie. Datei per CLI gelöst, dann „Gelöst“ ohne Rückfrage; „Fortsetzen“ legt „Merge branch 'feature'“ an (mit der Git-Konfiguration des Nutzers, kein Editor wartet).
  - `rebase-demo`: „Rebasen“ auf `main` hält bei „Rebase von topic läuft – Commit 1 von 2: topic: first“ (die Zeile zeigt „detached“ und „Rebase läuft“). Gelöst, „Fortsetzen“ hält bei Commit 2 von 2. „Überspringen“ zeigt die Rückfrage mit `notes.txt`; bestätigt endet der Rebase, `topic` steht mit dem ersten Commit auf `main`.
  - Merge von `docs` ohne Konflikt mit einer neuen Datei im Worktree: „Merge branch 'docs'“.
  - Merge von `hotfix` mit Konflikt, „Abbrechen“: Rückfrage „Merge abbrechen?“ mit `app.txt`. Während sie offen war, `README.md` per CLI geändert und bestätigt: Das Tor lehnt ab („seit der Rückfrage haben sich Dateien geändert“), der Merge läuft weiter. Neue Rückfrage mit `README.md` und `app.txt` bestätigt: Der Merge ist weg, `app.txt` auf dem Stand davor, die neue Datei und die Änderung an `README.md` bleiben.
  - Log beider Läufe: 0 Warnungen und Fehler.
- Aus dem E2E behoben:
  - Der Schließen-Knopf der Rückfragen zu Abbrechen und Überspringen hieß „Abbrechen“ neben „Merge abbrechen“ – jetzt „Zurück“.
  - Die Rückfrage sagte, jede gelistete Datei gehe zurück; git behält aber eine eigene Änderung an einer Datei, die der Merge nicht berührt (`README.md` oben). Jetzt „können zurückgehen“.
  - gits Fortschrittszeile „Rebasing (1/2)“ endet mit `\r`, die Fehlermeldung stand dahinter in derselben Zeile („Rebasing (1/2)error: …“). `GitResult.Message` zeigt jetzt wie ein Terminal nur den Teil nach dem letzten `\r` (Test in `GitSecretsTests`).
- Nicht geprüft: „Öffnen“ startet VS Code (es hätte ein Fenster auf dem Rechner des Nutzers geöffnet); Esc in der Rückfrage.
