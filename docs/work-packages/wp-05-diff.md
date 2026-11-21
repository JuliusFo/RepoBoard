# WP-05 Diff-Ansicht

Branch `wp/05-diff`, 2026-11-21, noch ohne Release.

## Planung (aus `docs/roadmap.md`)
- Diff einer Datei (gestaged/ungestaged) neben der Liste, Monaco als Diff-Editor wie in FerretSharp (lokal mitgeliefert, `JsModule`-Muster).
- Danach: einzelne Hunks bzw. Zeilen stagen/unstagen (`git apply --cached` mit erzeugtem Patch).
- Offen waren: Diff im Detailbereich der Zeile oder als eigener Bereich/Dialog? Hunk-Staging im selben Paket oder eigenes?

## Entscheidungen des Nutzers (2026-11-21)
- Der Diff kommt aus **`git diff` mit eigener Ansicht**, nicht aus Monaco (ADR 0008).
- Er erscheint **in der geöffneten Zeile**: die Dateiliste bleibt links, der Diff ersetzt Branches und Commits, solange er offen ist.
- **Hunk-Staging kommt als eigenes Paket danach** (Roadmap).
- **Erst nur inline**; Side-by-Side in den Backlog.
- Den Plan hat der Nutzer mit diesen Antworten freigegeben.

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0008: Befehle je Art der Änderung, Grenzen (Datei über 5 MB im Worktree nicht lesen – seit dem Review für alle Dateien, nicht nur neue –, höchstens 5 000 Zeilen, Wort-Hervorhebung höchstens 2 Mio. Token-Paare je Datei), Wort-Hervorhebung paarweise, Merkmale für CR am Zeilenende und fehlenden Zeilenumbruch.
- Ein Klick auf den Dateinamen öffnet den Diff, ein zweiter oder X bzw. Esc schließt ihn; das Häkchen stagt weiter. Nach dem Stagen oder Unstagen folgt der Diff der Datei auf die andere Seite; ist sie in keiner Liste mehr, schließt er.
- Mit jedem neuen Status (Auto-Sync alle 20 s, nach jeder Aktion, beim Zurückwechseln ins Fenster) wird der Diff neu gelesen; bis der neue da ist, bleibt der alte stehen.

## Gebaut
- **Core:** `FileDiff` (`Parse`, `DiffHunk`, `DiffLine`, `DiffSpan`, Grenzen `MaxLines`, `MaxFileBytes`, `MaxWordCells`), `DiffWords`, `GitClient.DiffAsync`, `BoardService.DiffAsync` (liest nur: wartet nicht auf das Tor, ein Fehler steht im Diff statt in `RepoState.Error`), Text `SyncText.Op_Diff`.
- **UI:** `DiffPanel` (inline, Zeilennummern, Hunk-Köpfe, Wort-Markierung, ␍ und ⊘ mit Tooltip, Hinweise für Binär, zu groß, gekürzt, keine Textänderung, Konflikt, Fehler; Esc über den Fokus des Bereichs), `RepoDetailsPanel` (Dateiname als Knopf, Auswahl markiert, `details.diff-open`), `UI/State/DiffSelection` (`DiffTarget`, `Resolve`). Texte `Diff_*` in `BoardText` (en/de), Tokens `--green-mark`/`--danger-mark`.
- **Seit dem Review:** `DiffSelection.Next`, `FileChange.IsNestedRepository`, `DiffPanel.FocusAsync` (Fokus nur nach Klick) und `ShouldRender`, `@key` je Datei und Seite in der Liste, `--literal-pathspecs` bei Diff/Stage/Unstage, `Task.Run` in `BoardService.DiffAsync`, zeilenweiser Parser, `MaxFileBytes` für alle Dateien, `MaxWordCells`.
- **Doku:** ADR 0008, README, Architektur (UI-Konzept), Roadmap (WP-25 Hunk-Staging), Backlog.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test`: 232 Tests grün, nach dem Review 240.
- E2E (2026-11-21, Testinstanz mit `--data-dir` im Scratchpad, ein Test-Repo dort mit Bare-Remote und je einer Art Änderung). Eingeschleuste Eingaben wurden gefiltert (`Test-InjectedInput.ps1`), daher CDP und Klicks des Nutzers:
  - Je Datei per CDP: geänderte Datei mit drei Hunks und Wort-Markierung (`set`→`init`, `5`→`50`), gestaged und ungestaged derselben Datei getrennt, Umbenennung mit „Umbenannt von long.txt“, Binärdatei, neue Datei mit Umlaut, CRLF-Datei („Keine Textänderungen“ – mit `core.autocrlf` des Nutzers sieht git keinen Unterschied).
  - **Gefunden und behoben:** Eine neue Datei zeigte „Diff: warning: … LF will be replaced by CRLF“ statt des Diffs – `--no-index` endet mit 1 und schreibt bei `core.autocrlf` eine Warnung auf stderr. Erst zählten nur `fatal:`/`error:` als Fehler, seit dem Review Exit 1 nur mit Diff auf stdout als Erfolg (Test `A_warning_of_git_does_not_hide_the_diff_of_a_new_file`).
  - Stagen und Unstagen bei offenem Diff: er folgt der Datei auf die andere Seite. Esc (CDP-Taste) schließt ihn, Branches und Commits sind zurück.
  - Echte Maus (Nutzer): Klick auf „Model.cs“ öffnet den Diff, X schließt ihn (`isTrusted`).
  - Bilder hell und dunkel; Log ohne `[ERR]`/`[WRN]`.
- Nicht geprüft: sehr große Diffs (5 000 Zeilen) im Fenster, Dateien mit textconv-Treiber.

## Offen
- Der Nutzer bemerkte beim Klicken und Hovern eine leichte Verzögerung. Vermutlich derselbe Weg wie in FerretSharp ADR 0039 (`WebView2CompositionControl`, GPU-Kopie ohne Warten; dort `CopyFence`/`WebViewCapture`), nicht WP-05 – Backlog, Bekannte Lücken.
- Side-by-Side, Syntax-Highlighting, Kodierungen außer UTF-8, Diff im externen Werkzeug: Backlog.

## Nachtrag: Code-Review (2026-11-21)
Getrennter Reviewer-Agent über `2b8d871..659bbd0`, danach ein zweiter Durchgang über die Fix-Commits. Entscheidung des Nutzers: „Alle beheben“ (alle sieben Befunde und die beiden kleinen Punkte aus „Vorher schon da“), zur 5-MB-Grenze die vorgeschlagene Annahme (Größe der Datei im Worktree, für alle Dateien); zum zweiten Durchgang „Ja, so machen“ (8–10 beheben, 11 in den Backlog).

1. M – Ein geschlossener Diff öffnete sich später von selbst wieder und nahm den Fokus (Auswahl blieb gespeichert) → behoben: `DiffSelection.Next` in `RepoDetailsPanel.OnParametersSet` löscht sie, wenn die Datei in keiner Liste mehr steht; den Fokus setzt nur ein Klick (`DiffPanel.FocusAsync` über `@ref`). Test `The_target_moves_with_the_diff_and_ends_with_the_file`; E2E: Datei weg → Diff zu, Datei zurück → bleibt zu, Fokus bleibt im Commit-Feld.
2. M – Parsen und Wort-Diff auf dem UI-Thread, große getrackte Dateien ohne Grenze → behoben: `BoardService.DiffAsync` über `Task.Run`, `FileDiff.Parse` liest zeilenweise und hört nach 5 000 Zeilen auf, 5-MB-Grenze für jede Datei im Worktree, Budget der Wort-Hervorhebung je Datei. Tests `A_file_above_the_limit_is_not_read` (neu und getrackt), `Word_highlighting_stops_at_the_budget_of_the_file`.
3. M – Pfade gingen als Muster an git (`[ab].txt` zog `a.txt` mit in den Diff; beim Stagen genauso, vorher schon da) → behoben: `--literal-pathspecs` bei Diff, Stage und Unstage. Test `Paths_are_file_names_not_patterns`.
4. K – Fehler von `--no-index` am englischen Präfix erkannt → behoben: Exit 1 gilt nur mit Diff auf stdout als Erfolg. Test `A_new_file_gone_before_its_diff_is_an_error` (englisch und übersetzt über einen nachgebauten `GitRunner`).
5. K – Nach dem Folgen auf die andere Seite schloss ein Klick nicht, später sprang der Diff zurück → behoben mit 1 (`Next` zieht die Seite nach).
6. K – Eingebettetes Repo `sub/` zeigte „Could not access 'sub/nul'“ → behoben: `FileChange.IsNestedRepository`, Hinweis statt git-Aufruf. Test `A_repository_inside_the_worktree_is_one_entry_without_a_diff`; E2E.
7. K – Das Diff-Panel baute bei jedem Rendern der Seite alle Zeilen neu auf → behoben: `ShouldRender` nur bei geändertem Diff, Fehler, Datei oder Seite.
- Vorher schon da, mitbehoben: Markierung `?` war gelb (Klasse `l-new`, CSS `l-\?`) → grün; Pfade mit führendem Punkt lasen sich rückwärts (`gitignore.`) → inneres `span` mit `direction: ltr; unicode-bidi: isolate` (E2E-Bild).
- Beim E2E der Fixes gefunden (vorher schon da): Ohne `@key` setzte Blazor die Zeile einer Datei, die in die andere Liste wanderte, für die nächste Datei ein – mit dem vom Klick gesetzten Häkchen (ein Klick darauf hätte die falsche Datei gestaged) → behoben: `@key` je Datei und Seite; E2E: nach dem Stagen kein falsches Häkchen.

Zweiter Durchgang:
8. M – `GIT_LITERAL_PATHSPECS` global in `GitRunner` erbte sich an Hooks weiter, deren Globs nichts mehr fanden (Regression aus 3) → behoben: nur `--literal-pathspecs` an den Befehlen mit Pfaden (keine Hooks). Test `A_hook_still_matches_globs`.
9. K – Der Test zu 4 scheiterte ohne den Fix nicht (git im Test antwortet englisch) → behoben: Theory mit übersetzter Meldung über NSubstitute; scheitert ohne den Fix.
10. K – Regel `git.md`, ADR 0008 und dieses Protokoll beschrieben den alten Stand → nachgezogen.
11. K – Eine gelöschte große Datei hat keine Größe im Worktree, die Grenze greift nicht → nicht behoben, Backlog (Entscheidung des Nutzers).
- Vorher schon da, Backlog: Scheitert ein Stagen, bleibt das vom Klick gesetzte Häkchen stehen (`checked` in der Liste „Nicht gestaged“ nicht gebunden).

Dritter Durchgang (Nutzer: „Ja, beides“ – beheben und pushen):
12. K – Der Test mit `Substitute.ForPartsOf` rief beim Einrichten die echte `RunAsync` auf → `Substitute.For`.
13. K – Die Regel zu literalen Pfaden nannte die Bedingung „ohne Hooks“ nicht → ergänzt (sonst `:(literal)` je Pfad).
14. K – „Gebaut“ nannte die Ergänzungen aus dem Review nicht → ergänzt.

Regeln ergänzt: `git.md` (literale Pfade je Befehl, nie global; Erfolg von `--no-index`), `ui.md` (`@key` bei Listen mit Eingabefeldern).
