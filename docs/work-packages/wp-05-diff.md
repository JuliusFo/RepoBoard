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
- Einzelheiten nach ADR 0008: Befehle je Art der Änderung, Grenzen (neue Datei über 5 MB nicht lesen, höchstens 5 000 Zeilen), Wort-Hervorhebung paarweise, Merkmale für CR am Zeilenende und fehlenden Zeilenumbruch.
- Ein Klick auf den Dateinamen öffnet den Diff, ein zweiter oder X bzw. Esc schließt ihn; das Häkchen stagt weiter. Nach dem Stagen oder Unstagen folgt der Diff der Datei auf die andere Seite; ist sie in keiner Liste mehr, schließt er.
- Mit jedem neuen Status (Auto-Sync alle 20 s, nach jeder Aktion, beim Zurückwechseln ins Fenster) wird der Diff neu gelesen; bis der neue da ist, bleibt der alte stehen.

## Gebaut
- **Core:** `FileDiff` (`Parse`, `DiffHunk`, `DiffLine`, `DiffSpan`, Grenzen `MaxLines`, `MaxNewFileBytes`), `DiffWords`, `GitClient.DiffAsync`, `BoardService.DiffAsync` (liest nur: wartet nicht auf das Tor, ein Fehler steht im Diff statt in `RepoState.Error`), Text `SyncText.Op_Diff`.
- **UI:** `DiffPanel` (inline, Zeilennummern, Hunk-Köpfe, Wort-Markierung, ␍ und ⊘ mit Tooltip, Hinweise für Binär, zu groß, gekürzt, keine Textänderung, Konflikt, Fehler; Esc über den Fokus des Bereichs), `RepoDetailsPanel` (Dateiname als Knopf, Auswahl markiert, `details.diff-open`), `UI/State/DiffSelection` (`DiffTarget`, `Resolve`). Texte `Diff_*` in `BoardText` (en/de), Tokens `--green-mark`/`--danger-mark`.
- **Doku:** ADR 0008, README, Architektur (UI-Konzept), Roadmap (WP-25 Hunk-Staging), Backlog.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test`: 232 Tests grün.
- E2E (2026-11-21, Testinstanz mit `--data-dir` im Scratchpad, ein Test-Repo dort mit Bare-Remote und je einer Art Änderung). Eingeschleuste Eingaben wurden gefiltert (`Test-InjectedInput.ps1`), daher CDP und Klicks des Nutzers:
  - Je Datei per CDP: geänderte Datei mit drei Hunks und Wort-Markierung (`set`→`init`, `5`→`50`), gestaged und ungestaged derselben Datei getrennt, Umbenennung mit „Umbenannt von long.txt“, Binärdatei, neue Datei mit Umlaut, CRLF-Datei („Keine Textänderungen“ – mit `core.autocrlf` des Nutzers sieht git keinen Unterschied).
  - **Gefunden und behoben:** Eine neue Datei zeigte „Diff: warning: … LF will be replaced by CRLF“ statt des Diffs – `--no-index` endet mit 1 und schreibt bei `core.autocrlf` eine Warnung auf stderr. Jetzt zählen nur `fatal:`/`error:` als Fehler (Test `A_warning_of_git_does_not_hide_the_diff_of_a_new_file`).
  - Stagen und Unstagen bei offenem Diff: er folgt der Datei auf die andere Seite. Esc (CDP-Taste) schließt ihn, Branches und Commits sind zurück.
  - Echte Maus (Nutzer): Klick auf „Model.cs“ öffnet den Diff, X schließt ihn (`isTrusted`).
  - Bilder hell und dunkel; Log ohne `[ERR]`/`[WRN]`.
- Nicht geprüft: sehr große Diffs (5 000 Zeilen) im Fenster, Dateien mit textconv-Treiber.

## Offen
- Der Nutzer bemerkte beim Klicken und Hovern eine leichte Verzögerung. Vermutlich derselbe Weg wie in FerretSharp ADR 0039 (`WebView2CompositionControl`, GPU-Kopie ohne Warten; dort `CopyFence`/`WebViewCapture`), nicht WP-05 – Backlog, Bekannte Lücken.
- Side-by-Side, Syntax-Highlighting, Kodierungen außer UTF-8, Diff im externen Werkzeug: Backlog.
