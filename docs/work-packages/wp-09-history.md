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
