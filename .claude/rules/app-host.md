---
paths:
  - "src/RepoBoard.App/**"
  - "src/RepoBoard.Core/CommandLine.cs"
  - "src/RepoBoard.Core/AppPaths.cs"
  - "src/RepoBoard.Core/Autostart.cs"
---

# WPF-Host (App)

Aus FerretSharp übernommen; die Blazor-Fallen im WebView2 stehen in `ui.md`.

- **Eigener Einstieg** `Program.Main`: zuerst Velopack (WP-10, ADR 0007: seine Hooks starten die App mit eigenen Argumenten und enden dort; `SetAutoApplyOnStartup(false)`, sonst installiert ein Start einen Download ungefragt), dann UI-Kultur synchron, dann die Instanzsperre (eine Instanz je Datenordner – zwei würden dieselben Repos gleichzeitig fetchen), dann WPF. `--data-dir`, `--lang`, `--theme` über `CommandLine`.
- **DPI:** der Host ist per `app.manifest` PerMonitorV2-aware; ohne wird das Fenster auf Monitoren mit anderer Skalierung unscharf und `<select>`-Popups falsch groß.
- Theme: `WindowTheme` setzt Titelleiste (DWM), Fensterhintergrund und `PreferredColorScheme` der WebView; der Hintergrund der WebView steht vor ihrem Erzeugen fest (`WEBVIEW2_DEFAULT_BACKGROUND_COLOR`, kein weißes Aufblitzen).
- Dienste werden beim Beenden auf einem Pool-Thread gestoppt und entsorgt (`App.OnExit` → `Task.Run`): an den Dispatcher gebundene Ereignisse dort nicht abmelden, kein `Invoke` – der UI-Thread wartet auf genau diesen Task.
- **Das Bild ist eine Aufnahme** (`WebView2CompositionControl` → geteilte D3D-Textur → `D3DImage`): `WebViewCapture` wartet nach jeder Kopie auf die GPU (`CopyFence`) und hält ein Bild zurück, solange das `D3DImage` gesperrt ist (ADR 0009). „Anzeige hinkt“ oder „steht bis zur nächsten Bewegung“ zuerst hier vermuten, messen mit `tools/e2e/Measure-PictureDelay.ps1`; vor dem Heben von WebView2/BlazorWebView die Prüfliste in ADR 0009. Neustart der Aufnahme nach Ruhezustand/RDP: aus FerretSharp ADR 0038 übernehmen (Backlog).
- Programme startet der Host nur mit `ArgumentList` (Explorer, Terminal, VS Code) und einem Pfad vom Board. Ein Programm, das seine Argumente selbst zerlegt, bekommt den Pfad nicht als Argument: Windows Terminal liest `;` als Befehlstrenner, es bekommt den Ordner als Arbeitsordner (`-d .`, Review 0.4.0, A8); `cmd /c code` lehnt Pfade mit Shell-Zeichen ab und sagt, wenn `code` fehlt. Die Prüfungen stehen testbar in `UI/State/DesktopPaths`.
- `async void`-Handler im Host (Fenster aktiviert) fangen alles selbst und loggen – eine Ausnahme dort beendet sonst den Prozess bzw. landet im Dispatcher.

## Infobereich (WP-02, ADR 0006)
- `UseWindowsForms` nur für `NotifyIcon`: die impliziten Usings `System.Windows.Forms`/`System.Drawing` sind entfernt (sonst sind `Application`, `MessageBox` … mehrdeutig), WinForms-Typen über den Alias `Forms.`. `WFO0003` (DPI-Modus im Manifest) ist abgeschaltet: der Prozess ist WPF.
- `TrayIcon` entsteht und stirbt auf dem UI-Thread, **nicht** im DI-Container: der Host wird auf einem Pool-Thread entsorgt, das Symbol gehört einem versteckten Fenster des UI-Threads und bliebe sonst im Infobereich stehen (`App.OnExit`).
- `ShutdownMode=OnExplicitShutdown`: das Fenster zu schließen beendet nichts. Wer beenden will, ruft `ExitApp` (setzt `_exiting`, damit `Closing` nicht wieder versteckt).
- Ein verstecktes Fenster hat kein `Process.MainWindowHandle`: ein zweiter Start erreicht die laufende Instanz nur über `InstanceSignal`. Sie hört erst, wenn das Board geladen ist – der zweite Start versucht es daher bis zu 10 s (Review 0.4.0, A11); läuft sie erhöht, darf er ihr Ereignis nicht öffnen und sagt „läuft bereits“ (A33). `CloseMainWindow` von außen versteckt nur – Testinstanzen über `--exit` beenden.
- Ein Klick auf einen Balloon-Tip kommt nur als Popup an, nicht aus dem Benachrichtigungscenter.

## Updates (WP-10, ADR 0007)
- Der Updater startet erst, wenn die App wirklich endet: `AppExit.Exiting()` steht in `OnExit` **nach** dem Stoppen des Hosts. `WaitExitThenApplyUpdates` wartet bis 60 s auf das Prozessende; `ApplyUpdatesAndRestart` würde sofort beenden, ohne aufzuräumen.
- Die neue Version bekommt `CommandLine.Restart(args)`: volle Pfade (der Updater startet in einem anderen Arbeitsordner, FerretSharp A44), ohne `--minimized`/`--exit`. Eine neue Option mit Pfad gehört in `CommandLine.PathOptions`.
- Ein Build ist für Velopack nicht installiert (`IsInstalled` false): nichts wird geprüft. Updates nur mit einer gepackten Testversion prüfen (`docs/e2e-testing.md` › Updates).
