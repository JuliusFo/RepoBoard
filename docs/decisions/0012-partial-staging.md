# ADR 0012: Hunks und Zeilen stagen über `git apply --cached`

- Status: teilweise entschieden. **Entscheidung des Nutzers (2026-11-21, WP-25):** Teile neuer Dateien lassen sich stagen (ohne `git add -N`); Zeilen werden per Klick am linken Rand gewählt, Shift+Klick wählt einen Bereich. **Vorschlag (nicht bestätigt):** die Einzelheiten unten.
- Datum: 2026-11-21

## Kontext

Der Diff kommt seit WP-05 aus `git diff` (ADR 0008), seine Hunks sind also genau die von git. Teile davon lassen sich nur als Patch in den Index bringen: `git add -p` ist interaktiv (Terminal), `git apply` dagegen nimmt einen Patch auf stdin. Was RepoBoard dabei nie darf: ungesicherte Arbeit anfassen (`CLAUDE.md` §2, ADR 0003) – der Worktree ist die Arbeit des Nutzers.

## Entscheidung

- **Nur der Index:** `git apply --cached [--reverse] --whitespace=nowarn [--unidiff-zero] -` über `GitClient.ApplyToIndexAsync`, der Patch auf stdin (`GitRunner` mit `input`, UTF-8 ohne BOM). Immer `--cached`: der Worktree wird nie geschrieben. `--whitespace=nowarn`, weil ein `apply.whitespace=error` des Nutzers sonst eine Zeile ablehnt, die längst in der Datei steht; `--unidiff-zero` nur bei einem Hunk ohne Kontext (`diff.context=0`).
- **Patch** `HunkPatch.Build` (Core, rein): aus dem gezeigten `DiffHunk` und den gewählten Zeilen. Stagen wendet den Patch vorwärts auf den Index an (die alte Seite des ungestagten Diffs): eine nicht gewählte `-`-Zeile wird Kontext, eine nicht gewählte `+`-Zeile fällt weg. Unstagen wendet ihn rückwärts auf den Index an (die neue Seite des gestagten Diffs): eine nicht gewählte `+`-Zeile wird Kontext, eine nicht gewählte `-`-Zeile fällt weg. Zähler und Anfang im Hunk-Kopf werden neu berechnet (beide Seiten beginnen dort, wo die Seite beginnt, auf die er angewendet wird; eine leere Seite nennt die Zeile davor). CR am Zeilenende und „\ No newline at end of file“ kommen aus den Merkmalen der Zeile.
- **Neue und gelöschte Dateien:** Stagen eines Teils einer neuen Datei ist ein Patch `new file mode` (Modus aus dem Diff-Kopf) gegen `/dev/null` – im Index entsteht die Datei mit genau den gewählten Zeilen, ohne `intent-to-add`. Unstagen eines Teils einer gestagten Löschung ist rückwärts ein Patch `deleted file mode`. Sonst ein Patch auf denselben Pfad; nach einer Umbenennung der neue (nur er steht im Index).
- **Ganze Datei:** Ist die Auswahl jede Änderung der Datei (ein Hunk, alles gewählt, Diff nicht gekürzt), stagt bzw. unstagt RepoBoard die Datei wie das Häkchen – so stimmen Modus, Umbenennung, neue und gelöschte Datei ohne Sonderfälle.
- **Abgelehnt, bevor git es sieht:** der abgeschnittene letzte Hunk eines gekürzten Diffs (5 000 Zeilen, ADR 0008); ein Hunk mit U+FFFD (die Datei ist kein UTF-8, der Patch schriebe Ersatzzeichen in den Index); eine Auswahl, nach der eine Zeile ohne Zeilenumbruch nicht die letzte ihrer Seite wäre (eine solche Kontextzeile mit gewählten Zeilen danach wird als `-`/`+` mit Zeilenumbruch geschrieben – „a“ ohne Umbruch, „+b“ gewählt ergibt „a\nb“; was dann noch übrig bleibt, wird abgelehnt).
- **Veralteter Diff:** Der Patch entsteht aus dem Diff, den der Nutzer sieht. git prüft jede Kontextzeile gegen den Index und wendet alles oder nichts an: hat sich die Datei seither geändert, scheitert der Aufruf mit git's Meldung, der Index bleibt, und der neue Status bringt den neuen Diff.
- **Auswahl in der UI** (`UI/State/LineSelection`): Klick auf den Rand (Nummern und Vorzeichen) wählt eine geänderte Zeile oder hebt sie auf, Shift+Klick wählt alle geänderten Zeilen vom letzten Klick bis hier; immer nur in einem Hunk. Der Knopf am Hunk-Kopf heißt dann „Zeilen stagen (n)“. Die Auswahl bleibt bei einem neuen Status, solange ihr Hunk gleich aussieht.

## Folgen

- Kein neues Werkzeug, keine eigene Patch-Logik außer dem Bau des Patches: Anwenden, Prüfen und Atomarität macht git.
- `git apply` startet keine Hooks; `--literal-pathspecs` ist nicht nötig, der Pfad steht im Patch (mit C-Quoting bei `"`, `\` und Steuerzeichen).
- Auswahl per Ziehen und Tastatur fehlen (Backlog).
