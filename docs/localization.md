# Texte und Sprachen

Die Oberfläche gibt es auf Englisch (Standard) und Deutsch (ADR 0005). Diese Datei ist die Arbeitsanleitung.

## Kurz

- Jeder Text, den der Nutzer sieht, steht in einer Ressource: `Resources/<Bereich>Text.resx` (Englisch) und `Resources/<Bereich>Text.de.resx` (Deutsch), im Core unter `src/RepoBoard.Core/Resources/`, in der UI unter `src/RepoBoard.UI/Resources/`.
- Der Build erzeugt daraus eine Klasse `<Bereich>Text` (Namespace `RepoBoard.Core.Resources` bzw. `RepoBoard.UI.Resources`) mit einer Eigenschaft je Schlüssel. Ein fehlender Schlüssel ist ein Compilerfehler (`Directory.Build.targets`).
- Neuer Text = Schlüssel in **beiden** Dateien; `<comment>` in der englischen Datei für jeden Platzhalter. `ResourceTests` (Core, UI) prüfen Schlüssel, leere Texte, Platzhalter und Kommentare.
- Die App setzt beim Start nur `CultureInfo.CurrentUICulture` (`--lang=en|de`, sonst die Sprache von Windows); Zahlen und Daten formatieren wie in Windows.
- Die Tests laufen mit deutscher UI-Kultur (`tests/Shared/UiCulture.cs`).
- Die statische Seite `src/RepoBoard.App/wwwroot/index.html` (Fehlerleiste von Blazor) ist nur Englisch.

## Bereiche

| Projekt | Datei | Inhalt |
|---|---|---|
| Core | `SyncText` | Git und Board: Namen der Operationen, Meldungen im Aktivitätsprotokoll, Gründe für einen übersprungenen Auto-Pull, Scan, git fehlt/Zeitlimit |
| Core | `CoreText` | eigene Dateien: Konfiguration nicht lesbar, neuer, kaputt, nicht gespeichert; Datei kein UTF-8 |
| UI | `BoardText` | die Seite: Kopfzeile, Filter, Tabelle, Zeile, Details, Aktivität, relative Zeiten, Startmeldungen des Hosts |
| UI | `UpdateText` | Updates (WP-10): Knopf in der Kopfzeile, Update-Dialog, Abschnitt in den Einstellungen, Benachrichtigung |

Ein neuer Bereich bekommt eine eigene Datei, wenn er eine eigene Ansicht ist (z. B. `SettingsText` mit einer Einstellungsseite).

## Glossar

| Englisch | Deutsch |
|---|---|
| repository | Repository (kurz in Knöpfen: Repo) |
| branch, commit, fetch, pull, push, stage, upstream, rebase, merge | bleiben englisch |
| up to date | aktuell |
| ahead / behind | voraus / hinterher |
| auto-fetch, auto-pull, auto-sync | Auto-Fetch, Auto-Pull, Auto-Sync |
| activity | Aktivität |
| diverged | auseinandergelaufen |
