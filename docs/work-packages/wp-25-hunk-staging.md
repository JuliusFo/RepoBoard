# WP-25 Hunks und Zeilen stagen

Branch `wp/25-hunk-staging`, 2026-11-21, noch kein Release.

## Planung (aus `docs/roadmap.md`)
- Braucht WP-05 (Diff aus `git diff`, ADR 0008). Als eigenes Paket nach WP-05 entschieden vom Nutzer (2026-11-21).
- Am Hunk-Kopf der Diff-Ansicht „Hunk stagen“ (ungestaged) bzw. „Hunk unstagen“ (gestaged): den Hunk als Patch mit Dateikopf an `git apply --cached` (bzw. `--cached --reverse`) über stdin geben; danach Status und Diff neu.
- Danach: einzelne Zeilen auswählen und nur sie stagen (Patch aus den gewählten Zeilen bauen, nicht gewählte `-` werden Kontext, nicht gewählte `+` fallen weg).
- Invariante: schreibt nur in den Index, nie in den Worktree (kein `git apply` ohne `--cached`). Tests gegen `GitSandbox` je Fall (erster/letzter Hunk, neue Datei, fehlender Zeilenumbruch am Ende, CRLF).
- Offen waren: Neue Dateien teilweise stagen (`git add -N`)? Zeilenauswahl per Klick/Ziehen oder Häkchen je Zeile?

## Entscheidungen des Nutzers (2026-11-21)
- **Teile neuer Dateien lassen sich stagen**, ohne `git add -N`: ein Patch `new file mode` legt die Datei mit den gewählten Zeilen im Index an.
- **Zeilenauswahl per Klick am linken Rand**, Shift+Klick wählt einen Bereich; Ziehen in den Backlog. Keine Häkchen je Zeile.
- Den Plan hat der Nutzer mit „Ja, so bauen“ freigegeben.

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0012: `--whitespace=nowarn`, `--unidiff-zero` bei Hunks ohne Kontext, Patch für gelöschte Dateien, Rückfall auf das Stagen der ganzen Datei, wenn die Auswahl jede Änderung ist, Ablehnung bei abgeschnittenem Hunk, Nicht-UTF-8 und einer Auswahl, die eine Zeile ohne Zeilenumbruch in die Mitte brächte.
- Knopf rechts im Hunk-Kopf (bleibt beim seitlichen Scrollen stehen), Beschriftung „Zeilen stagen (n)“ mit Auswahl; gewählte Zeilen mit Akzentfarbe am Rand. Die Auswahl bleibt bei einem neuen Status, solange ihr Hunk gleich aussieht, und wird nach erfolgreichem Stagen geleert.

## Gebaut
- **Core:** `HunkPatch` (`Build`, `Quote`, `HunkPatchKind`), `GitClient.ApplyToIndexAsync`, `GitRunner.RunAsync` mit `input` (stdin, UTF-8 ohne BOM, erst lesen, dann schreiben), `FileDiff.NewFileMode`/`DeletedFileMode`, `BoardService.StageHunkAsync` (durch das Tor, Fehler als `RepoState.Error`), Texte `Op_StageHunk`/`Op_StageLines`/`Op_UnstageHunk`/`Op_UnstageLines`, `Hunk_Incomplete`/`Hunk_NotUtf8`/`Hunk_SplitsLastLine`.
- **UI:** `DiffPanel` (Knopf je Hunk, klickbarer Rand je geänderter Zeile, `ShouldRender` mit Auswahl und laufendem Stagen), `UI/State/LineSelection`, Texte `Diff_StageHunk`/`Diff_UnstageHunk`/`Diff_StageLines`/`Diff_UnstageLines` und Hinweise in `BoardText`, CSS `.diff-hunk .hunk-stage`, `.diff-line .gutter`, `.picked`.
- **Doku:** ADR 0012, README, CHANGELOG, Architektur (UI-Konzept), Regel `git.md`, Backlog (Ziehen, Tastatur, mehrere Hunks).

## Geprüft
- `dotnet build -warnaserror` und `dotnet test`: neue Tests `HunkStagingTests` (erster, mittlerer, letzter Hunk hin und zurück; einzelne Zeilen; Teil einer neuen Datei und zurück; Teil einer Löschung und einer gestagten Löschung; Umbenennung; Zeile ohne Zeilenumbruch am Ende; CRLF; veralteter Diff abgelehnt; `apply.whitespace=error` mit `diff.context=0`; ganze Datei, nichts, abgeschnittener Hunk, Auswahl ohne Zeilenumbruch abgelehnt; Quoting; Nicht-UTF-8 über das Board) – jeweils mit unverändertem Worktree – und `LineSelectionTests`.
- E2E (2026-11-21, Testinstanz mit `--data-dir` im Scratchpad, Test-Repo dort mit Bare-Remote; `Test-InjectedInput.ps1`: Eingaben kommen durch):
  - Erster Versuch mit echter Maus: Windows holte das Fenster nicht nach vorn (Nutzer war im Spiel) – abgebrochen, wie in `docs/e2e-testing.md` vorgesehen; auf Zuruf des Nutzers wiederholt.
  - Echte Maus (`Invoke-RealClick.ps1`): Klick auf den Rand von „line 15b added“ im gestagten Diff wählt sie („Zeilen unstagen (1)“), Klick auf den Knopf nimmt nur diese Zeile aus dem Index; Worktree-Datei unverändert (SHA-1 gleich).
  - CDP: Klick plus Shift+Klick wählt den Bereich (3 Zeilen, keine Textmarkierung), Klick hebt eine Zeile auf, „Zeilen stagen (2)“ stagt genau `-line 15`/`+line 15b added`; „Hunk unstagen“ auf dem einzigen Hunk nimmt die Datei ganz heraus, der Diff folgt auf „Nicht gestaged“; Teil der neuen Datei „neu ä.txt“ (`AM`, Index nur `n2`); CRLF-Zeile mit `\r\n` im Index; `tail` ohne Umbruch → „more“ gewählt ergibt `tail\nmore`; „Hunk stagen“ ohne Auswahl stagt den dritten Hunk.
  - Bild dunkel; Log ohne `[ERR]`/`[WRN]`.
- Nicht geprüft: helles Theme im Bild, sehr große Diffs mit Auswahl.
