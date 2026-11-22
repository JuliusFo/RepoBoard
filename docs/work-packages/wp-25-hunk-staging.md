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
- **UI:** `DiffPanel` (Knopf je Hunk, `ShouldRender` mit Auswahl und laufendem Stagen), seit dem Review `DiffLineView` (Zeile mit klickbarem Rand, eigenes `ShouldRender`), `UI/State/LineSelection`, Texte `Diff_StageHunk`/`Diff_UnstageHunk`/`Diff_StageLines`/`Diff_UnstageLines` und Hinweise in `BoardText`, CSS `.diff-hunk .hunk-stage`, `.diff-line .gutter`, `.picked`.
- **Doku:** ADR 0012, README, CHANGELOG, Architektur (UI-Konzept), Regel `git.md`, Backlog (Ziehen, Tastatur, mehrere Hunks).

## Geprüft
- `dotnet build -warnaserror` und `dotnet test`: neue Tests `HunkStagingTests` (erster, mittlerer, letzter Hunk hin und zurück; einzelne Zeilen; Teil einer neuen Datei und zurück; Teil einer Löschung und einer gestagten Löschung; Umbenennung; Zeile ohne Zeilenumbruch am Ende; CRLF; veralteter Diff abgelehnt; `apply.whitespace=error` mit `diff.context=0`; ganze Datei, nichts, abgeschnittener Hunk, Auswahl ohne Zeilenumbruch abgelehnt; Quoting; Nicht-UTF-8 über das Board) – jeweils mit unverändertem Worktree – und `LineSelectionTests`.
- E2E (2026-11-21, Testinstanz mit `--data-dir` im Scratchpad, Test-Repo dort mit Bare-Remote; `Test-InjectedInput.ps1`: Eingaben kommen durch):
  - Erster Versuch mit echter Maus: Windows holte das Fenster nicht nach vorn (Nutzer war im Spiel) – abgebrochen, wie in `docs/e2e-testing.md` vorgesehen; auf Zuruf des Nutzers wiederholt.
  - Echte Maus (`Invoke-RealClick.ps1`): Klick auf den Rand von „line 15b added“ im gestagten Diff wählt sie („Zeilen unstagen (1)“), Klick auf den Knopf nimmt nur diese Zeile aus dem Index; Worktree-Datei unverändert (SHA-1 gleich).
  - CDP: Klick plus Shift+Klick wählt den Bereich (3 Zeilen, keine Textmarkierung), Klick hebt eine Zeile auf, „Zeilen stagen (2)“ stagt genau `-line 15`/`+line 15b added`; „Hunk unstagen“ auf dem einzigen Hunk nimmt die Datei ganz heraus, der Diff folgt auf „Nicht gestaged“; Teil der neuen Datei „neu ä.txt“ (`AM`, Index nur `n2`); CRLF-Zeile mit `\r\n` im Index; `tail` ohne Umbruch → „more“ gewählt ergibt `tail\nmore`; „Hunk stagen“ ohne Auswahl stagt den dritten Hunk.
  - Bild dunkel; Log ohne `[ERR]`/`[WRN]`.
- Nicht geprüft: helles Theme im Bild, sehr große Diffs mit Auswahl.

## Nachtrag: Code-Review (2026-11-21)
Getrennter Reviewer-Agent über `7e39aaa..e097af3`. Entscheidung des Nutzers: „Alle beheben“ (zu 1: Teil-Stagen bei Diff-Treiber gesperrt, ganze Datei geht; zu 5: erst messen).

1. S – Der Patch entstand aus dem angezeigten Diff mit textconv: eine neue Datei kam als Text des Diff-Treibers in den Index (git-crypt: Geheimnis im Klartext) → behoben: `StageHunkAsync` liest im Tor mit `--no-textconv` neu, weicht das ab, wird Teil-Stagen abgelehnt (`Hunk_DiffDriver`). Test `Parts_of_a_file_shown_through_textconv_are_not_staged`.
2. M – „Hunk unstagen“ auf einer gestagten Umbenennung machte daraus gestagte Löschung plus ungetrackte Datei → behoben: dort immer der Patch-Weg (`wholeFile: false`). Vorher schon da, mitbehoben: das Häkchen unstagt eine Umbenennung jetzt mit beiden Pfaden. Test `A_staged_rename_keeps_its_rename_or_goes_back_as_a_whole`.
3. M – Mit `diff.context=0` lehnte git eine Teilauswahl mit Kontext nur an einem Ende ab → behoben: `--unidiff-zero`, sobald am Anfang oder Ende Kontext fehlt. Test `Part_of_a_hunk_without_context_is_staged`.
4. K (Verdacht) – Schreiben auf stdin ohne Zeitlimit → nicht reproduziert: ein Test mit `git daemon` (liest stdin nicht) und 4 MB Eingabe endete auch ohne Fix nach 2,8 s im Zeitlimit; `git apply` liest ohnehin zuerst. Nichts geändert.
5. K (Verdacht) – Neu-Rendern bei jedem Klick → gemessen (E2E, 5 000 Zeilen): ~110 ms je Klick; behoben mit `DiffLineView` (eigenes `ShouldRender`): ~60 ms, Öffnen 720 → 550 ms.
6. K – „ganze Datei“ stagte auch eine ungesehene Änderung → behoben: der im Tor neu gelesene Diff muss der gezeigte sein (`Hunk_Changed`). Test `A_diff_that_is_no_longer_current_stages_nothing_through_the_board`.
7. K – Patch ohne Kontext ungeschützt gegen veralteten Diff → mit 6 behoben, ADR eingeschränkt.
8. K – Letzte Zeile bekam in einer CRLF-Datei nur LF → behoben. Test `A_last_line_without_newline_gets_a_windows_one_in_a_crlf_file`.
9. K (Verdacht) – Symlink, Submodul, Typwechsel in Teilen → behoben ohne Nachstellen im Repo (Symlinks unter Windows): `FileDiff.IsSpecialFile`, `Hunk_SpecialFile`. Test `Links_and_submodules_are_not_staged_in_parts` (geparste Ausgabe); Typwechsel seit 13 als zwei Dateien.
10. K – Zeilen ließen sich während des Stagens wählen und gingen danach verloren → behoben: keine Auswahl, solange gestaged wird. Ohne Test (Komponente).
- Jeder neue Test scheiterte, als der jeweilige Fix probehalber ausgebaut war.
- E2E nach den Fixes: Auswahl, Bereich und Aufheben per CDP; echter Klick nach dem Umbau zu `DiffLineView` nicht möglich (Windows holte das Fenster nicht nach vorn) – offen.

Zweiter Durchgang (Fix-Commits `e097af3..b10bf6c`; Nutzer: „Alle beheben“):
11. M – Das Neu-Lesen im Tor parste den Diff zweimal auf dem UI-Thread (Regression aus 1) → behoben: `StageHunkAsync` arbeitet über `Task.Run` (`StageHunkCoreAsync`), wie `DiffAsync`. Ohne Test (UI-Thread).
12. K (Verdacht) – Gleich nach dem Stagen war der Knopf am nächsten Hunk aktiv, obwohl der neue Diff noch lud → „Die Datei hat sich geändert“ → im E2E nachgestellt (ohne Fix: Fehlerzeile, mit Fix: beide Hunks gestaged), behoben: Knöpfe aus, bis der Diff zum neuen Status gelesen ist (`_current`).
13. K – Eine von `diff -M` nicht gepaarte Umbenennung bekam die Meldung zu Links und Submodulen → eigener Fall `IsTwoFiles` mit Meldung `Hunk_TwoFiles`; `IsSpecialFile` nur noch bei `120000`/`160000`. Test `A_deleted_and_an_added_file_in_one_diff_are_not_staged_in_parts`.

Dritter Durchgang (`b10bf6c..6e7c6b7`; Nutzer: „Beheben, dann pushen“):
14. K – Nach einer unerwarteten Ausnahme beim Lesen des Diffs blieben die Hunk-Knöpfe bis zum nächsten Status gesperrt → behoben: `_current` im `finally` (das Stagen vergleicht ohnehin mit frisch gelesenem Diff). Ohne Test (Komponente).
- Echter Mausklick nach dem Umbau zu `DiffLineView` (2026-11-22, `Invoke-RealClick.ps1`, ungefilterte Eingabe): Klick auf den Rand von „line 15b added“ wählt sie („Zeilen stagen (1)“), Klick auf den Knopf bringt nur diese Zeile in den Index, „line 15 changed“ bleibt draußen; Worktree-Datei unverändert (Hash gleich), Log ohne Warnungen.
