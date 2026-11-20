---
paths:
  - "src/RepoBoard.App/**"
---

# WPF-Host (App)

Aus FerretSharp übernommen; die Blazor-Fallen im WebView2 stehen in `ui.md`.

- **Eigener Einstieg** `Program.Main`: UI-Kultur synchron, dann die Instanzsperre (eine Instanz je Datenordner – zwei würden dieselben Repos gleichzeitig fetchen), dann WPF. `--data-dir`, `--lang`, `--theme` über `CommandLine`.
- **DPI:** der Host ist per `app.manifest` PerMonitorV2-aware; ohne wird das Fenster auf Monitoren mit anderer Skalierung unscharf und `<select>`-Popups falsch groß.
- Theme: `WindowTheme` setzt Titelleiste (DWM), Fensterhintergrund und `PreferredColorScheme` der WebView; der Hintergrund der WebView steht vor ihrem Erzeugen fest (`WEBVIEW2_DEFAULT_BACKGROUND_COLOR`, kein weißes Aufblitzen).
- Dienste werden beim Beenden auf einem Pool-Thread gestoppt und entsorgt (`App.OnExit` → `Task.Run`): an den Dispatcher gebundene Ereignisse dort nicht abmelden, kein `Invoke` – der UI-Thread wartet auf genau diesen Task.
- **Das Bild ist eine Aufnahme** (`WebView2CompositionControl`): Steht die Anzeige nach Ruhezustand, Dock-Wechsel oder RDP, liegt es am Aufnahmeweg, nicht an Blazor. FerretSharp hat dafür `WebViewCapture`, `CopyFence` und ein WebView2-Pin (ADR 0038/0039 dort); RepoBoard hat nur das Pin (`Directory.Packages.props`). Tritt es auf: dort nachsehen, nicht neu erfinden (Backlog).
- Programme startet der Host nur mit `ArgumentList` (Explorer, Terminal, VS Code) und einem Pfad vom Board; `cmd /c code` lehnt Pfade mit Shell-Zeichen ab (`WpfDesktop.OpenInEditor`).
- `async void`-Handler im Host (Fenster aktiviert) fangen alles selbst und loggen – eine Ausnahme dort beendet sonst den Prozess bzw. landet im Dispatcher.
