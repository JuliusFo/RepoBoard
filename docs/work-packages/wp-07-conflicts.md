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
- Einzelheiten nach ADR 0015:
  - Schlüssel je Operation und Schritt, Dateiliste der Rückfrage im Tor geprüft.
  - `GIT_EDITOR=:` für jeden Aufruf; 3 min für Merge, Rebase, Fortsetzen, Überspringen und Abbrechen.
  - Mergen und Rebasen ohne Rückfrage, nur mit sauberem Worktree.
- Commit-Box gesperrt, solange eine Operation läuft; „Alle stagen“ lässt Konfliktdateien aus, „Alle unstagen“ ist während einer Operation gesperrt.
- „Mergen“ und „Rebasen“ erscheinen nur beim Überfahren eines Branches (sonst wäre die Liste voller Knöpfe); „Wechseln“ bleibt sichtbar.
- Die Art eines Konflikts (beide geändert, von uns gelöscht …) steht nur im Tooltip.

## Gebaut
- **Core:**
  - `OperationInfo` (liest Marker, `MERGE_MSG`, `rebase-merge`/`rebase-apply`, `REBASE_HEAD`), `RepoState.Unfinished` (`Operation` ist nur noch die Art).
  - `GitClient`: `RefOf`, `MergeAsync`, `RebaseAsync`, `ChangedPathsAsync`, `CountPushedToRewriteAsync`, `ContinueAsync`, `SkipAsync`, `AbortAsync`, `HasConflictMarkersAsync`.
  - `GitRunner`: `GIT_EDITOR=:`, `CommitTimeout`.
  - `BoardService`: `MergeAsync`, `RebaseAsync` (`StartAsync` mit den Prüfungen), `ContinueAsync`, `AbortAsync`, `SkipAsync` (`DiscardAsync`), `DiscardedFiles`, `MarkResolvedAsync`, `HasConflictMarkersAsync`; `IgnoredInTheWayAsync` nimmt jetzt Pfade.
  - Texte in `SyncText`; `Revert_Stopped`/`CherryPick_Stopped` durch `Operation_Stopped` ersetzt.
- **UI:**
  - `RepoDetailsPanel`: Leiste der Operation, Gruppe „Konflikte“, Rückfragen, „Mergen“/„Rebasen“ in der Branch-Liste, Commit-Sperre.
  - `ConfirmDialog` (Rückfrage mit Dateiliste, Esc bricht ab).
  - `UI/State/OperationTools` (Titel, Sperren, Art des Konflikts).
  - `IDesktop.OpenInEditor(path, file)`: VS Code mit Repo und Datei; die Datei muss im Repo liegen.
  - Texte in `BoardText`, CSS für Leiste, Konflikte und Branch-Knöpfe.
- **Doku:** ADR 0015 (ADR 0008 und 0013 ergänzt), `CLAUDE.md` §2, README, CHANGELOG, Architektur, Regel `git.md`, Backlog, Roadmap.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test` (444 Tests, nach dem Code-Review 452). Neu sind `OperationTests` (Core) und `OperationToolsTests` (UI).
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

## Nachtrag: Code-Review (2026-11-28)

Separater Reviewer über `3318da0..6606b9f`, zweiter Durchgang über die Korrekturen. Entscheidung des Nutzers: „1–12 beheben“ (Punkt 13 und „vorher schon da“ in den Backlog), danach „N1, N2, N4–N7 beheben“ (N3 nur in der ADR).

1. S – Abbrechen/Überspringen löschte eine neue Datei, die der Nutzer während der Operation gestaged hatte („Alle stagen“), obwohl die Rückfrage „neue Dateien bleiben“ sagte → behoben: `KeepNewFilesAsync` macht eigene gestagte neue Dateien vorher untracked (`rm --cached`); die der Operation (`AddedByAsync`) gehen mit. Test `Abort_keeps_a_new_file_staged_meanwhile_but_not_the_operations_own`.
2. M – Nach dem Lösen aller Konflikte machte „Alle unstagen“ alles ungestaged, „Fortsetzen“ committete dann einen Merge ohne die andere Seite → behoben: „Alle unstagen“ ist gesperrt, solange eine Operation läuft (`OperationTools.UnstageAllPaths`, Test). Fortsetzen bei ungestagten Änderungen bleibt offen (Merge aus dem Terminal mit unbeteiligten Änderungen).
3. M – Text der Rückfrage beim Rebase falsch („git behält eigene Änderungen“; `rebase --abort` ist `reset --hard`) → behoben: eigener Text `Operation_AbortTextRebase`, ADR korrigiert.
4. M – Der Schlüssel eines Rebase enthielt HEAD nicht: ein Commit im Terminal während der offenen Rückfrage wäre verworfen worden → behoben: HEAD in jedem Schlüssel. Test `Abort_of_a_rebase_keeps_out_once_a_commit_was_made_meanwhile` (scheitert ohne).
5. K – Fortsetzen einer leeren Lösung riet erneut zu „Fortsetzen“ → behoben: `Continue_Empty` verweist auf „Überspringen“. Test `Continuing_an_empty_resolution_points_to_skip`.
6. K – Konnte git nicht auf Marker prüfen, stagte „Gelöst“ ohne Rückfrage → behoben: Rückfrage mit eigenem Text. Der optionale Teil (`whitespace`-Attribut) nicht umgesetzt.
7. K – `--abort` mit 30 s → behoben: 3 min.
8. K – „Öffnen“ scheiterte bei einem Repo in einer Laufwerkswurzel → behoben: `UI/State/EditorPath` über `GetRelativePath`, Test.
9. K – Ersatzschlüssel bei Lesefehler war fest → behoben: Guid.
10. K – Ein gleichnamiger Tag auf demselben Commit machte „Merge tag …“ → behoben: dann der volle Ref. Test mit annotiertem Tag.
11. K – „Alle stagen“ mit vielen Pfaden sprengte die Kommandozeile → behoben: Stagen, Unstagen und Untrack in Teilen (`InBatchesAsync`).
12. K – `GIT_EDITOR=true` braucht ein Programm `true` → behoben: `GIT_EDITOR=:`.
13. K – Laufendes `git am` erscheint als Rebase → nicht behoben (Nutzer: Backlog).
- Vorher schon da: `CommitAsync` prüft im Tor nicht auf eine laufende Operation → Backlog.

Zweiter Durchgang (alle alten Punkte abgedeckt; neu):
- N1 M – Eine nach dem Stagen bearbeitete neue Datei sperrte Abbrechen ganz (`rm --cached` ohne `-f`) → behoben: `-f`. Test `Abort_names_an_adapted_file_of_the_merge_and_keeps_a_new_file_edited_after_staging` (scheitert ohne).
- N2 M – Durch die Korrektur zu 1 fehlte eine angepasste neue Datei der Operation in der Rückfrage und wurde trotzdem gelöscht → behoben: `DiscardedFilesAsync` vergleicht sie mit dem hereinkommenden Commit, Rückfrage und Tor nutzen dieselbe Methode. Gleicher Test (scheitert ohne).
- N3 K – In seltenen Fällen bleibt nach dem Abbrechen eine Datei der Operation als untracked liegen → nicht behoben, in ADR 0015 festgehalten (Nutzer).
- N4 K – „Überspringen“-Hinweis auch, wenn nur die Lösung wieder ungestaged war → behoben, Fall im Test `Continuing_an_empty_resolution_points_to_skip`.
- N5 K – Ein Tag auf einen Tree/Blob blieb unerkannt → behoben: `RefExistsAsync` (`show-ref --verify`).
- N6 K – Gesperrtes „Alle unstagen“ ohne Tooltip → behoben (Satz `Pick_Operation`).
- N7 K (Verdacht) – `ESC[K` in Meldungen bei gesetztem `TERM` → behoben: `GitResult.Message` entfernt ANSI-Steuerfolgen, Test in `GitSecretsTests`.
