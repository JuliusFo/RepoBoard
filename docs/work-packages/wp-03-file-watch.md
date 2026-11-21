# WP-03 Sofort aktualisieren (Dateiwächter)

Branch `wp/03-file-watch`, 2026-11-21, Release 0.3.0.

## Planung (aus `docs/roadmap.md`)
- `FileSystemWatcher` je Repo auf `.git/HEAD`, `.git/index`, `.git/refs`, `FETCH_HEAD` und den Worktree; Ruhezeit nach einer Änderungsserie, dann ein Status. Der 20-s-Takt bleibt als Rückfallebene (verpasste Ereignisse, Netzlaufwerke).
- Grenzen: Anzahl Watcher bei vielen Repos, `node_modules`/`bin`/`obj` im Worktree, Puffer-Überlauf → einmal neu lesen.
- Offen war: Worktree-Änderungen auch beobachten (teurer) oder nur `.git`?

## Entscheidungen des Nutzers (2026-11-21)
- **Ganzer Ordner** wird beobachtet, nicht nur `.git`: eine gespeicherte Datei erscheint sofort unter „Änderungen“.
- **Rückfall:** beobachtete Repos und Worktrees liest der Status-Takt nur alle **2 min**, Repos ohne funktionierenden Wächter weiter alle 20 s.
- **Immer an**, kein Schalter im Einstellungsmenü.
- Plan mit diesen Antworten freigegeben. Für das Code-Review: gefundene Punkte selbst bewerten, die beste Lösung umsetzen und wiederholen, bis alles passt (der Nutzer war nicht da).

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0011: Wurzeln statt eines Wächters je Repo (ein Worktree im Repo-Ordner und die Git-Ordner der Worktrees brauchen keinen eigenen), Routing nach Pfad (Refs → Repo und alle Worktrees), Ruhezeit 750 ms und höchstens 3 s, feste Liste ignorierter Ordner (`bin`, `obj`, `node_modules`, `.vs`, `.idea`, `TestResults`), neuer Versuch eines gescheiterten Wächters nach einer Minute.
- Ein neu gefundener Worktree (ohne Status) wird beim Abgleich sofort gelesen, nicht erst mit dem Takt.
- „Status neu lesen“ und das Zurückwechseln ins Fenster lesen weiter alles.

## Gebaut
- **Core:** `Watch/WatchMap` (`Of`, `Matches`, `Roots`, `Route`, `Under`, `IsCovered`), `Watch/ChangeDebouncer`, `Watch/RepoWatchService` (Hosted Service: Abgleich bei Änderungen des Boards, Wächter je Wurzel, Entprellen, Lesen über `RefreshAsync` mit höchstens acht gleichzeitig, Überlauf, Fehler und neuer Versuch); `RepoState.StatusReadAt`, `RepoState.Watched`; `BoardService.RefreshStaleAsync` und `WatchedRefresh`; `AutoSyncService` ruft `RefreshStaleAsync` statt `RefreshAllAsync`.
- **App:** `RepoWatchService` als Hosted Service vor `AutoSyncService`.
- **UI:** keine Änderung (keine neuen Texte).
- **Doku:** ADR 0011, Architektur (Bausteine, §1.3 Dateiwächter, Auto-Sync, UI-Konzept, Host), README, CHANGELOG, Regel `git.md` (lesende Aufrufe schreiben nichts; Pfade um `Watch/` ergänzt), Roadmap (WP-03 raus), Backlog (Dateiwächter, Messpunkte aktualisiert).

## Geprüft
- E2E (2026-11-21, Testinstanz mit `--data-dir` im Scratchpad; Repo `project` mit Bare-Remote, einem Worktree in `.claude/worktrees/session` und einem daneben; `GIT_TRACE` der Instanz in eine Datei, um die `git status`-Aufrufe zu zählen). Änderungen per git im Terminal, die Zeile per CDP beobachtet:
  - Neue Datei, `git add`, Commit, Branchwechsel im Repo: Zeile nach 760–820 ms geändert. Neue Datei im Claude-Worktree und im Worktree daneben: 760 ms. `git fetch` nach einem Push von `theirs`: „1 hinterher“, „gerade eben“ nach 760 ms. `git worktree add` im Repo: Zähler 2 → 3 nach 1,5 s, der neue Worktree mit Status.
  - 150 Schreibvorgänge in `bin/`, `obj/` und `node_modules/`: kein `git status`.
  - Leerlauf 150 s: 4 `git status` (ein Rückfall-Durchlauf für Repo und drei Worktrees nach 2 min) statt etwa 30 mit dem 20-s-Takt. Start: 5 für 4 Zeilen.
  - Log ohne `[ERR]`/`[WRN]`. Keine neue Mausinteraktion, daher keine echten Klicks (die Worktree-Gruppe per CDP aufgeklappt).
- Unter Linux (`Test-Linux.ps1`): grün.
- `dotnet build -warnaserror`, `dotnet test`: 316 Tests grün, nach dem Review 338.
- Nicht geprüft: Netzlaufwerk (Wächter scheitert → Takt), Pufferüberlauf, sehr viele Repos. Neu: `WatchMapTests` (Routing je Fall, Wurzeln, eigener Git-Ordner, Abgleich), `ChangeDebouncerTests`, `RepoWatchTests` gegen echte Repos (Commit, Branchwechsel, neue Datei, Stage, Fetch ohne Refresh-Aufruf; neuer Worktree mit Status und Commits; Wächter folgen dem Board, `git worktree remove` trotz Wächter; ein Lesen löst kein weiteres aus – einmal gebrochen: scheitert; Rückfall-Takt mit `FakeTimeProvider`).

## Nachtrag: Code-Review (2026-11-21)
Getrennter Reviewer-Agent über `6438d18..69ce61b`, danach ein zweiter Durchgang über die Fix-Commits. Entscheidung des Nutzers vorab: „Wenn dir beim Code Review Dinge auffallen, evaluiere die beste Lösung und setze diese um. Das Ganze so lange wiederholen, bis alles gut ist.“ Jeder Fix mit Test wurde einmal gebrochen: der genannte Test scheiterte.

1. M – Endlosschleife: Mit fsmonitor (Cookie je `git status`) oder Git LFS (`lfs/tmp`) schreibt RepoBoards eigener Status in `.git`, der Wächter las daraufhin wieder (vom Reviewer nachgestellt) → behoben: im Git-Ordner zählt nur eine Positivliste (`WatchMap.StateFiles`/`StateFolders`, Submodule über `SubmoduleChanged`). Tests: Theory-Fälle in `WatchMapTests`, `Reading_the_status_does_not_read_it_again` auch mit fsmonitor-Daemon (übersprungen, wo git keinen hat).
2. K – `config` und `info/` (Upstream, Excludes) gingen nur an das Repo, nicht an seine Worktrees → behoben, Theory-Fälle.
3. K – `StatusReadAt` wurde auch bei gescheitertem Status gesetzt → behoben: erst nach Erfolg; `RefreshAsync` meldet ein belegtes Tor (`false`), nur dann liest der Wächter erneut; ein gescheiterter Status wird bei jedem Takt gelesen (`RepoState.StatusFailed`, im zweiten Durchgang statt `Error`, das auch gescheiterte Fetches hält). Test im Rückfall-Test.
4. K – `StatusReadAt` als `DateTimeOffset?` konnte zerrissen gelesen werden, die Entprellung hing an der Wanduhr → behoben: `long` über `Volatile`, Zeit aus `TimeProvider.GetTimestamp` (monoton).
5. K – Beim Beenden konnten Lesevorgänge und Callbacks auf entsorgte Semaphoren treffen → behoben: nicht mehr entsorgt.
6. K – Eine unerwartete Ausnahme beendete die Schleife (und mit `StopHost` den Host) → behoben: geloggt, 5 s Pause, neuer Abgleich.
7. K – Jedes Lesen eines Repos mit Worktrees erzeugte eine neue Liste und damit eine neue Karte → behoben: gleiche Worktrees, gleiche Liste; `NextWake` erzwingt den Abgleich nur bei fälligem Neuversuch.
8. K – Ein Wächter auf einem Netzlaufwerk kann starten und nichts melden; ein Worktree-Eintrag auf eine Laufwerkswurzel beobachtete das ganze Laufwerk → behoben: keine Wächter auf UNC, Netzlaufwerken und Laufwerkswurzeln (20-s-Takt).
9. K – Ein dauerhaft scheiternder Wächter warnte jede Minute → behoben: Warnung je Ordner und Meldung einmal, danach Debug; im zweiten Durchgang auch für einen Wächter, der startet und dann scheitert.
10. K – Testlücken (Neuversuch, Überlauf, belegtes Tor, `Under`) → Tests ergänzt.
11. K – Doku veraltet (CLAUDE.md, ADR 0002, ADR 0003) → nachgetragen.

Zweiter Durchgang: alle Fixes decken ihre Befunde, nichts gebrochen; dazu behoben: `StatusFailed` statt `Error` (s. 3), Warnung (s. 9), `reftable/` in der Positivliste und geteilt, `GIT_CEILING_DIRECTORIES` in `GitSandbox` (ein Repo über dem Temp-Ordner beantwortete sonst einen Test ohne `.git`), `GitSandbox.TryGit` statt eines zweiten Prozessstarts im Test, zwei Kleinigkeiten in Architektur und ADR. Vorher schon da und in den Backlog: ein Status-Fehler bleibt nach einem erfolgreichen Status stehen; `git worktree lock` unter Linux ohne Lesen.
