# Architektur

Wie RepoBoard aufgebaut ist und warum. Stack und Schichten: `CLAUDE.md` §3–4; Entscheidungen: `docs/decisions/`.

## 1. Bausteine

```
Shell.razor ──► BoardService ──► GitClient ──► GitRunner ──► git.exe (CLI des Nutzers)
   ▲   │            │  ▲
   │   │            │  ├── AutoSyncService (BackgroundService, alle 20 s)
   │   │            │  └── RepoWatchService (FileSystemWatcher je Wurzel ──► WatchMap ──► RefreshAsync, ADR 0011)
   │   │            └────► ConfigStore ──► %APPDATA%\RepoBoard\config.json
   │   └──► IDesktop (App: Ordnerdialog, Explorer, Terminal, VS Code), IAutostart (App: Run-Schlüssel)
   └── Changed (beliebiger Thread) → ein Rendern je Schub

ActivityLog.Added ──► SyncNotifier (eine Meldung je Durchlauf) ──► TrayIcon (App: NotifyIcon, Balloon-Tip)
TrayIcon / InstanceSignal ──► Fenster zeigen, BoardNavigation.Open(repo) ──► Shell öffnet die Zeile

UpdateState ──► IAppUpdater (App: VelopackUpdater ──► GitHub-Releases) ── Announced ──► TrayIcon (einmal je Version)
   └── Restart ──► AppExit ──► App beendet sich, danach startet der Updater die neue Version (ADR 0007)
```

### 1.1 Git-Weg (ADR 0002)
- Mindestens git 2.27 (`merge --no-autostash`; `switch` braucht 2.23). `GitRunner` startet git mit Zeitlimit und ohne Shell; `GitCallMode.Background` verbietet Anmeldefenster. Die git-Variablen des Elternprozesses (`GIT_DIR`, `GIT_INDEX_FILE` …) kommen nicht bei git an (`EnvironmentFor`).
- `GitClient` kennt jeden Befehl und liest nur maschinenlesbare Ausgaben: `RepoStatus.Parse` (`status --porcelain=v2 --branch -z --untracked-files=all`), `ParseBranches` (`for-each-ref`), `ParseLog` (`log -z --topo-order --format` mit Eltern, Felder mit NUL getrennt), `CommitDetails.Parse` (`show -s --format`, NUL-getrennt; `show --name-status -z`). Zeiten als Zahl (`%ct`, `committerdate:unix`), Ahead/Behind der Branches aus `upstream:track` als Zahlen – in der UI in ihrer Sprache. Commit-IDs gehen nur als Hex an git (`IsCommitId`, ADR 0013).
- Aus dem Git-Ordner direkt gelesen: `FETCH_HEAD` (Zeit des letzten Fetch, auch aus IDE/Terminal) und die Marker laufender Operationen (`MERGE_HEAD`, `rebase-merge`, `rebase-apply`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`).

### 1.2 Board
- `BoardService` hält die Liste (`ImmutableList<RepoState>`, nach Name sortiert), speichert über `ConfigStore` und führt jede Operation aus: ein Tor je Repo, je Repo samt Worktrees eine Netzwerkoperation (`FamilyNetwork`), insgesamt höchstens vier. Ein Ordner ohne `.git` gilt als fehlend (git suchte sonst das Repo darum herum). Nach jeder Operation liest es den Status neu und schreibt einen Eintrag ins `ActivityLog`.
- `RepoState` ist der Laufzeitzustand eines Repos (Status, Fehler, laufende Operation, letzter Fetch, Details für die geöffnete Zeile). Properties werden nur als Ganzes ersetzt.
- Details (Branches, Historie mit Graph, ob HEAD gepusht ist) liest das Board nur für geöffnete Zeilen (`WantsDetails`): `RepoState.LogLength` Commits (15, „mehr laden“ +50 bis 1 000), mit `LogAll` aller Branches; `CommitGraph` verteilt sie auf Spuren (ADR 0013). Commit-Details und deren Diffs liest es nur lesend wie den Diff (ohne Tor, auf dem Thread-Pool); Amend, Revert und Cherry-Pick prüfen im Tor unmittelbar vorher (gepusht, laufende Operation, Änderungen, in HEAD, schon übernommen).
- **Worktrees (WP-04, ADR 0010):** Mit jedem Status eines Repos liest das Board `git worktree list --porcelain -z`; jeder verknüpfte Worktree ist ein eigener `RepoState` mit `MainRepo` (in `RepoState.Worktrees`, nicht in der Konfiguration), mit eigenem Tor und eigenem Status, aber ohne Auto-Sync (die Refs teilt er mit dem Repo). `RefreshAllAsync` liest erst die Repos, dann alle Worktrees; nach Fetch, Pull und Push eines Repos auch dessen Worktrees. Nur der Hauptworktree eines Repos zeigt Worktrees; einen verknüpften Worktree (Git-Ordner mit `commondir`, Hauptworktree mit `.git`) ersetzt `AddAsync` durch sein Repo – auch einen, den der Scanner findet; Submodule bleiben, wie gewählt. Entfernen nur nach `BlockAsync` (im Dialog und unmittelbar davor) und mit den bestätigten ignorierten Dateien (ADR 0010).
- **Scan (ADR 0014):** `ScanAsync` liest die Funde ohne git (`RepoScanner.Inspect`: was die Konfiguration ausführen würde, Hauptrepo außerhalb des Ordners); der Scan-Dialog lässt anhaken, `AddScannedAsync` fügt hinzu.

### 1.3 Dateiwächter (WP-03, ADR 0011)
- `RepoWatchService` hält einen rekursiven `FileSystemWatcher` je Wurzel (`WatchMap.Roots`: Ordner und Git-Ordner aller Repos und Worktrees, die kein anderer enthält) und gleicht sie bei jeder Änderung des Boards ab, wenn sich Repos, Worktrees, Git-Ordner oder fehlende Ordner geändert haben (`WatchMap.Matches`).
- `WatchMap.Route` ordnet einen geänderten Pfad zu: Refs → Repo und alle Worktrees, `.git/worktrees/<name>` → dieser Worktree (der Ordner selbst angelegt oder entfernt → Repo), `reftable` → Repo und alle Worktrees, `config`/`info` → Repo und alle Worktrees, übriger Git-Ordner → Repo, aber nur eine Positivliste (HEAD, index, Marker laufender Operationen …; was der eigene Status mit fsmonitor oder LFS schreibt, zählt nicht), Arbeitsdateien → tiefster Repo- bzw. Worktree-Ordner; `*.lock`, `bin`, `obj`, `node_modules`, `.vs`, `.idea`, `TestResults` lösen nichts aus. Netzlaufwerke und Laufwerkswurzeln bekommen keinen Wächter.
- `ChangeDebouncer`: ein Status je Serie (750 ms Ruhe, spätestens 3 s), über `RefreshAsync`; entfällt, wenn ein Lesen nach der letzten Änderung begann (`RepoState.StatusReadAt`). `RepoState.Watched` sagt dem Takt, wer beobachtet ist.

### 1.4 Auto-Sync (ADR 0003)
- `AutoSyncService` ruft alle 20 s `RefreshStaleAsync` (Status der Repos und Worktrees ohne Wächter, der beobachteten nur alle 2 min als Rückfall; bis acht parallel) und `AutoSyncDueAsync` (fällige Repos; nichts, solange `AutoSyncPaused` – nur zur Laufzeit, ADR 0006).
- Je Repo: `fetch --all --prune` im Hintergrundmodus, dann bei Auto-Pull `PullCheck.Blocker` und ggf. `merge --ff-only --no-autostash @{u}` (Netzwerk, 3 min). Höchstens drei Repos gleichzeitig, einer der vier Netzwerkplätze bleibt für Klicks; derselbe Fehler kommt einmal (`LastAutoError`).

### 1.5 Konfiguration
- `config.json` (Version 1): `repos` mit `path`, `autoFetch`, `autoPull`; `autoSyncMinutes`; `settings` (`AppSettings`: `closeToTray`, `notifications`, `trayHintShown`, ADR 0006; `checkForUpdates`, `updateCheckHours`, `updateNotified`, ADR 0007). Autostart steht nicht darin, sondern im Run-Schlüssel der Registry. Beim Lesen bereinigt (null, leere oder relative Pfade, doppelte, Intervall außerhalb der Auswahl); Pfade werden voll gespeichert (`Paths.Full`). Neuere oder unlesbare Datei: nichts wird gespeichert, bis zum Neustart; kaputte Datei: Kopie `config.json.bad-<Zeit>`, Liste beginnt leer.

## 2. UI-Konzept

Eine Seite (Wunsch des Nutzers: „Alles übersichtlich auf einer Seite“):
- **Kopfzeile:** Suche (Name, Pfad, Branch), Filter-Chips mit Zahl (Alle, Änderungen, Hinterher, Voraus, Auto-Sync, Probleme), ggf. „Auto-Sync pausiert · Fortsetzen“, Intervall, Status neu lesen, Alle fetchen, Ordner scannen (Liste zum Anhaken mit Hinweisen, ADR 0014), Repo hinzufügen, Einstellungen (Menü: im Infobereich weiterlaufen, mit Windows starten, Auto-Sync pausieren, Benachrichtigungen; Updates). Gibt es eine neuere Version, steht vor dem Intervall ein Knopf („Update … verfügbar“, Fortschritt, „Neu starten für …“), der den Update-Dialog öffnet.
- **Tabelle:** je Repo Name und Pfad (mit Zähler der Worktrees, ein Klick klappt sie als eingerückte Unterzeilen auf; „Claude“ und „gesperrt“ als Badge, darunter „Aufräumen …“), Branch (Hauptbranches grau, Feature-Branches farbig, detached markiert, laufende Operation), Sync (↑ voraus, ↓ hinterher, „aktuell“, „kein Upstream“, Warnzeichen bei übersprungenem Auto-Pull), Änderungen (`!` Konflikte, `+` gestaged, `~` geändert, `?` neu), letzter Fetch (älter als ein Tag: Warnfarbe), Schalter Auto-Fetch und Auto-Pull, Aktionen (Fetch, Pull, Push – hervorgehoben, wenn sinnvoll –, Explorer, Terminal, VS Code, entfernen; an einem Worktree ohne Fetch und ohne Schalter, „Worktree entfernen“ statt „vom Board entfernen“). Filter und Suche gelten auch für Worktrees: passende stehen unter ihrem Repo, auch wenn dessen Gruppe zu ist. Ein Fehler steht als Zeile darunter, bis er ausgeblendet oder durch einen Erfolg ersetzt wird.
- **Geöffnete Zeile:** Änderungen mit Häkchen (stagen/unstagen), Commit bzw. „Alle committen“, Commit und Push; „Letzten Commit ändern“ (Amend, nur ungepusht); Branches (lokal, dann nur entfernte) mit Wechseln und Anlegen; Historie mit Graph und Refs, Schalter „Alle Branches“, „50 weitere laden“. Ein Klick auf einen Commit zeigt Nachricht, Autor, Eltern, Refs, Dateien und deren Diff anstelle von Branches und Historie, mit Revert bzw. Cherry-Pick (WP-09, ADR 0013). Ein Klick auf einen Dateinamen zeigt den Diff dieser Seite (gestaged/ungestaged) anstelle von Branches und Commits (WP-05, ADR 0008: `git diff`, inline, Wort-Markierung); er folgt der Datei beim Stagen und wird mit jedem neuen Status neu gelesen, Esc/X schließt ihn. Am Hunk-Kopf „Hunk stagen“ bzw. „Hunk unstagen“; ein Klick auf den linken Rand einer geänderten Zeile wählt sie (Shift+Klick: Bereich), der Knopf stagt dann nur diese Zeilen (WP-25, ADR 0012: `git apply --cached`, nur der Index).
- **Aktivität** unten: zu die neueste Meldung, offen die Liste (neueste zuerst, „auto“ markiert).
- Änderungen aus IDE und Terminal erscheinen nach etwa einer Sekunde (Dateiwächter, ADR 0011); beim Zurückwechseln ins Fenster liest RepoBoard den Status aller Repos neu (höchstens alle 5 s), ebenso „Status neu lesen“.
- Ein Klick auf eine Benachrichtigung zu einem Repo setzt den Filter auf „Alle“, leert die Suche, öffnet die Zeile und holt sie per Fokus in den Blick.

## 3. Host
- `Program.Main`: Velopack (Hooks für Installieren/Update/Deinstallieren, ADR 0007), UI-Kultur, Instanzsperre je Datenordner (ein zweiter Start setzt das Ereignis `show` der laufenden Instanz – bis zu 10 s lang, solange sie noch startet –, `--exit` deren `exit`, `InstanceSignal`), dann WPF.
- `App`: Serilog nach `<Datenordner>\logs`, Generic Host ohne Standards, `BoardService` laden, Infobereich (`TrayIcon`), `SyncNotifier`, `InstanceSignal`, Fenster zeigen (nicht mit `--minimized`), `RepoWatchService` und `AutoSyncService` starten, dann die Update-Prüfung (`UpdateState.StartAsync`).
- **Infobereich (ADR 0006):** `ShutdownMode=OnExplicitShutdown`; Schließen versteckt das Fenster (Einstellung), Beenden über Menü, `--exit` oder Sitzungsende. Benachrichtigungen nur, wenn das Fenster nicht sichtbar und aktiv ist.
- Optionen: `--data-dir=`, `--lang=en|de`, `--theme=dark|light`, `--minimized`, `--exit`, `--update-source=` (nur Tests, ADR 0007).
