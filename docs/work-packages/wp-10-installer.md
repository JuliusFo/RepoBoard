# WP-10 Installer und Updates

Branch `wp/10-installer`, 2026-11-20, noch ohne Release.

## Planung (aus `docs/roadmap.md`)
- Velopack wie FerretSharp (ADR 0024 dort): `Program.Main` ruft Velopack zuerst, Release-Workflow auf Tag (Version gegen `Directory.Build.props`), Setup und Portable, Update-Prüfung mit Dialog, Notizen aus dem CHANGELOG.
- App-Icon (`tools/icon` wie FerretSharp).
- Offen war: automatische Update-Prüfung beim Start oder nur auf Klick?

## Entscheidungen des Nutzers (2026-11-20)
- **Automatisch nach Updates suchen, wie FerretSharp** (dort WP-32 und Nachträge): beim Start und während RepoBoard läuft (Nie / Stündlich / Alle 4 Stunden (Standard) / Täglich), abschaltbar, „Jetzt prüfen“; nur melden, Herunterladen und „Jetzt neu starten“ erst nach Klick; eine gescheiterte Prüfung ist nur ein Hinweis in den Einstellungen; vor dem Herunterladen noch einmal prüfen; Quelle GitHub-Releases ohne Token.
- **Eine Windows-Benachrichtigung je neuer Version** (Rückfrage beim Plan, weil RepoBoard oft nur im Infobereich läuft und den Hinweis im Fenster dann niemand sieht).
- Den Plan hat der Nutzer mit dieser Antwort freigegeben. Das App-Icon (WP-02 hat schon eins) wird hier nicht überarbeitet – bleibt im Backlog.

## Vorschläge (nicht bestätigt)
- Technik nach ADR 0007: Velopack 1.2.161, Pack-Id `RepoBoard`, unsigniert; Assets `RepoBoard-win-Setup.exe`, `RepoBoard-win-Portable.zip`.
- Anzeige in der Kopfzeile statt einer Statusleiste (RepoBoard hat keine), ein Abschnitt „Updates“ im Einstellungsmenü.
- Die Benachrichtigung je Version wird über Starts hinweg gemerkt (`updateNotified`), nur neuere Versionen werden gemeldet; nicht über dem aktiven Fenster und nicht mit „Benachrichtigungen: Aus“ – die Version gilt dann trotzdem als gemeldet.
- „Jetzt neu starten“ beendet ohne Rückfrage (RepoBoard hat nichts Ungesichertes) wie „Beenden“ im Infobereich; die neue Version startet mit Fenster (ohne `--minimized`).
- Der Deinstaller entfernt die Autostart-Einträge des Programms.

## Gebaut
- **Core:** `AppSettings.CheckForUpdates`, `UpdateCheckHours` (0/1/4/24, Standard 4, beim Lesen bereinigt), `UpdateNotified` – Version von `config.json` bleibt 1. `CommandLine.UpdateSource` (`--update-source=`) und `CommandLine.Restart` (volle Pfade, ohne `--minimized`/`--exit`). `Autostart.Starts` (gehört ein Run-Wert zu diesem Programm). `ProjectSite` (GitHub-Adresse).
- **UI:** aus FerretSharp `IAppUpdater`/`AppUpdate`/`VersionNotes`, `UpdateState` (alle Nachträge: Hintergrundprüfung, stille Prüfung bei bekanntem Update, Prüfung vor dem Download), `ReleaseNotes` (ohne Bilder), `ReleaseVersion`. Neu: `UpdateState.Announced` (einmal je Version), `AppExit` (statt `ExitGuard`), `AppVersion`. Komponenten `Components/Update/`: `UpdateIndicator` (Kopfzeile), `UpdateDialog` (Modal), `UpdateSettings` (im `SettingsMenu`). Texte in `UpdateText` (en/de).
- **Host:** `Program.Main` ruft Velopack zuerst (`SetAutoApplyOnStartup(false)`, vor dem Deinstallieren `RegistryAutostart.RemoveAllOf`). `VelopackUpdater` (GitHub ohne Token oder `--update-source`). `App`: `UpdateState` nach dem Start des Hosts, Benachrichtigung über `TrayIcon.Notify` (nimmt jetzt eine Aktion für den Klick statt eines Repo-Pfads), `AppExit.Requested` → Beenden, `AppExit.Exiting` nach dem Stoppen des Hosts.
- **Release:** `.github/workflows/release.yml` (Tag `v*` oder von Hand), `vpk` in `dotnet-tools.json`, Velopack in `Directory.Packages.props`.
- **Werkzeuge:** `Start-TestInstance.ps1 -Exe current\RepoBoard.exe` startet eine Portable-Testversion aus `<Root>\app`.

## Geprüft
- `dotnet build -warnaserror` und `dotnet test`: 192 Tests grün (120 Core, 72 UI).
- E2E (2026-11-20, alles im Scratchpad): Testversionen 0.1.90/0.1.91/0.1.92 mit eigener Pack-Id `RepoBoardE2E` (`vpk pack` lokal), Portable-Zip von 0.1.90 nach `<Root>\app`, eigener Daten- und WebView2-Ordner, Feed per `--update-source`. Eingeschleuste Eingaben kamen durch (`Test-InjectedInput.ps1`), Klicks mit `Invoke-RealClick.ps1`.
  - Prüfung beim Start → „Update 0.1.91 verfügbar“ in der Kopfzeile, `updateNotified` = 0.1.91 gespeichert.
  - Klick → Dialog mit Notizen (Absatz, Überschriften, Aufzählung mit Fortsetzungszeile, Inline-Code, Link als Text), Hinweis „nur die Notizen von 0.1.91“ (Portable hat kein lokales volles Paket), Bild.
  - „Herunterladen“ → App bleibt offen, „Version 0.1.91 ist heruntergeladen …“, Kopfzeile „Neu starten für 0.1.91“. „Jetzt neu starten“ → App endet sauber, nach ~2 s läuft 0.1.91 mit denselben Argumenten (volle Pfade).
  - Einstellungen › Updates in 0.1.91: Version, „Installiert …“, Schalter, Intervall, „RepoBoard ist aktuell (geprüft um …)“; „Jetzt prüfen“ aktualisiert die Zeit. „Automatisch prüfen“ aus → Intervall gesperrt, `checkForUpdates: false` gespeichert, an → frei.
  - Kaputter Feed → nichts in der Kopfzeile, in den Einstellungen rot „Prüfung auf Updates nicht möglich: …“ (dunkel lesbar).
  - Fenster beim Start aktiv → keine Benachrichtigung (gewollt), die Version gilt als gemeldet.
  - Logs ohne `[ERR]`/`[WRN]`.
- Nicht geprüft: die Benachrichtigung auf dem Bildschirm und der Klick aufs Popup (Fenster mit Dialog) – der Nutzer war nicht am Rechner, Test steht aus (Ablauf: `docs/e2e-testing.md` › Updates); `Setup.exe` (Installation nach `%LocalAppData%`, Verknüpfungen, SmartScreen, Deinstallation mit dem Autostart-Hook – das ginge nur auf den echten Ordnern des Nutzers), der Release-Workflow (erst mit dem ersten Tag), der Link „Alle Releases auf GitHub“.

## Offen
- Die Kopfzeile passt bei 1440 px Fensterbreite schon ohne den Update-Knopf nicht ganz (Einstellungsknopf am Rand abgeschnitten); mit dem Knopf fehlen ~200 px – Backlog.
- Signieren (SmartScreen) wie in FerretSharp: Backlog.
