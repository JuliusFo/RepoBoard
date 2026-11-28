# RepoBoard – alle Git-Repos auf einer Seite

> Projektanweisungen für Claude Code. Arbeitssprache mit dem Nutzer: **Deutsch**. Code, Kommentare und Commit-Messages: **Englisch**.
> Release-Stand und Historie: die Tags `vX.Y.Z` und `CHANGELOG.md`. Aufbau, Regeln und Skills stammen aus FerretSharp (`C:\Dev-Repos\FerretSharp`); dort gelernte Fallen stehen hier mit Herkunft („FerretSharp WP-NN“).

## Wo steht was

Diese Datei lädt in jede Session und enthält nur, was immer gilt. Die Fallen eines Bereichs stehen in `.claude/rules/` und laden, sobald eine passende Datei gelesen oder bearbeitet wird (`paths` im Kopf jeder Regel). Neue Fallen dort eintragen, wo sie gebraucht werden; eine neue Regel nur mit gültigem `paths`-Kopf (ohne lädt sie immer).

| Datei | Lesen … |
|---|---|
| `docs/architecture.md` – Bausteine, Git-Weg, Auto-Sync, UI-Konzept | vor Änderungen am jeweiligen Bereich |
| `docs/work-packages/` – Index mit Bereichen, je Paket ein Protokoll mit den Entscheidungen des Nutzers | bevor ein Bereich angefasst wird: im Index suchen, dann nur diese Datei |
| `docs/roadmap.md` (Geplantes), `docs/backlog.md` (Ideen) | beim Start eines Pakets; neue Ideen in den Backlog |
| `docs/release.md` – CI, Versionierung | vor Änderungen an CI/Version und vor jedem Release |
| `docs/e2e-testing.md`, `docs/localization.md` | vor E2E-Läufen, vor neuen Texten |
| `docs/decisions/` – ADRs | bei Stack- oder Architekturänderungen |

## 1. Ziel

Ein eigener Git-Client wie GitHub Desktop, aber für **viele Repos gleichzeitig**: eine Seite mit allen Repos des Nutzers, ihrem aktuellen Branch, Ahead/Behind und lokalen Änderungen; die üblichen Git-Aktionen (Fetch, Pull, Push, Commit, Branch wechseln); dazu **Auto-Fetch und Auto-Pull je Repo**, ein- und ausschaltbar, damit ausgewählte Repos immer aktuell sind (Wunsch des Nutzers, 2026-11-20). Funktionen im Überblick: `README.md`.

Der Nutzer ist erfahrener .NET-Entwickler (Visual Studio, Blazor). Erklärungen auf Senior-Niveau, keine Grundlagen.

## 2. Invarianten

**Lokale Arbeit geht nie verloren (ADR 0003):**
- RepoBoard führt nichts aus, was ungesicherte Arbeit verwerfen kann: kein `reset --hard`, `clean`, `checkout -- <pfad>`, `restore` auf den Worktree, `stash drop`, `push --force`, `branch -D`. Kommt so etwas dazu (z. B. „Änderungen verwerfen“), bekommt es eine eigene enge Methode in `GitClient`, eine Rückfrage in der UI, einen ADR und Tests. Bisher so: `--abort` und `--skip` einer angehaltenen Operation (ADR 0014, Schlüssel und Dateiliste der Rückfrage im Tor geprüft).
- **Auto-Pull ist nur ein Fast-Forward** (`merge --ff-only @{u}` nach dem Fetch) und nur, wenn `PullCheck` nichts findet: kein detached HEAD, Upstream vorhanden, kein laufendes Merge/Rebase/Cherry-Pick/Revert, keine Konflikte, nichts lokal voraus. Lokale Änderungen bleiben; überschneiden sie sich mit dem Pull, bricht git ab.
- Was im Hintergrund läuft, fragt nie nach Zugangsdaten (`GitCallMode.Background`: `GCM_INTERACTIVE=never`, `GIT_TERMINAL_PROMPT=0`); es scheitert und meldet das einmal.

**Git nur über die CLI und nur an einer Stelle (ADR 0002):**
- Prozesse startet nur `GitRunner` (Zeitlimit, `ArgumentList`, keine Shell) und der Host für Explorer, Terminal und Editor; `ArchitectureTests` hält das fest. Jeder Befehlstext steht in `GitClient`.
- Werte von außen (Pfade, Branch-Namen) gehen nach `--` oder werden vorher geprüft (`GitClient.IsBranchNameAsync`: kein führendes `-`, `git check-ref-format --branch`) – sonst wäre ein Name eine Option.
- Die Repos sind Ordner des Nutzers: RepoBoard schreibt dort nie selbst Dateien, nur git auf ausdrücklichen Klick oder über Auto-Sync.

**Eigene Dateien:** Konfiguration nur über `ConfigStore` (schreiben atomar über `AtomicJsonFile`, lesen über `VersionedJsonFile`). Eine Datei, die diese Version nicht lesen kann (neuer, gesperrt), wird nie überschrieben; eine kaputte bleibt als Kopie liegen.

**Versionierung:** SemVer, `CHANGELOG.md` pflegen, Tag `vX.Y.Z` je Release – erst taggen, wenn der CHANGELOG-Abschnitt steht (`docs/release.md`). Features späterer Pakete nicht vorziehen, sondern in `docs/backlog.md` notieren.

## 3. Stack (entschieden – Änderungen nur per ADR in `docs/decisions/`)

Gleicher Stack wie FerretSharp (Entscheidung des Nutzers, 2026-11-20; ADR 0001). Paketversionen in `Directory.Packages.props` mit FerretSharp zusammen heben.

| Bereich | Entscheidung | Hinweise |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# 14 | SDK per `global.json` (`rollForward: latestFeature`). |
| Solution | **`RepoBoard.slnx`**, Central Package Management | Repo-lokales `nuget.config` (nur nuget.org, Source Mapping: `build-release.md`). |
| UI | **Blazor Hybrid**: WPF-Host mit `BlazorWebView`, Komponenten in einer Razor Class Library | App-TFM **`net10.0-windows10.0.19041.0`** (mit `net10.0-windows` stürzt BlazorWebView beim Start ab). |
| Theme, UI-Zustand | eigenes CSS mit Design-Tokens (`repoboard.css`, aus FerretSharp); Komponenten + schlanke State-Klassen | **Nicht verwenden:** WPF-UI, CommunityToolkit.Mvvm, CSS-Frameworks, MVVM-Frameworks. |
| Git | **git-CLI** des Nutzers über `GitRunner` (ADR 0002) | Kein libgit2/LibGit2Sharp: Credential Manager, SSH, Hooks und LFS wie im Terminal. |
| Hosting, Logging | `Microsoft.Extensions.Hosting`; **Serilog** (Datei unter `%APPDATA%\RepoBoard\logs`) | Die BlazorWebView nutzt den Service Provider des Hosts. |
| Icons | **Lucide 1.55** als Pfade in `Icons.cs` | Kein Paket, keine Icon-Schrift; neue Icons aus derselben Version. |
| Tests | **xUnit v3** auf **Microsoft Testing Platform** + NSubstitute | Kein VSTest. Git-Tests laufen gegen echte Repos im Temp-Ordner (`GitSandbox`). |
| Persistenz | JSON (`System.Text.Json`): `AtomicJsonFile` / `VersionedJsonFile` | Keine `object`-Properties. |
| Texte | typisierte resx, Englisch (Standard) und Deutsch (ADR 0005) | `docs/localization.md`. |
| CI | GitHub Actions (Linux zuerst, dann Windows) | `docs/release.md`. Installer/Updates (Velopack wie FerretSharp): Roadmap. |

## 4. Solution-Struktur

```
src/RepoBoard.Core/   net10.0 – reine Logik, KEIN WPF, KEINE Windows-only-APIs, baut unter Linux
  Git/ (GitRunner, GitClient, RepoStatus) Repos/ (Konfiguration, Scanner) Board/ (BoardService, RepoState, ActivityLog)
  Sync/ (AutoSyncService, PullCheck) Watch/ (RepoWatchService, WatchMap) IO/ (aus FerretSharp) Resources/
src/RepoBoard.UI/     net10.0 Razor Class Library, plattformneutral: Shell.razor, Components/ (Ordner = Namespace),
                      State/ (Namespace `UI.State`, testbare Logik, Host-Schnittstellen), Resources/, wwwroot/
src/RepoBoard.App/    net10.0-windows10.0.19041.0 – schlanker WPF-Host (Program.Main, Generic Host, Serilog, Host-Dienste)
tests/                Core.Tests, UI.Tests (State ohne Rendern), App.Tests (nur Windows: WebView-Aufnahme), Shared/ (TestFolder, GitSandbox, …)
tools/                linux/ (Build und Tests im Linux-Container wie die CI), e2e/ (Testinstanz, echte Klicks, CDP)
```

Regeln:
- `RepoBoard.Core` und `RepoBoard.UI` dürfen **kein** `System.Windows` und keine Windows-only-APIs referenzieren. Was nur der Host kann (Ordnerdialog, Explorer, Terminal, Fenster), definiert UI/Core als Interface (`IDesktop`); die Implementierung liegt in `RepoBoard.App`.
- Komponenten rufen git nie selbst, sondern über `BoardService`.

## 5. Kernbegriffe (Details: `docs/architecture.md`)

Repo (`RepoEntry` in der Konfiguration, `RepoState` zur Laufzeit), Status (`RepoStatus` aus `git status --porcelain=v2`), Board (`BoardService`: Liste, Operationen, ein Tor je Repo, höchstens vier Netzwerkoperationen gleichzeitig), Dateiwächter (`RepoWatchService`: Status gleich nach einer Änderung im Repo, ADR 0011), Auto-Sync (`AutoSyncService` alle 20 s: Status der Repos ohne Wächter – beobachtete alle 2 min –, dann fällige Fetches/Fast-Forwards), Aktivität (`ActivityLog`, was im Hintergrund geschah).

## 6. Arbeitsweise

- **Pakete:** eigener Branch `wp/NN-kurzname` (kleinere Themen `feat/…`, `fix/…`; reine Doku-Commits direkt auf `main`); neue Arbeit immer von `main` abzweigen. Welches Paket als Nächstes kommt, entscheidet der Nutzer – keine Reihenfolge planen, nur Abhängigkeiten festhalten. Vor dem Start den Plan nennen, dann umsetzen. Ein Paket zu Ende bringen, bevor andere Themen angefasst werden; Ideen unterwegs nach `docs/backlog.md`. Ist unklar, **welches** Element eine Bitte meint, einmal kurz nachfragen; ist nur unklar, **wie** es aussehen soll, die einfachste Variante bauen und als Frage notieren.
- **Entschieden ist nur, was der Nutzer entschieden hat.** Regeln, Verbote und Festlegungen, die Claude selbst vorschlägt, stehen in Doku, Regeln und ADRs als „Vorschlag (nicht bestätigt)“, bis der Nutzer zustimmt.
- **Fertig heißt:** `dotnet build -warnaserror` und `dotnet test` grün, jeder sichtbare Text in Englisch und Deutsch, eine sichtbare Funktion im `README.md`, ADR bei nicht-trivialen Entscheidungen, Protokoll in `docs/work-packages/` (eigene Datei, Zeile im Index), Eintrag im `CHANGELOG.md`. Danach eine kurze Zusammenfassung auf Deutsch (was gebaut, was offen, was zu testen).
- **Commits** klein und thematisch, Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`). **PR-Titel und -Beschreibung** auf Englisch, Paket in Klammern am Ende: `feat: tray icon and autostart (WP-02)`.
- **Core zuerst mit Tests, dann UI.** Testbare Logik der UI gehört in eine reine Klasse unter `UI/State`.
- **Ordner rekursiv löschen nur über `SafeDelete.DirectoryBelow(root, path)`**, in Tests über `TestFolder`; keine `Remove-Item -Recurse`/`rm -r` in Skripten (`SafeDeleteTests` sucht danach).
- **UI end-to-end prüfen, ohne echte Nutzerdaten anzufassen** (`docs/e2e-testing.md`): App nur mit `--data-dir=<scratch>`, Test-Repos nur im Scratch-Ordner – **nie** Fetch, Pull, Push oder Commit in den echten Repos des Nutzers unter `C:\Dev-Repos`. Mausinteraktionen mit echter Windows-Eingabe prüfen, nicht nur per CDP.

## 7. Kontext des Nutzers

- Viele Repos unter `C:\Dev-Repos` (u. a. FerretSharp mit vielen `.claude/worktrees`), Git for Windows mit Credential Manager.
- Arbeitet oft mit mehreren Claude-Sessions parallel in Worktrees.
