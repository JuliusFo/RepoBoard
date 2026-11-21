---
paths:
  - "src/RepoBoard.App/**"
---

# WPF-Host (App)

Aus FerretSharp übernommen; die Blazor-Fallen im WebView2 stehen in `ui.md`.

- **Eigener Einstieg** `Program.Main`: zuerst Velopack (WP-10, ADR 0007: seine Hooks starten die App mit eigenen Argumenten und enden dort; `SetAutoApplyOnStartup(false)`, sonst installiert ein Start einen Download ungefragt), dann UI-Kultur synchron, dann die Instanzsperre (eine Instanz je Datenordner – zwei würden dieselben Repos gleichzeitig fetchen), dann WPF. `--data-dir`, `--lang`, `--theme` über `CommandLine`.
- **DPI:** der Host ist per `app.manifest` PerMonitorV2-aware; ohne wird das Fenster auf Monitoren mit anderer Skalierung unscharf und `<select>`-Popups falsch groß.
- Theme: `WindowTheme` setzt Titelleiste (DWM), Fensterhintergrund und `PreferredColorScheme` der WebView; der Hintergrund der WebView steht vor ihrem Erzeugen fest (`WEBVIEW2_DEFAULT_BACKGROUND_COLOR`, kein weißes Aufblitzen).
- Dienste werden beim Beenden auf einem Pool-Thread gestoppt und entsorgt (`App.OnExit` → `Task.Run`): an den Dispatcher gebundene Ereignisse dort nicht abmelden, kein `Invoke` – der UI-Thread wartet auf genau diesen Task.
- **Das Bild ist eine Aufnahme** (`WebView2CompositionControl`): Es kopiert jedes Bild des Browsers per D3D11 in eine geteilte Textur, die WPF über D3D9 präsentiert, und die Aufnahme liefert nur bei einer Änderung der Seite ein Bild. Zeigt WPF eines nicht, bleibt es unsichtbar bis zur nächsten Änderung – Hover und Klicks erschienen verspätet oder erst mit der nächsten Mausbewegung (2026-11-21). `WebViewCapture` ersetzt deshalb den Bild-Handler des Controls: wartet nach jeder Kopie auf die GPU (`CopyFence`) und hält ein Bild zurück, solange das `D3DImage` gesperrt ist, statt es zu verwerfen – per Reflection auf private Member, WebView2 dafür transitiv gepinnt (ADR 0009, aus FerretSharp ADR 0038/0039). „Anzeige hinkt“ oder „steht bis zur nächsten Bewegung“ also zuerst hier vermuten, nicht bei Blazor; messen mit `tools/e2e/Measure-PictureDelay.ps1`. Beim Heben von WebView2/BlazorWebView: `RepoBoard.App.Tests` grün, „Frames of the WebView's capture taken over“ (Debug) im Log, keine Warnung „cannot be held/waited for“, Messskript gegen den Stand vorher. Steht die Anzeige nach Ruhezustand, Dock-Wechsel oder RDP ganz: FerretSharp startet die Aufnahme dann neu (ADR 0038 dort, mit Nachträgen) – hier noch nicht übernommen (Backlog); von dort übernehmen, nicht neu erfinden.
- Programme startet der Host nur mit `ArgumentList` (Explorer, Terminal, VS Code) und einem Pfad vom Board; `cmd /c code` lehnt Pfade mit Shell-Zeichen ab (`WpfDesktop.OpenInEditor`).
- `async void`-Handler im Host (Fenster aktiviert) fangen alles selbst und loggen – eine Ausnahme dort beendet sonst den Prozess bzw. landet im Dispatcher.

## Infobereich (WP-02, ADR 0006)
- `UseWindowsForms` nur für `NotifyIcon`: die impliziten Usings `System.Windows.Forms`/`System.Drawing` sind entfernt (sonst sind `Application`, `MessageBox` … mehrdeutig), WinForms-Typen über den Alias `Forms.`. `WFO0003` (DPI-Modus im Manifest) ist abgeschaltet: der Prozess ist WPF.
- `TrayIcon` entsteht und stirbt auf dem UI-Thread, **nicht** im DI-Container: der Host wird auf einem Pool-Thread entsorgt, das Symbol gehört einem versteckten Fenster des UI-Threads und bliebe sonst im Infobereich stehen (`App.OnExit`).
- `ShutdownMode=OnExplicitShutdown`: das Fenster zu schließen beendet nichts. Wer beenden will, ruft `ExitApp` (setzt `_exiting`, damit `Closing` nicht wieder versteckt).
- Ein verstecktes Fenster hat kein `Process.MainWindowHandle`: `RunningInstance` findet es nicht, darum `InstanceSignal`. `CloseMainWindow` von außen versteckt nur – Testinstanzen über `--exit` beenden.
- Ein Klick auf einen Balloon-Tip kommt nur als Popup an, nicht aus dem Benachrichtigungscenter.

## Updates (WP-10, ADR 0007)
- Der Updater startet erst, wenn die App wirklich endet: `AppExit.Exiting()` steht in `OnExit` **nach** dem Stoppen des Hosts. `WaitExitThenApplyUpdates` wartet bis 60 s auf das Prozessende; `ApplyUpdatesAndRestart` würde sofort beenden, ohne aufzuräumen.
- Die neue Version bekommt `CommandLine.Restart(args)`: volle Pfade (der Updater startet in einem anderen Arbeitsordner, FerretSharp A44), ohne `--minimized`/`--exit`. Eine neue Option mit Pfad gehört in `CommandLine.PathOptions`.
- Ein Build ist für Velopack nicht installiert (`IsInstalled` false): nichts wird geprüft. Updates nur mit einer gepackten Testversion prüfen (`docs/e2e-testing.md` › Updates).
