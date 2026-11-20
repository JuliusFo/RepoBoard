# ADR 0006: Infobereich, Autostart und Benachrichtigungen

- Status: teilweise entschieden. **Entscheidungen des Nutzers (2026-11-20, WP-02):** Schließen schickt RepoBoard in den Infobereich (umschaltbar); Benachrichtigungen standardmäßig bei Auto-Pulls und Problemen (umschaltbar: nur Probleme, aus); Autostart startet nur im Infobereich; „Auto-Sync pausieren“ gilt nur bis zum Neustart. **Vorschlag (nicht bestätigt):** die Technik unten (NotifyIcon, Balloon-Tips, Run-Schlüssel, benannte Ereignisse, `--exit`).
- Datum: 2026-11-20

## Kontext

„Immer aktuell“ (ADR 0003) gilt nur, solange RepoBoard läuft. Ohne Infobereich endet Auto-Sync mit dem Fenster; ohne Autostart nach jeder Anmeldung; ohne Benachrichtigung sieht niemand, was im Hintergrund passiert oder scheitert. WPF hat kein Symbol für den Infobereich und keine Benachrichtigungen.

## Entscheidung

- **Symbol:** WinForms `NotifyIcon` im WPF-Host (`UseWindowsForms` nur in `RepoBoard.App`; die impliziten WinForms-Usings sind entfernt, WinForms-Typen werden mit `Forms.` benannt). Statt `Shell_NotifyIcon` per P/Invoke: Menü (`ContextMenuStrip`), Tooltip, Klicks und Balloon-Tips sind fertig und erprobt; der Preis ist eine Assembly-Referenz im Host, nicht in Core/UI (`ArchitectureTests`). Menü: RepoBoard öffnen (fett, auch Linksklick), Alle fetchen, Auto-Sync pausieren (Häkchen), Beenden. Der Tooltip zählt Repos, hinterher und mit Problemen (`TrayStatus`, höchstens 127 Zeichen).
- **Schließen:** `ShutdownMode=OnExplicitShutdown`; `Closing` versteckt das Fenster, wenn `AppSettings.CloseToTray` an ist, beim ersten Mal mit einem Hinweis (`TrayHintShown`). Beenden über das Menü, `--exit` oder das Ende der Windows-Sitzung.
- **Benachrichtigungen:** `NotifyIcon.ShowBalloonTip`; Windows 10/11 zeigt sie als Toast – ohne Paket, AUMID oder Startmenü-Verknüpfung. Was gemeldet wird, entscheidet `SyncNotifier` (Core) an `ActivityEntry.Kind`, nie am Text: nur Auto-Sync-Einträge, je nach `AppSettings.Notifications`. Was in 2 s zusammenkommt (ein Takt), wird **eine** Meldung, Probleme zuerst, höchstens vier Zeilen; derselbe Fehler kommt schon im Aktivitätsprotokoll nur einmal. Nicht über dem sichtbaren, aktiven Fenster – dort steht es in der Aktivität. Ein Klick auf das Popup öffnet das Fenster und – bei genau einem Repo – dessen Zeile (`BoardNavigation`).
- **Autostart:** Wert unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` mit `"<exe>" --minimized` (ohne Adminrechte, im Task-Manager unter „Autostart-Apps“). Der Eintrag ist die Wahrheit, nicht `config.json`: an heißt vorhanden und nicht im Task-Manager abgeschaltet (`StartupApproved\Run`, erstes Byte ungerade = aus). Einschalten schreibt das aktuelle Programm und löscht eine Abschaltung des Task-Managers; ausschalten löscht nur den eigenen Wert. Ein anderer Datenordner (`--data-dir`) bekommt einen eigenen Wertnamen `RepoBoard (<id>)` und den Ordner in der Befehlszeile – eine Testinstanz überschreibt nie den Eintrag des Nutzers.
- **Zweiter Start, `--exit`:** Ein verstecktes Fenster hat kein `MainWindowHandle`, `RunningInstance` findet es nicht. Die laufende Instanz wartet daher auf benannte Ereignisse `Local\RepoBoard-<id>-show` und `-exit` (`InstanceSignal`, `<id>` = `AppPaths.Id`, Hash des Datenordners). Ein zweiter Start setzt `show` (vorher `AllowSetForegroundWindow` für die laufende Instanz), `RepoBoard.exe --exit` setzt `exit` – für E2E und später einen Installer, der das Programm ersetzt.
- **Pause:** `BoardService.AutoSyncPaused` überspringt `AutoSyncDueAsync`, der Status wird weiter gelesen und Klicks funktionieren; nur zur Laufzeit, ein Hinweis in der Kopfzeile mit „Fortsetzen“.

## Konsequenzen

- Ein Klick auf eine Benachrichtigung **im Benachrichtigungscenter** tut nichts (E2E, 2026-11-20): Windows meldet Balloon-Klicks nur, solange das Popup sichtbar ist. Abhilfe wären echte Toasts mit eigener AUMID und COM-Aktivierung (Backlog).
- Mit `--minimized` entsteht das Fenster samt WebView erst beim ersten Zeigen: Speicher und Start bleiben klein, Auto-Sync läuft trotzdem.
- `tools/e2e/Stop-TestInstance.ps1` beendet über `--exit` (Schließen versteckt nur); `tools/e2e/Invoke-TrayIcon.ps1` bedient das Symbol ohne eingeschleuste Eingaben.
