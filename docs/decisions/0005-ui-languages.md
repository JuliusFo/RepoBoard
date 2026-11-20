# ADR 0005: Oberfläche in Englisch und Deutsch

- Status: übernommen aus FerretSharp (ADR 0017 dort); **Vorschlag (nicht bestätigt):** die Sprache folgt Windows
- Datum: 2026-11-20

## Entscheidung

- Jeder sichtbare Text steht in typisierten resx-Dateien: `Resources/<Bereich>Text.resx` (Englisch, neutrale Sprache) und `<Bereich>Text.de.resx` (Deutsch). Der Build erzeugt die Klassen (`Directory.Build.targets`), ein fehlender Schlüssel ist ein Compilerfehler. `ResourceTests` prüfen beide Sprachen, gleiche Platzhalter und Kommentare an Platzhaltern.
- Die Sprache: `--lang=en|de`, sonst Deutsch auf einem deutschen Windows und Englisch sonst (`CommandLine.UiCulture`). Eine Einstellung in der App kommt mit einer Einstellungsseite (Backlog).
- Nur `CurrentUICulture` wird gesetzt, synchron in `Program.Main`; Zahlen und Daten behalten das Format von Windows.
- Git-Begriffe bleiben auch im Deutschen englisch (Branch, Commit, Fetch, Pull, Push, Stage).
