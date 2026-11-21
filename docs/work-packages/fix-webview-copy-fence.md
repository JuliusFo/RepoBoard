# Fix: Klicks und Hover erscheinen verspätet (`fix/webview-copy-fence`)

Beobachtung des Nutzers beim E2E von WP-05 (2026-11-21, Testinstanz 1440×900): Klicks und CSS-Hover zeigen sich spürbar verspätet. Ursache wie in FerretSharp `fix/typing-delay` (ADR 0039 dort): WPF präsentiert die geteilte Textur der WebView-Aufnahme, bevor die GPU die Kopie fertig hat. ADR 0008.

## Messung
- Aufbau: zwei Testinstanzen (`--data-dir`), Ausgangsstand `main` (0.1.0) und dieser Branch, je 8 Test-Repos im Scratch-Ordner (Bare-Remote plus Clones), Fenster 1440×900, DPR 1. `tools/e2e/Measure-PictureDelay.ps1` bewegt die Maus über `Invoke-RealClick.ps1` abwechselnd auf Zeile 1 und 5 (Hover) bzw. klickt Zeile 1 auf und zu (`-Click`) und misst am Fensterbild (`PrintWindow` mit `PW_RENDERFULLCONTENT`, alle 16,7 ms), wann die Änderung sichtbar ist; „verloren“ heißt: bis zur nächsten Bewegung bzw. zum nächsten Klick (0,9–1,4 s) nicht sichtbar.
- Eingeschleuste Klicks waren zuerst gefiltert (Nutzer im Spiel, FACEIT), nach dem Spiel kamen sie durch.

| | Hover verloren | Hover Median / max | Klick verloren | Klick > 100 ms | Klick max |
|---|---|---|---|---|---|
| vorher (`main`) | 33 von 60 | 22 / 41 ms | 19 von 70 | 11 (160–220 ms) | 220 ms |
| nachher | 0 von 60 | 28–31 / 52 ms | 1 von 70 | 0 | 56 ms |

- Log der Testinstanz nachher: „Frames of the WebView's capture taken over“, keine Wartezeit ab 2 ms, kein Bild zurückgehalten, 0,1–0,9 Bilder je Sekunde (die Aufnahme liefert hier nur bei Änderungen). Keine Warnungen oder Fehler.
- Der eine verlorene Klick nachher ist nicht erklärt (kein Eintrag im Log); nicht wiederholt in einem zweiten Lauf mit 40 Klicks.

## Gebaut
- `App/Services/CopyFence` aus FerretSharp, zusätzlich mit Prüfung des Query-Werts (ADR 0008).
- `App/Services/WebViewCapture`: der Bild-Teil aus FerretSharp (Handler des Bildpuffers ersetzt, Bild zurückhalten bei gesperrtem `D3DImage`, Warten auf die Kopie, Debug-Zeilen zu Bildern, Wartezeiten und zurückgehaltenen Bildern). Nicht übernommen: Neustart der Aufnahme nach Ruhezustand/Sitzungswechsel/Front Buffer, Software-Anzeige über RDP (Backlog).
- `tests/RepoBoard.App.Tests` (Windows): `CopyFence` gegen ein WARP-Gerät, die privaten WebView2-Member gegen das Pin. Gegenprobe: ein fehlender Slot in `ID3D11Device` bzw. `ID3D11DeviceContext` macht den Test rot (der zweite erst mit der Prüfung des Query-Werts).
- `tools/e2e/Measure-PictureDelay.ps1` für die Gegenprobe beim nächsten Heben von WebView2/BlazorWebView.

## Offen
- ADR 0008 ist ein Vorschlag, bis der Nutzer zustimmt.
- Einmal mit echter Maus im großen Fenster gegenprüfen (beim Nutzer, maximiert).
