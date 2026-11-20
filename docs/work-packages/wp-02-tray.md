# WP-02 Infobereich, Autostart, Benachrichtigungen

Branch `wp/02-tray`, 2026-11-20, noch ohne Release.

## Planung (aus `docs/roadmap.md`)
- Fenster schließen → Symbol im Infobereich, Auto-Sync läuft weiter (ADR 0003, Konsequenzen: „immer aktuell“ gilt sonst nur bei offenem Fenster). Menü: Öffnen, Alle fetchen, Auto-Sync pausieren, Beenden.
- Autostart mit Windows (Eintrag unter `HKCU\…\Run` oder Verknüpfung im Autostart-Ordner), schaltbar.
- Windows-Benachrichtigung bei Auto-Pull und Fehlern; Klick öffnet das Repo im Fenster.
- Offen waren: Schließen = in den Infobereich oder beenden (oder fragen)? Benachrichtigung bei jedem Auto-Pull oder nur bei Fehlern/übersprungenen? Autostart minimiert? Technik für das Symbol (ADR).

## Entscheidungen des Nutzers (2026-11-20)
- Schließen schickt RepoBoard **in den Infobereich** (Standard; umschaltbar), beim ersten Mal mit Hinweis.
- Benachrichtigungen standardmäßig bei **Auto-Pulls und Problemen**, gebündelt je Durchlauf, nur wenn das Fenster nicht vorn ist; umschaltbar auf „nur Probleme“ und „aus“.
- Autostart startet **nur im Infobereich**.
- „Auto-Sync pausieren“ gilt **nur bis zum Neustart**.
- Den Plan hat der Nutzer mit diesen Antworten freigegeben.

## Vorschläge (nicht bestätigt)
- Technik nach ADR 0006: WinForms `NotifyIcon`, Balloon-Tips als Benachrichtigungen, Run-Schlüssel statt Verknüpfung, benannte Ereignisse für den zweiten Start.
- `RepoBoard.exe --exit` beendet die Instanz eines Datenordners (für das Stop-Skript nötig, später für einen Installer).
- Einstellungen als Menü hinter einem Knopf in der Kopfzeile, dort auch der Pause-Schalter; Programmsymbol „vier Kacheln“ in der Akzentfarbe (`tools/icon/New-AppIcon.ps1`).
- „Neue Commits“ aus Auto-Fetch ohne Auto-Pull melden nicht (das Board zeigt sie); gemeldet werden Auto-Pulls, Fehler und übersprungene Auto-Pulls.

## Gebaut
- **Core:** `AppSettings` in `config.json` (`settings`: `closeToTray`, `notifications`, `trayHintShown`; Version bleibt 1; fehlend oder eine ungültige Zahl → Standard, ein unbekannter Text macht die Datei wie bei den übrigen Feldern unlesbar). `ActivityEntry.Kind` (Info, Error, Pulled, NewCommits, Skipped) und `RepoPath`, `ActivityLog.Added`. `BoardService.UpdateSettingsAsync`, `SetAutoSyncPaused`/`AutoSyncPaused`. `SyncNotifier` (Auswahl nach Einstellung, eine Meldung je Auto-Sync-Durchlauf – außerhalb eines Durchlaufs nach 2 s –, Probleme zuerst, Grenzen von Windows), `BoardService.AutoSyncRunning`/`AutoSyncRunEnded`, `FetchAllAsync`. `Autostart` (Wertname, Befehlszeile mit Quoting nach `CommandLineToArgvW`, Abschaltung des Task-Managers), `AppPaths.Id`/`IsDefault`, `CommandLine.Minimized`/`Exit`/`Has`.
- **UI:** `SettingsMenu` (Schließen, Autostart, Pause, Benachrichtigungen), Hinweis „Auto-Sync pausiert · Fortsetzen“ in der Kopfzeile, `BoardNavigation` (Zeile öffnen und per Fokus in den Blick holen, auch wenn die Seite erst später entsteht), `TrayStatus` (Tooltip), `IAutostart`. Icons `Settings`, `Resume` aus FerretSharps `Icons.cs`.
- **Host:** `TrayIcon` (NotifyIcon, Menü, Tooltip, Balloon-Tips, Klick → Fenster/Repo), `RegistryAutostart`, `InstanceSignal` (show/exit), `ShutdownMode=OnExplicitShutdown`, Schließen → verstecken, `--minimized`, `--exit`; Programmsymbol `RepoBoard.ico`.
- **Werkzeuge:** `tools/icon/New-AppIcon.ps1`; `tools/e2e/Invoke-TrayIcon.ps1`; `Stop-TestInstance.ps1` beendet über `--exit`; `Start-TestInstance.ps1` prüft den Port über den WebView2-Kindprozess, wenn Windows keinen Listener nennt.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test`: 137 Tests grün (118 Core, 19 UI).
- E2E (2026-11-20, Testinstanz mit `--data-dir` im Scratchpad, `--lang=de`, drei Test-Repos dort mit Bare-Remote). Eingeschleuste Eingaben wurden gefiltert (`Test-InjectedInput.ps1`), daher CDP, Fenster-Nachrichten, UI Automation und Klicks des Nutzers:
  - Einstellungsmenü (Bild), Pause per Schalter: Hinweis, Aktivität; in der Pause 75 s lang kein Fetch/Pull trotz neuem Commit; „Fortsetzen“ → beide Auto-Pull-Repos gepullt.
  - `WM_CLOSE` versteckt das Fenster, der Prozess läuft weiter, `trayHintShown` gespeichert.
  - Benachrichtigungen (Nutzer): Hinweis, „Auto-Sync: 2 Repositories“, „mine – 1 Commit gepullt (main)“. Klick aufs Popup → Fenster vorn, Zeile „mine“ offen und fokussiert. Klick im Benachrichtigungscenter → nichts (siehe Offen).
  - Zweiter Start bei verstecktem Fenster → Fenster sichtbar, zweiter Prozess endet mit 0.
  - Autostart an → `RepoBoard (<id>)` mit `--minimized` und `--data-dir`, aus → Wert weg; der Eintrag `RepoBoard` des Nutzers blieb unberührt.
  - Tray (Nachricht + UI Automation): Menü mit den vier Einträgen, „Auto-Sync pausieren“ wirkt, Linksklick zeigt das Fenster, „Beenden“ beendet sauber.
  - `--minimized`: kein Fenster, keine WebView, Tray-Menü da; zweiter Start zeigt Fenster und Seite. `Stop-TestInstance.ps1` beendet über `--exit`. Log ohne `[ERR]`/`[WRN]`.
- Nicht geprüft: Tooltip des Symbols auf dem Bildschirm (nur `TrayStatusTests`), ein echter Start bei der Windows-Anmeldung, der Task-Manager-Schalter.

## Offen
- Klick auf eine Benachrichtigung im Benachrichtigungscenter öffnet nichts (Balloon-Tips melden Klicks nur als Popup) – Backlog.
- Weitere Ideen: `docs/backlog.md` › Infobereich und Benachrichtigungen.
