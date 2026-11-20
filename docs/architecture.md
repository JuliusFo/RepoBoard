# Architektur

Wie RepoBoard aufgebaut ist und warum. Stack und Schichten: `CLAUDE.md` §3–4; Entscheidungen: `docs/decisions/`.

## 1. Bausteine

```
Shell.razor ──► BoardService ──► GitClient ──► GitRunner ──► git.exe (CLI des Nutzers)
   ▲   │            │  ▲
   │   │            │  └── AutoSyncService (BackgroundService, alle 20 s)
   │   │            └────► ConfigStore ──► %APPDATA%\RepoBoard\config.json
   │   └──► IDesktop (App: Ordnerdialog, Explorer, Terminal, VS Code), IAutostart (App: Run-Schlüssel)
   └── Changed (beliebiger Thread) → ein Rendern je Schub

ActivityLog.Added ──► SyncNotifier (eine Meldung je Durchlauf) ──► TrayIcon (App: NotifyIcon, Balloon-Tip)
TrayIcon / InstanceSignal ──► Fenster zeigen, BoardNavigation.Open(repo) ──► Shell öffnet die Zeile

UpdateState ──► IAppUpdater (App: VelopackUpdater ──► GitHub-Releases) ── Announced ──► TrayIcon (einmal je Version)
   └── Restart ──► AppExit ──► App beendet sich, danach startet der Updater die neue Version (ADR 0007)
```

### 1.1 Git-Weg (ADR 0002)
- `GitRunner` startet git mit Zeitlimit und ohne Shell; `GitCallMode.Background` verbietet Anmeldefenster.
- `GitClient` kennt jeden Befehl und liest nur maschinenlesbare Ausgaben: `RepoStatus.Parse` (`status --porcelain=v2 --branch -z --untracked-files=all`), `ParseBranches` (`for-each-ref`), `ParseLog` (`log --format` mit `%x1f`/`%x1e`).
- Aus dem Git-Ordner direkt gelesen: `FETCH_HEAD` (Zeit des letzten Fetch, auch aus IDE/Terminal) und die Marker laufender Operationen (`MERGE_HEAD`, `rebase-merge`, `rebase-apply`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`).

### 1.2 Board
- `BoardService` hält die Liste (`ImmutableList<RepoState>`, nach Name sortiert), speichert über `ConfigStore` und führt jede Operation aus: ein Tor je Repo, höchstens vier Netzwerkoperationen gleichzeitig. Nach jeder Operation liest es den Status neu und schreibt einen Eintrag ins `ActivityLog`.
- `RepoState` ist der Laufzeitzustand eines Repos (Status, Fehler, laufende Operation, letzter Fetch, Details für die geöffnete Zeile). Properties werden nur als Ganzes ersetzt.
- Details (Branches, letzte 15 Commits) liest das Board nur für geöffnete Zeilen (`WantsDetails`).

### 1.3 Auto-Sync (ADR 0003)
- `AutoSyncService` ruft alle 20 s `RefreshAllAsync` (Status aller Repos, bis acht parallel) und `AutoSyncDueAsync` (fällige Repos; nichts, solange `AutoSyncPaused` – nur zur Laufzeit, ADR 0006).
- Je Repo: `fetch --all --prune` im Hintergrundmodus, dann bei Auto-Pull `PullCheck.Blocker` und ggf. `merge --ff-only @{u}`.

### 1.4 Konfiguration
- `config.json` (Version 1): `repos` mit `path`, `autoFetch`, `autoPull`; `autoSyncMinutes`; `settings` (`AppSettings`: `closeToTray`, `notifications`, `trayHintShown`, ADR 0006; `checkForUpdates`, `updateCheckHours`, `updateNotified`, ADR 0007). Autostart steht nicht darin, sondern im Run-Schlüssel der Registry. Beim Lesen bereinigt (null, leere oder relative Pfade, doppelte, Intervall außerhalb der Auswahl). Neuere oder unlesbare Datei: nichts wird gespeichert, bis zum Neustart; kaputte Datei: Kopie `config.json.bad-<Zeit>`, Liste beginnt leer.

## 2. UI-Konzept

Eine Seite (Wunsch des Nutzers: „Alles übersichtlich auf einer Seite“):
- **Kopfzeile:** Suche (Name, Pfad, Branch), Filter-Chips mit Zahl (Alle, Änderungen, Hinterher, Voraus, Auto-Sync, Probleme), ggf. „Auto-Sync pausiert · Fortsetzen“, Intervall, Status neu lesen, Alle fetchen, Ordner scannen, Repo hinzufügen, Einstellungen (Menü: im Infobereich weiterlaufen, mit Windows starten, Auto-Sync pausieren, Benachrichtigungen; Updates). Gibt es eine neuere Version, steht vor dem Intervall ein Knopf („Update … verfügbar“, Fortschritt, „Neu starten für …“), der den Update-Dialog öffnet.
- **Tabelle:** je Repo Name und Pfad, Branch (Hauptbranches grau, Feature-Branches farbig, detached markiert, laufende Operation), Sync (↑ voraus, ↓ hinterher, „aktuell“, „kein Upstream“, Warnzeichen bei übersprungenem Auto-Pull), Änderungen (`!` Konflikte, `+` gestaged, `~` geändert, `?` neu), letzter Fetch (älter als ein Tag: Warnfarbe), Schalter Auto-Fetch und Auto-Pull, Aktionen (Fetch, Pull, Push – hervorgehoben, wenn sinnvoll –, Explorer, Terminal, VS Code, entfernen). Ein Fehler steht als Zeile darunter, bis er ausgeblendet oder durch einen Erfolg ersetzt wird.
- **Geöffnete Zeile:** Änderungen mit Häkchen (stagen/unstagen), Commit bzw. „Alle committen“, Commit und Push; Branches (lokal, dann nur entfernte) mit Wechseln und Anlegen; letzte Commits mit Refs.
- **Aktivität** unten: zu die neueste Meldung, offen die Liste (neueste zuerst, „auto“ markiert).
- Beim Zurückwechseln ins Fenster liest RepoBoard den Status sofort neu (höchstens alle 5 s).
- Ein Klick auf eine Benachrichtigung zu einem Repo setzt den Filter auf „Alle“, leert die Suche, öffnet die Zeile und holt sie per Fokus in den Blick.

## 3. Host
- `Program.Main`: Velopack (Hooks für Installieren/Update/Deinstallieren, ADR 0007), UI-Kultur, Instanzsperre je Datenordner (ein zweiter Start setzt das Ereignis `show` der laufenden Instanz, `--exit` deren `exit`, `InstanceSignal`), dann WPF.
- `App`: Serilog nach `<Datenordner>\logs`, Generic Host ohne Standards, `BoardService` laden, Infobereich (`TrayIcon`), `SyncNotifier`, `InstanceSignal`, Fenster zeigen (nicht mit `--minimized`), `AutoSyncService` starten, dann die Update-Prüfung (`UpdateState.StartAsync`).
- **Infobereich (ADR 0006):** `ShutdownMode=OnExplicitShutdown`; Schließen versteckt das Fenster (Einstellung), Beenden über Menü, `--exit` oder Sitzungsende. Benachrichtigungen nur, wenn das Fenster nicht sichtbar und aktiv ist.
- Optionen: `--data-dir=`, `--lang=en|de`, `--theme=dark|light`, `--minimized`, `--exit`, `--update-source=` (nur Tests, ADR 0007).
