---
paths:
  - "tests/**"
---

# Tests

Aus FerretSharp übernommen, soweit es hier gilt; Git-Tests: `git.md`.

- **xUnit v3** auf der **Microsoft Testing Platform** + NSubstitute. Kein VSTest: `Microsoft.NET.Test.Sdk` und `xunit.runner.visualstudio` nicht verwenden.
- Helfer in `tests/Shared` (Namespace `RepoBoard.Testing`, in jedes Testprojekt verlinkt): `TestFolder`, `GitSandbox`, `Wait`, `UiCulture`, `ResourceChecks`, `Repository`. Ordner nur über `TestFolder` anlegen und löschen, nie selbst rekursiv löschen (`SafeDeleteTests` durchsucht auch die Tests).
- Die Tests laufen mit deutscher UI-Kultur (`UiCulture`), englische Texte mit `using (UiCulture.Use("en")) { … }`. Zahlen in Texten (`{0:N0}`) folgen dagegen der Kultur der Maschine: die erwartete Zahl mit `ToString("N0", CultureInfo.CurrentCulture)` bauen (FerretSharp WP-59, CI rot). `ResourceTests` (Core, UI) prüfen beide Sprachen je Schlüssel.
- **Die CI testet zuerst unter Linux**, lokal läuft nur Windows: Pfade in Erwartungen über `Path.Combine`, nie mit `\` oder Laufwerksbuchstaben; Dateinamen sind dort case-sensitiv; Sperren über `FileShare` sind unter Linux nur beratend; kein Test hängt von Reihenfolge oder Zustand eines anderen ab. Vor dem Push `pwsh -NoProfile -File tools/linux/Test-Linux.ps1`.
- Zeit über `TimeProvider` (`FakeTimeProvider`), nicht mit festen `Task.Delay` abwarten. Auf etwas warten, das ein anderer Thread wahr macht: `Wait.UntilAsync(…, what)`.
- Testbare UI-Logik liegt in `UI/State` und wird ohne Rendern geprüft (`RepoBoard.UI.Tests`); Tests liegen im Ordner des Bereichs, den sie prüfen.

## Redundante Tests (aus FerretSharp, Review 3.54.0/3.70.0)
Jeder Test kostet Laufzeit und Pflege; ein neuer Test muss einen Fehler fangen, den kein anderer fängt.
- Gleicher Aufruf, andere Eingabe → ein Fall mehr im bestehenden `[Theory]`, kein neuer `[Fact]`. Der Name sagt die Regel.
- Kein Test für Sprache oder Bibliothek (Record-Gleichheit, Setter, Standardverhalten von `System.Text.Json`) und keiner, dessen Assert nicht scheitern kann.
- Die Standardwerte der Einstellungen sind Entscheidungen des Nutzers und stehen in **einem** Test (`ConfigStoreTests.A_missing_file_gives_the_decided_defaults`).
- Vor dem Zusammenlegen jeden Assert der alten Tests abhaken; vor dem Entfernen prüfen, dass der verbleibende Test scheitert, wenn der Code kaputt ist (kurz brechen, laufen lassen, zurück).
- Nie weg: Tests, die einen Review-Befund oder Bugfix festhalten, und die Invarianten-Tests (`ArchitectureTests`, `SafeDeleteTests`, `ResourceTests`, die Auto-Sync-Fälle aus ADR 0003).
