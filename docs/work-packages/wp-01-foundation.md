# WP-01 Fundament: Projekt nach dem Muster von FerretSharp, Spike übernommen

Ordner `C:\Dev-Repos\RepoBoard`, 2026-11-20, noch ohne Release (0.1.0).

## Anlass (Gespräch mit dem Nutzer, 2026-11-20)
- Wunsch: ein Git-Tool wie GitHub Desktop (aktueller Branch, Commit, Pull, Fetch …), dazu eine direkte Übersicht über alle Repos und ihre Branches, Auto-Fetch/Auto-Pull für bestimmte Repos zum Ein- und Ausschalten, „alles übersichtlich auf einer Seite“. Erst nach bestehenden Tools suchen, sonst ein Spike „mit C# und dem selben UI-Stack wie FerretSharp“.
- Recherche: kein Tool deckt alles ab (ADR 0001, Kontext). Spike in einem Scratch-Ordner gebaut, mit den Repos des Nutzers (nur Status gelesen) angesehen.
- Nutzer: „Der Spike sieht richtig gut aus!“; danach die Ideenliste (jetzt WP-02 … WP-24 in `docs/roadmap.md`), dann: neues Projekt im normalen Projektordner „mit der Projektstruktur von FerretSharp inklusive Regeln, Claude-Skills, etc.“, „direkt sauber aufbauen und das Gelernte aus FS übernehmen“.

## Entscheidungen des Nutzers
- Stack und Aufbau wie FerretSharp (ADR 0001).
- Auto-Fetch und Auto-Pull je Repo, ein- und ausschaltbar; alles auf einer Seite.

## Vorschläge (nicht bestätigt)
- Name **RepoBoard** (aus dem Spike).
- Git über die CLI (ADR 0002); Auto-Pull nur als Fast-Forward mit den Regeln aus ADR 0003; ein Intervall für alle (Standard 5 min), Takt 20 s.
- Sprache der Oberfläche folgt Windows (ADR 0005).
- Versionen ab 0.1.0 (ADR 0004).

## Gebaut
- **Aus FerretSharp übernommen:** `.editorconfig`, `global.json`, `nuget.config` (Source Mapping wegen der DevExpress-Quelle), `Directory.Build.props/.targets` (typisierte resx), Central Package Management mit denselben Versionen, `.gitattributes`, `.gitignore`; `Core/IO` (`SafeDelete`, `AtomicFile`, `AtomicJsonFile`, `VersionedJsonFile`, `JsonFiles`, `InstanceLock`), `TextFormat`, `RunningInstance`, `WindowTheme` (ohne Einstellung); Test-Helfer, `SafeDeleteTests`, `ResourceChecks`; `tools/linux`, `tools/e2e`; Skills und Regeln, angepasst.
- **Aus dem Spike, überarbeitet:** `GitRunner` (git fehlt → Meldung), `GitClient` (+ `IsBranchNameAsync`, `OperationIn`, `LastFetchIn`), `RepoStatus`; `BoardService` (vorher `RepoBoardService`), `RepoState` (laufende Operation als Enum), `ActivityLog`, `PullCheck` (aus dem Service gelöst), `AutoSyncService`, `RepoScanner`; `ConfigStore` jetzt versioniert und ohne Überschreiben unlesbarer Dateien. UI in `Components/Board`, `Components/Common`, testbare Logik in `UI/State` (`BoardFilter`, `RelativeTime`, `IDesktop`); alle Texte in `SyncText`, `CoreText`, `BoardText` (en/de); CSS mit gemischten Flächen statt `rgba`.
- **Host:** eigener `Program.Main` (UI-Kultur, Instanzsperre je Datenordner, `--data-dir`, `--lang`, `--theme`), Serilog im Datenordner, Generic Host ohne Standards, Ausnahme-Handler, Status beim Aktivieren des Fensters.
- **Neu gegenüber dem Spike:** Branch-Namen werden vor `git switch` geprüft (ein Name `-d` wäre eine Option gewesen); `cmd /c code` lehnt Pfade mit Shell-Zeichen ab; `ArchitectureTests` (keine WPF-Referenz im Core, Prozesse nur in `GitRunner` und Host); Git-Tests isoliert von der Git-Konfiguration der Maschine (`GitSandbox`).
- **CI:** Linux (Core, UI, Tests mit Coverage), Windows (Solution, Tests), Zeilenenden; Dependabot für Actions.

## Geprüft
- `dotnet build -warnaserror` (Solution) und `dotnet test` grün: 81 Core-Tests, 17 UI-Tests.
- E2E (2026-11-20): App mit `--data-dir` im Scratchpad, `--lang=en --theme=dark`, drei Test-Repos dort (aktuell, Feature-Branch ohne Upstream mit Änderungen, ohne Commit). Übersicht, Zähler der Chips, Auto-Fetch beim Start, dunkle Titelleiste und englische Texte per `PrintWindow` geprüft; Log ohne `[ERR]`/`[WRN]`. Die geöffnete Zeile nicht: der Klick dafür landete im Vordergrundfenster des Nutzers (Lehre in `docs/e2e-testing.md`), danach keine Eingaben mehr.
- Nicht geprüft: `tools/linux/Test-Linux.ps1` (Docker) und die CI (noch kein GitHub-Repo).

## Offen
- Siehe `docs/backlog.md` › Bekannte Lücken; nächste Pakete: `docs/roadmap.md`.
