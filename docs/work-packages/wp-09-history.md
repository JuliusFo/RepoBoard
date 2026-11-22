# WP-09 Historie und Commit-Werkzeuge

Branch `wp/09-history`, 2026-11-22, noch kein Release.

## Planung (aus `docs/roadmap.md`)
- Commit-Details: Dateien des Commits, Diff (mit WP-05), Nachricht, Refs.
- Historie als Graph (mehrere Branches), mehr als 15 Commits (nachladen).
- Amend (nur ungepusht, sonst Warnung), Revert, Cherry-Pick.
- Invariante: Amend eines gepushten Commits braucht Force-Push → nicht in diesem Paket.
- Offen war: Graph selbst zeichnen (SVG) oder vorhandene Bibliothek?

## Entscheidungen des Nutzers (2026-11-22)
- **Graph selbst als SVG**, keine Bibliothek.
- **Historie zeigt HEAD**, ein Schalter „Alle Branches“ zeigt alle lokalen und Remote-Branches.
- **Konflikt bei Revert/Cherry-Pick bleibt stehen** für IDE oder Terminal, bis WP-07 Abbrechen und Fortsetzen bringt; kein automatisches `--abort`.
- **Commit-Details in der Zeile** anstelle von Branches und Historie (wie der Diff), Esc/X schließt.
- Den Plan (Core mit Tests, dann UI; Amend nur ungepusht und nur gestagte Änderungen; Revert/Cherry-Pick nur mit sauberem Worktree, nicht für Merge-Commits) hat der Nutzer mit diesen Antworten freigegeben.

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0013:
  - Spuren rücken nicht nach links; eine Spur, die auf denselben ersten Elternteil wartet, wechselt in die linke.
  - Höchstens 8 Spuren gezeichnet, Linienfarbe der rechten Spur.
  - Nachladen in Schritten von 50 bis 1 000.
  - Merge-Commits zeigen ihre Änderungen gegen den ersten Elternteil.
  - Commit-IDs nur als Hex.
  - Kein Cherry-Pick einer Änderung, die HEAD schon hat.
  - Commit-Ansicht schließt nach einem Revert/Cherry-Pick.
- Amend: das Häkchen füllt die Nachricht des letzten Commits vor; abgehakt geht eine unveränderte Nachricht wieder weg. Knöpfe heißen dann „Commit ändern“ und „Ändern und pushen“.
- „Alle Branches“ bleibt bis zum Neustart, die nachgeladene Länge nur, solange die Zeile offen ist.
- Revert und Cherry-Pick ohne Rückfrage: Beide legen nur einen neuen Commit an.

## Gebaut
- **Core:**
  - `CommitInfo` mit `Id`, `ShortId`, `Parents`; `RepoDetails` mit `Graph`, `HasMore`, `HeadOnRemote`.
  - `CommitGraph` (`GraphRow`, `GraphLine`, `GraphLineKind`) und `CommitDetails`/`CommitFile` mit Parser.
  - `GitClient`: `LogAsync(count, all)`, `IsCommitId`, `CommitDetailsAsync`, `CommitDiffAsync` (Größe über `cat-file --batch-check`), `MessageAsync`, `IsOnRemoteAsync`, `IsAppliedAsync`, `AmendAsync`, `RevertAsync`, `CherryPickAsync`.
  - `RepoState.LogLength`/`LogAll`.
  - `BoardService`: `SetLogAsync`, `CommitDetailsAsync`, `CommitDiffAsync`, `HeadMessageAsync`, `CommitAsync(…, amend)`, `RevertAsync`, `CherryPickAsync` mit Prüfung im Tor (`PickAsync`).
  - Texte in `SyncText`.
- **UI:**
  - `HistoryRow` (Zeile mit Graph, `ShouldRender`), `CommitPanel` (Details, Dateien, Diff nur lesend, Revert/Cherry-Pick).
  - `DiffLineView` mit `Pickable`.
  - `RepoDetailsPanel`: Schalter, „50 weitere laden“, Amend-Häkchen, Commit-Ansicht.
  - `UI/State/GraphDrawing`, `UI/State/CommitTools`.
  - Texte in `BoardText`, CSS für Graph, Commit-Ansicht und Häkchen.
- **Doku:** ADR 0013, README, CHANGELOG, Architektur, Regel `git.md`, Backlog.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test` (408 Tests). Neu sind `CommitGraphTests`, `HistoryTests` und `CommitToolsTests`.
  - `HistoryTests` deckt ab: Log mit „mehr“ und allen Branches, Datei `HEAD`, Details mit allen Dateiarten und Umbenennung, Diff gegen den Elternteil, Root- und Merge-Commit, Größengrenze, Nachladen über das Board.
  - Amend ungepusht und gepusht.
  - Revert, Cherry-Pick, Cherry-Pick derselben Änderung, sechs Ablehnungsfälle, neue Dateien erlaubt, Konflikt bleibt stehen.
- E2E (2026-11-22, Testinstanz mit `--data-dir` im Scratchpad, Test-Repo dort mit Bare-Remote, Merge und zwei Feature-Branches; `Test-InjectedInput.ps1`: Eingaben kommen durch). Alle Klicks mit echter Maus (`Invoke-RealClick.ps1`):
  - Zeile öffnen: Der Graph zeigt den Merge als Ring und die Feature-Linie grün.
  - „Alle Branches“: `feature/theme` erscheint in eigener Spur.
  - Commit „feat: remember me“: Nachricht mit Rumpf, Autor, Eltern-Link, Refs, Datei und Diff. „Revert“ legt `Revert "feat: remember me"` an.
  - Cherry-Pick von `feat: dark theme` legt einen neuen Commit oben an, die Ansicht schließt sich. Danach ist der Knopf am Original gesperrt („hat diese Änderung schon“).
  - Amend-Häkchen: Die Nachricht ist vorgefüllt, Text per CDP angehängt, „Commit ändern“ ersetzt den Commit; die Aktivität meldet es.
  - Nach dem Push ist das Häkchen gesperrt und nennt den Grund.
  - 30 Commits per CLI angelegt: Der Dateiwächter bringt sie, „50 weitere laden“ zeigt alle 45, danach ist der Knopf weg.
  - Merge-Commit: Hinweis „gegen den ersten Elternteil“, Revert gesperrt. Ein Klick auf einen Eltern-Link öffnet diesen Commit.
  - Bilder dunkel. Log ohne `[ERR]`/`[WRN]`.
- Im E2E gefunden und behoben:
  - Ein zweiter Cherry-Pick derselben Änderung hätte `CHERRY_PICK_HEAD` hinterlassen (git 2.41 beim Nutzer) → `IsAppliedAsync`, Test `A_change_picked_before_is_not_picked_again`.
  - Die Linie eines Feature-Tips in die Hauptspur hatte die Farbe der Hauptspur → Farbe der rechten Spur.
  - Nach dem Revert blieb die Ansicht mit aktivem Knopf offen → sie schließt sich nach Erfolg.
- Im Test gefunden: Ein Cherry-Pick auf den eigenen Elternteil in derselben Sekunde ergibt denselben Commit (gleicher Hash). Der Test lässt `main` vorher weiterlaufen.
- Nicht geprüft: helles Theme im Bild, sehr große Repos (Zeit von `--topo-order`), mehr als acht Spuren.

## Nachtrag: Code-Review (2026-11-22)
Getrennter Reviewer-Agent über `add5681..52d1853`. Entscheidung des Nutzers: „Alle beheben, bei 2 nur mit Meldung“.

1. S – Revert und Cherry-Pick überschrieben ignorierte Dateien ohne Rückfrage (`.env` nach „stop tracking“, git 2.41 nachgestellt) → behoben: `BoardService.IgnoredInTheWayAsync` mit `GitClient.IgnoredAmongAsync` (`ls-files --others --ignored`, Pfade in Teilen unter der Längengrenze der Kommandozeile), Meldung `Pick_Ignored`. Test `An_ignored_file_at_a_path_of_the_commit_keeps_revert_and_cherry_pick_out` (Revert und Cherry-Pick), dazu `Git_itself_keeps_a_new_file_that_a_cherry_pick_would_bring`.
2. M – Leerer Cherry-Pick nach Squash mehrerer Commits blieb stehen, die Meldung nannte `--continue` → behoben nur mit Meldung (Entscheidung des Nutzers): `CherryPick_Empty` nennt `--skip`, kein automatisches `--skip` (Backlog). Test `A_cherry_pick_that_comes_out_empty_after_a_squash_says_so`.
3. M – Amend ersetzte den HEAD beim Klick, nicht den, dessen Nachricht vorgefüllt war → behoben: `expectedHead` im Tor; die UI löst das Häkchen bei neuem HEAD. Test `Amend_keeps_out_when_another_commit_came_since_its_message_was_filled_in`.
4. K – Amend prüfte laufendes Merge/Rebase nur in der UI → behoben im Tor (`AmendBlockerAsync`). Test `Amend_keeps_out_while_a_rebase_is_stopped`.
5. K (Verdacht) – `git cherry` hielt die Commit-Details auf → behoben ohne Messung: „schon übernommen“ wird nach den Details gelesen (`BoardService.IsAppliedAsync`).
6. K (Verdacht) – `for-each-ref --contains` je Status → durch `rev-list -n1 HEAD --not --remotes` ersetzt (gleiche Bedeutung, ein Durchlauf; Amend-Tests decken beide Richtungen).
7. K – `IsCommitId` nahm kurze IDs an (ein Branch gleichen Namens gewönne) → nur 40 oder 64 Zeichen, Theorie angepasst.
8. K – Spätes Vorfüllen nach dem Abhaken → behoben (seit dem zweiten Durchgang in `AmendDraft.Fill`, getestet).
9. K – Betreff ohne Tooltip → `title` wieder am Betreff.
10. K – README „scroll back“ → „Load 50 more“.
- Die neuen Core-Tests scheiterten, als die Fixes probehalber ausgebaut waren.
- Vorher schon da, in den Backlog: ignorierte Dateien bei Branchwechsel und Auto-Pull; 30-s-Zeitlimit bei signierten Commits.

Zweiter Durchgang (`52d1853..b64e63d`; Nutzer: „Ja, alle drei“):
11. M (N1) – Rest von 1: eine ignorierte *Datei* an der Stelle eines Ordners, den der Commit braucht (`foo` für `foo/bar`), ging verloren → behoben: Eltern-Ordner, an denen eine Datei liegt, gehen mit an `ls-files` und zählen nur bei genauem Treffer (ein ignorierter Ordner oberhalb sperrt nichts). Tests `An_ignored_file_where_the_commit_needs_a_folder_keeps_the_cherry_pick_out` (scheiterte ohne Fix) und `An_ignored_folder_above_the_commits_paths_is_no_obstacle`.
12. K (N2) – Das Häkchen löste sich bei neuem HEAD ohne Hinweis → Hinweis `Amend_HeadMoved` daneben bis zum nächsten Ankreuzen oder Commit; das eigene Amend löst ihn nicht aus.
13. K (N4) – Logik des Häkchens ungetestet in der Komponente → `UI/State/AmendDraft` mit `AmendDraftTests`.
14. K (N3) – Doku nachgezogen (Regel zu Commit-IDs, Protokoll, CHANGELOG, ADR).
- E2E nach dem zweiten Durchgang (echte Maus): Häkchen gesetzt, Nachricht vorgefüllt; Commit per CLI („andere Session“) → Häkchen aus, Hinweis sichtbar, unveränderte Nachricht weg; erneut angekreuzt und „Commit ändern“ → Amend im Reflog, kein Hinweis. Log ohne Warnungen.
