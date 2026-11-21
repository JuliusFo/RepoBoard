# ADR 0008: Der Diff kommt von git, die Ansicht ist eigenes HTML

- Status: teilweise entschieden. **Entscheidung des Nutzers (2026-11-21, WP-05):** der Diff aus `git diff` mit eigener Ansicht statt Monacos Diff-Editor; inline; in der geöffneten Zeile; Hunk-Staging als eigenes Paket danach. **Vorschlag (nicht bestätigt):** die Einzelheiten unten.
- Datum: 2026-11-21

## Kontext

Die Roadmap sah für WP-05 Monaco als Diff-Editor vor, wie der SQL-Editor in FerretSharp (dort ~6 MB, AMD-Build, ADR 0011). Monaco vergleicht aber zwei Texte mit einem eigenen Algorithmus: Es normalisiert Zeilenenden (eine Änderung nur von LF nach CRLF ist unsichtbar), kennt weder `.gitattributes` noch Diff-Treiber oder git's Erkennung von Binärdateien, und Hunks, die man später einzeln stagen will, müssten aus Monacos Diff in einen Patch für `git apply` übersetzt werden – die beiden Algorithmen schneiden Hunks verschieden.

## Entscheidung

- **Quelle:** `git diff` je Datei über `GitClient.DiffAsync` (ADR 0002): gestaged `diff --cached -M -- [<alt>] <neu>` (gegen HEAD, vor dem ersten Commit gegen den leeren Baum), ungestaged `diff -- <pfad>` (gegen den Index), neue Dateien `diff --no-index -- /dev/null <pfad>` (Exit-Code 1 heißt „verschieden“ oder Fehler: Erfolg nur mit einem Diff auf stdout – eine Warnung auf stderr wie CRLF bei `core.autocrlf` ist kein Fehler, und git übersetzt seine Meldungen). Immer `--literal-pathspecs --no-color --no-ext-diff`; textconv und die übrigen Diff-Einstellungen des Nutzers gelten wie im Terminal. Konfliktdateien und eingebettete Repos (`sub/`) zeigen nur einen Hinweis (Konflikte: WP-07).
- **Grenzen:** eine Datei über 5 MB im Worktree wird nicht gelesen (git liest sie ganz, und das mit jedem neuen Status; Annahme bestätigt vom Nutzer, Review 2026-11-21) – eine gelöschte Datei hat keine Größe im Worktree und ist davon nicht erfasst (Backlog); höchstens 5 000 Zeilen werden gezeigt, darunter ein Hinweis; die Wort-Hervorhebung vergleicht je Datei höchstens 2 Mio. Token-Paare. Lesen und Parsen laufen auf dem Thread-Pool, nicht auf dem UI-Thread.
- **Parser** `FileDiff.Parse` (Core, ohne git testbar): Hunks mit alten und neuen Zeilennummern, gezählt nach dem Hunk-Kopf (eine leere Kontextzeile bei `diff.suppressBlankEmpty` zählt mit), „\ No newline at end of file“ und ein CR am Zeilenende als Merkmal der Zeile, Binärdateien.
- **Wort-Hervorhebung** `DiffWords`: ein Block entfernter Zeilen vor einem Block hinzugefügter wird Zeile für Zeile gepaart, die geänderten Wörter (längste gemeinsame Teilfolge von Wörtern, Leerraum, Zeichen) hervorgehoben. git entscheidet, welche Zeilen sich ändern; das ist nur die Markierung darin.
- **Ansicht** (`DiffPanel`): inline wie `git diff`, alte und neue Zeilennummer, Hunk-Köpfe mit dem Abschnitt, Farben aus den Tokens (hell/dunkel); in der geöffneten Zeile anstelle von Branches und Commits. Kein Syntax-Highlighting.

## Folgen

- Was die Ansicht zeigt, ist das, was git committet – auch Zeilenenden (␍ an der Zeile) und Binärdateien.
- Hunk-Staging (eigenes Paket) kann Hunks 1:1 als Patch an `git apply --cached` geben.
- Kein Paket, kein JavaScript, keine ~6 MB im Installer; dafür kein Syntax-Highlighting und keine Side-by-Side-Ansicht (Backlog).
- Dateien, die nicht UTF-8 sind, erscheinen mit Ersatzzeichen (`GitRunner` liest UTF-8) – Backlog.
