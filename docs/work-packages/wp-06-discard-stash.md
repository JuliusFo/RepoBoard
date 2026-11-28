# WP-06 Änderungen verwerfen und Stash

Branch `wp/06-discard-stash`, 2026-11-28, Release 0.5.0.

## Planung (aus `docs/roadmap.md`)
- Änderungen einer Datei bzw. aller verwerfen (`restore --worktree`, neue Dateien in den Papierkorb statt `clean`).
- Stash: Liste, anlegen (mit Nachricht, optional mit neuen Dateien), anwenden, pop, löschen.
- Optional je Repo: Auto-Stash um den Auto-Pull (stash → fast-forward → pop; bei Konflikt beim pop bleibt der Stash und es kommt eine Meldung).
- Invariante: neuer ADR (Ausnahme zu `CLAUDE.md` §2), Rückfrage mit Dateiliste, Tests je Fall.
- Offen war: Papierkorb oder echtes Löschen für neue Dateien? Auto-Stash überhaupt (er ändert den Worktree im Hintergrund)?

## Entscheidungen des Nutzers (2026-11-28)
Der Nutzer hat den Plan mit diesen Empfehlungen freigegeben („Ja, so umsetzen“):
- **Verwerfen mit Sicherung:** Geänderte getrackte Dateien gehen per `stash push --keep-index -- <pfade>` zurück. Ihre Änderungen bleiben als Eintrag in der Stash-Liste, „Anwenden“ holt sie zurück. Kein bloßes `restore --worktree`.
- **Neue Dateien in den Papierkorb**, nicht endgültig löschen.
- **Nur nicht gestagte Änderungen** verwerfen; Gestagtes bleibt.
- **Stash** mit Liste, Anlegen (Nachricht, „mit neuen Dateien“), Anwenden, Pop, Löschen mit Rückfrage; Hinweis, dass Worktrees die Liste teilen.
- **Kein Auto-Stash** um den Auto-Pull (Backlog).

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0016:
  - Pfade über `--pathspec-from-file` als `:(literal)`.
  - Einträge über ihren Commit, nicht ihre Position.
  - Pop als Anwenden plus Löschen.
  - Dateien eines Eintrags ohne `stash show --include-untracked`.
- Abweichung vom Plan: **keine Prüfsumme je Datei** im Tor, nur „hat noch eine Änderung, die Verwerfen anbietet“. Ein weiter bearbeiteter Stand geht mit in Stash oder Papierkorb, nichts geht verloren.
- Der Knopf je Datei erscheint nur beim Überfahren und bei Fokus.
- Der Stash-Abschnitt sitzt unter der Commit-Box; ohne Änderungen und Einträge fehlt er.
- **Frage an den Nutzer:** Mit dem Stash-Abschnitt zeigt die Liste der Änderungen nur noch etwa vier Dateien, bevor sie scrollt (Pane höchstens 380 px). Den Abschnitt einklappbar machen oder die Pane höher?

## Gebaut
- **Core:**
  - `GitClient`: `DiscardIntoStashAsync`, `StashAsync`, `StashesAsync`, `StashFilesAsync`, `ApplyStashAsync`, `DropStashAsync`.
  - `StashEntry` (Parser für die NUL-Liste).
  - `StashCheck`: Regeln für Knöpfe und Tor.
  - `IRecycleBin` (`Core/IO`).
  - `BoardService`: `DiscardChangesAsync`, `StashAsync`, `ApplyStashAsync` (auch Pop), `DropStashAsync`, `StashFilesAsync`, `RepoDetails.Stashes`.
  - `WatchMap`: `logs/refs/stash`.
  - Texte in `SyncText`.
- **App:** `ShellRecycleBin` (`SHFileOperation`, `FOF_ALLOWUNDO`, `FOF_WANTNUKEWARNING`, STA-Thread).
- **UI:**
  - `RepoDetailsPanel`: „Alle verwerfen“, Knopf je Datei, Stash-Abschnitt, Rückfragen zu Verwerfen und Löschen.
  - `UI/State/StashTools`, Icon `Undo`, CSS, Texte in `BoardText`.
- **Doku:** ADR 0016, `CLAUDE.md` §2, README, CHANGELOG, Architektur, Regel `git.md`, Backlog, Roadmap.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test` (631 Tests).
  - Neu sind `StashTests` und `StashCheckTests` (Core), `StashToolsTests` (UI) sowie ein Fall in `WatchMapTests`.
  - `StashTests` deckt ab:
    - Verwerfen getrackter und neuer Dateien, mit Rücknahme über „Anwenden“. `[e].txt` trifft dabei nicht `e.txt`; gegengeprüft: ohne `:(literal)` scheitert der Test.
    - Eine gestagte und weiter geänderte Datei geht auf den gestagten Stand zurück.
    - Eine inzwischen gestagte Datei lässt das Tor ablehnen.
    - Ohne Papierkorb wird nichts verworfen, auch nichts in den Stash gelegt.
    - Stash mit neuen Dateien und Pop.
    - „nur neue Dateien“ statt eines leeren Stash.
    - Löschen findet den Eintrag nach seinem Commit, nachdem die Liste verrutscht ist; ein zweites Löschen lehnt ab.
    - Pop mit Konflikt behält den Eintrag; Anwenden ist danach mit Grund gesperrt.
- E2E (2026-11-28, Testinstanz mit `--data-dir` im Scratchpad, Repo `proj` mit Bare-Remote dort; `Test-InjectedInput.ps1`: Eingaben kommen durch). Alle Klicks mit echter Maus (`Invoke-RealClick.ps1`), Text per CDP:
  - Überfahren von `[id].txt` zeigt den Knopf. Die Rückfrage „Änderungen an 1 Datei verwerfen?“ hat den Fokus; bestätigt liegt der Eintrag „On main: Von RepoBoard verworfen: [id].txt“ in der Liste, `i.txt` bleibt geändert.
  - „Alle verwerfen“: Während die Rückfrage offen war, `i.txt` per CLI gestaged, dann bestätigt. Das Tor lehnt ab, nichts geht verloren. Neu gefragt und bestätigt: `app.txt` und das gelöschte `notes.txt` sind im Stash, `new-file.txt` liegt im Windows-Papierkorb (mit Ursprungsordner geprüft).
  - „Anwenden“ auf den älteren, inzwischen zweiten Eintrag holt `[id].txt` zurück, der Eintrag bleibt. „Pop“ auf den neueren holt `app.txt` und `notes.txt` zurück und löscht ihn.
  - Stash „halbfertige Idee“ mit „Mit neuen Dateien“ legt auch `extra.txt` weg. Aufgeklappt zeigt der Eintrag seine fünf Dateien.
  - Löschen des älteren Eintrags fragt mit Dateiliste nach.
  - Pop mit einem per CLI committeten Konflikt in `app.txt`: Die Datei steht unter „Konflikte“, der Eintrag bleibt, „Anwenden“/„Pop“ und „Stashen“ sind mit Grund gesperrt.
  - Log: 0 Warnungen und Fehler.
- Aus dem E2E behoben: Der Tooltip von „Anwenden“/„Pop“ bei offenem Konflikt sprach vom Anlegen eines Stash – jetzt ein eigener Text (`StashApply_Conflicts`).
