# Arbeitspakete – Index

Was in den abgeschlossenen Paketen gebaut und entschieden wurde, mit Nachträgen, Fallen und E2E-Erkenntnissen: je Paket eine Datei. Vor Änderungen an einem Bereich das Paket lesen, das ihn gebaut hat – die Spalte „Bereiche“ führt vom Code zum Paket. Geplante Pakete: `docs/roadmap.md`; übergreifende Fallen: `.claude/rules/`, `docs/e2e-testing.md`. Begründungen: `docs/decisions/`.

## Versionen

| Version | Inhalt | Pakete |
|---|---|---|
| **0.x – Fundament** | Übersicht aller Repos, Git-Grundaktionen, Auto-Fetch/Auto-Pull je Repo | WP-01 … |

## Übersicht

Reihenfolge wie die Releases.

| Paket | Datei | Inhalt | Bereiche | Release | ADR |
|---|---|---|---|---|---|
| WP-01 | [wp-01-foundation.md](wp-01-foundation.md) | Projekt nach dem Muster von FerretSharp, Spike übernommen: Übersicht, Fetch/Pull/Push, Stage/Commit, Branches, Auto-Fetch/Auto-Pull, Aktivität | alles (`Core/Git`, `Core/Board`, `Core/Sync`, `Core/Repos`, `UI`, `App`) | 0.1.0 | 0001–0005 |
| WP-02 | [wp-02-tray.md](wp-02-tray.md) | Infobereich (Schließen versteckt, Menü), Benachrichtigungen bei Auto-Sync, Autostart, Pause, Einstellungsmenü, Programmsymbol, `--minimized`/`--exit` | `App/Services` (`TrayIcon`, `RegistryAutostart`, `InstanceSignal`), `Core/Sync/SyncNotifier`, `Core/Autostart`, `Repos/BoardConfig` (`AppSettings`), `UI/Components/Board/SettingsMenu`, `UI/State` (`BoardNavigation`, `TrayStatus`) | 0.1.0 | 0006 |
| WP-10 | [wp-10-installer.md](wp-10-installer.md) | Installer und Updates mit Velopack: Setup und Portable, Release-Workflow, automatische Prüfung (Start und Intervall), Hinweis in der Kopfzeile, Dialog mit Notizen, Benachrichtigung je Version, Neustart in die neue Version | `App/Program`, `App/Services/VelopackUpdater`, `UI/State` (`UpdateState`, `ReleaseNotes`, `AppExit`), `UI/Components/Update`, `Repos/BoardConfig` (`AppSettings`), `.github/workflows/release.yml` | 0.1.0 | 0007 |
| WP-05 | [wp-05-diff.md](wp-05-diff.md) | Diff-Ansicht: Klick auf eine geänderte Datei zeigt `git diff` (gestaged/ungestaged) inline in der geöffneten Zeile, Wort-Markierung, folgt der Datei beim Stagen | `Core/Git` (`FileDiff`, `DiffWords`, `GitClient.DiffAsync`), `Core/Board/BoardService.DiffAsync`, `UI/Components/Board` (`DiffPanel`, `RepoDetailsPanel`), `UI/State/DiffSelection` | – | 0008 |
