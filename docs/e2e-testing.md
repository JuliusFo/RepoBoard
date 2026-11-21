# UI end-to-end prüfen

Vor jedem E2E-Lauf lesen. Abläufe und Fallen aus FerretSharp (`docs/e2e-testing.md` dort, mit vielen Details zu Ports, mehreren Instanzen und FACEIT).

## Nie echte Daten
- App nur mit `--data-dir=<scratch>` starten – sonst arbeitet sie auf `%APPDATA%\RepoBoard\config.json` des Nutzers.
- Repos für den Test im Scratch-Ordner anlegen (Bare-Remote plus Clones wie `tests/Shared/GitSandbox.cs`, mit `-c user.name=… -c user.email=…`). **Nie** Fetch, Pull, Push, Commit, Stage oder Branchwechsel in den echten Repos des Nutzers unter `C:\Dev-Repos` – auch nicht über die App.
- Auto-Fetch/Auto-Pull nur für Test-Repos einschalten.

## Testinstanz
- `tools/e2e/Start-TestInstance.ps1 -Root <scratch>\e2e` baut nach `<Root>\app` und startet mit eigenem Daten- und WebView2-Ordner, Debug-Port 9333 (prüft, dass er frei ist und danach der eigenen Instanz gehört) und fester Fensterposition 100,100 / 1440×900. Danach eine `config.json` mit den Test-Repos in den Datenordner legen und neu starten, oder über „Repo hinzufügen“ (nativer Dialog – nicht per CDP bedienbar) – einfacher: die Datei vor dem Start schreiben (Format: `docs/architecture.md` 1.4, Pfade per JSON-Bibliothek, nicht von Hand mit `\`).
- `node tools/e2e/cdp.mjs 9333 eval <datei.js>` liest die Seite, `screenshot` nimmt ein Bild.
- `tools/e2e/Stop-TestInstance.ps1 -Root … -ProcessId …` beendet nur diese Instanz (über `RepoBoard.exe --exit`: Schließen versteckt seit WP-02 nur) und zählt `[ERR]`/`[WRN]` im Log.
- PowerShell unterscheidet bei Variablen nicht zwischen Groß und Klein: eine Variable `$root` im Skript überschreibt den Parameter `$Root` (Review WP-02, `Invoke-TrayIcon.ps1`).
- Mit `--minimized` entsteht keine WebView, bis das Fenster gezeigt wird: `Start-TestInstance.ps1` wartet auf die Seite und scheitert dann – selbst starten (Umgebung `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`/`WEBVIEW2_USER_DATA_FOLDER` wie im Skript) und mit einem zweiten Start zeigen.

## Infobereich und Benachrichtigungen (WP-02)
- Das Symbol ohne Maus: `tools/e2e/Invoke-TrayIcon.ps1 -Click menu -Item '<Eintrag>'` bzw. `-Click left` – die Rückrufnachricht von Windows an das versteckte Fenster des `NotifyIcon`, dann das Menü über UI Automation. Fenster schließen wie das X: `WM_CLOSE` per `PostMessage` an das Fenster mit Titel „RepoBoard“ (eine Nachricht, keine eingeschleuste Eingabe).
- Benachrichtigungen sieht nur der Nutzer: ihn bitten hinzusehen und **aufs Popup** zu klicken (im Benachrichtigungscenter wirkt der Klick nicht), die Wirkung per CDP lesen (`.row.repo.open`).
- Autostart schreibt in die echte Registry: eine Testinstanz (`--data-dir`) bekommt den eigenen Wert `RepoBoard (<id>)`, nie `RepoBoard`. Nach dem Test wieder ausschalten und nachsehen, dass der Wert weg ist.

## Updates (WP-10)
- Testversionen nie mit der Pack-Id `RepoBoard` und nie per `Setup.exe` (installiert nach `%LocalAppData%\RepoBoard` und startet ohne `--data-dir` auf den echten Daten): `dotnet publish src/RepoBoard.App -c Release -r win-x64 --self-contained -p:Version=0.1.90 -o <scratch>\publish-0.1.90`, dann `dotnet vpk pack --packId RepoBoardE2E --packVersion 0.1.90 … --outputDir <scratch>\feed` (nach `dotnet tool restore`), ebenso eine höhere Version in denselben Feed.
- Das Portable-Zip im Feed ist immer das der zuletzt gepackten Version: die alte Version in einen eigenen Ordner packen und deren Zip nach `<Root>\app` entpacken. Starten mit `Start-TestInstance.ps1 -NoBuild -Exe current\RepoBoard.exe -AppArgs '--update-source=<scratch>\feed'`.
- Nach „Jetzt neu starten“ startet der Updater die neue Version mit denselben Argumenten und derselben Umgebung (Debug-Port) – die Prozess-Id ändert sich.
- Ein Debug-Port bleibt nach dem Beenden manchmal kurz belegt: `-Port` mit einem anderen Port.
- Die Benachrichtigung kommt nur, wenn das Fenster nicht aktiv ist und die Version neuer als `updateNotified` in `config.json` ist: vor dem Start dort zurücksetzen, mit `--minimized` selbst starten, den Nutzer aufs Popup klicken lassen.

## Echte Eingabe
- Drei Stufen: DOM-Events, CDP-Eingabe (am WPF-Host vorbei), Windows-Eingabe per `SendInput` (wie beim Nutzer). Mausinteraktionen mit Stufe 3 prüfen: `tools/e2e/Invoke-RealClick.ps1` (prüft, dass das Fenster vorn und unter dem Punkt liegt).
- Vorher `tools/e2e/Test-InjectedInput.ps1`: filtert der Rechner eingeschleuste Eingaben (Anti-Cheat-Treiber FACEIT nach einer Spielsitzung des Nutzers), den Nutzer klicken lassen und die Wirkung per CDP lesen – nicht weiterklicken.
- Screenshots per `PrintWindow`/`CopyFromScreen` enthalten die Titelleiste: Klickziele immer per `getBoundingClientRect` bestimmen.
- Holt Windows das Fenster nicht nach vorn, landet ein Klick im Fenster darüber: vorher prüfen, abbrechen statt weiterklicken. **Nur `Invoke-RealClick.ps1` klicken lassen, keine eigenen Klickskripte** – es prüft Vordergrund und Fenster unter dem Punkt. Am 2026-11-20 (WP-01) landete ein selbst geschriebener Klick ohne diese Prüfung im Spiel des Nutzers, das im Vordergrund lag.
- Bilder ohne Eingabe: `PrintWindow` mit `PW_RENDERFULLCONTENT` (2) nimmt das Fenster auch verdeckt auf; `CopyFromScreen` nimmt, was oben liegt.
- **Was im Fenster ankommt, nicht nur in der Seite:** CDP-Screenshots kommen von Chromiums Oberfläche und zeigen nie, ob WPF das Bild präsentiert hat. Verzögerte oder verlorene Bilder (ADR 0009) misst `tools/e2e/Measure-PictureDelay.ps1` am Fensterbild (Hover; mit `-Click` Klicks, die ungefilterte Eingabe brauchen). Während der Messung die Maus nicht anfassen.
