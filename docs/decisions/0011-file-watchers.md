# ADR 0011: Dateiwächter je Repo, der Status-Takt als Rückfall

- Status: teilweise entschieden. **Entscheidung des Nutzers (2026-11-21, WP-03):** ganzer Ordner beobachten (nicht nur `.git`); beobachtete Repos liest der Takt nur alle 2 min, die übrigen weiter alle 20 s; immer an, kein Schalter. **Vorschlag (nicht bestätigt):** die Einzelheiten unten.
- Datum: 2026-11-21

## Kontext

Bis 0.2.0 las RepoBoard den Status jedes Repos und jedes Worktrees alle 20 s (`git status`, dazu `worktree list`, ggf. Branches und Log). Ein Commit oder Branchwechsel in IDE oder Terminal stand bis zu 20 s später auf dem Board, und mit WP-04 wuchs die Zahl der Prozessstarts mit jedem Worktree – bei den vielen `.claude/worktrees` des Nutzers ein git-Prozess je Worktree und Takt, auch wenn sich nirgends etwas ändert.

## Entscheidung

- **Ein rekursiver `FileSystemWatcher` je Wurzel** (`RepoWatchService`, Core, Hosted Service): Wurzeln sind die Ordner und Git-Ordner aller Repos und Worktrees, die kein anderer enthält (`WatchMap.Roots`). Ein Worktree unter `<repo>\.claude\worktrees` und die Git-Ordner der Worktrees (`.git/worktrees/<name>`) liegen im Repo-Ordner und brauchen keinen eigenen; ein Worktree neben dem Repo bekommt einen. Puffer 64 KB, gefiltert auf Dateiname, Ordnername und Schreibzeit. Plattformneutral (inotify unter Linux), Core bleibt Linux-fähig.
- **Routing nach Pfad** (`WatchMap.Route`, rein und ohne Dateisystem getestet): zuerst der tiefste Git-Ordner, der den Pfad enthält – `refs/**` und `packed-refs` machen Repo **und alle Worktrees** veraltet (geteilte Refs), `worktrees/<name>/…` nur diesen Worktree, ein neuer Eintrag unter `worktrees/` das Repo (es liest die Liste neu), alles andere das Repo. Sonst der tiefste Repo- oder Worktree-Ordner. Nichts lesen: `objects/`, `logs/`, `*.lock`, ein Pfad mit einem Segment `.git` außerhalb eines bekannten Git-Ordners (Zeigerdatei eines Worktrees, eingebettetes Repo) und Ordner namens `bin`, `obj`, `node_modules`, `.vs`, `.idea`, `TestResults` (feste Liste; ein getrackter `bin`-Ordner zeigt sich erst mit dem Rückfall).
- **Entprellen** (`ChangeDebouncer`): je Repo bzw. Worktree ein Status, wenn 750 ms nichts kam, spätestens 3 s nach der ersten Änderung einer Serie. Gelesen wird über `BoardService.RefreshAsync` (Tor, höchstens acht gleichzeitig). Hat ein Lesen nach der letzten Änderung begonnen (`RepoState.StatusReadAt`: Klick, Takt, Ende einer Operation), entfällt es; ist das Repo beschäftigt, kommt es nach der Ruhezeit wieder dran.
- **Kein Kreislauf:** RepoBoards eigene git-Aufrufe schreiben nichts, was ein Wächter sieht (`GIT_OPTIONAL_LOCKS=0`, ADR 0002); ein Test hält fest, dass ein Lesen kein weiteres auslöst.
- **Rückfall:** `AutoSyncService` ruft je Takt (20 s) `RefreshStaleAsync`: Repos und Worktrees ohne Wächter (`RepoState.Watched` falsch: Ordner fehlt, Netzlaufwerk ohne Benachrichtigungen, inotify-Grenze) jedes Mal, beobachtete nur, wenn ihr letzter Status 2 min alt ist. Der Knopf „Status neu lesen“ und das Zurückwechseln ins Fenster lesen weiter alles.
- **Fehler:** ein übergelaufener Puffer liest einmal alles unter dieser Wurzel; jeder andere Fehler beendet den Wächter, seine Repos fallen auf den Takt zurück, nach einer Minute ein neuer Versuch. Ein Wächter, der nicht startet, ebenso (Warnung im Log, keine Meldung im Board).
- **Abgleich:** bei jeder Änderung des Boards prüft `WatchMap.Matches` billig (Referenzen), ob sich Repos, Worktrees, Git-Ordner oder fehlende Ordner geändert haben; nur dann werden Wächter angelegt oder beendet. Ein Worktree ohne Status (eben angelegt) wird dabei sofort gelesen.

## Folgen

- Änderungen aus IDE und Terminal stehen nach etwa einer Sekunde auf dem Board; ein neuer Worktree einer Claude-Session ebenso.
- Im Leerlauf startet RepoBoard für beobachtete Repos alle 2 min einen git-Prozess statt alle 20 s.
- Ein Build in einem nicht ignorierten Ordner liest den Status des Repos höchstens alle 3 s.
- Ein Wächter hält einen Handle auf seinen Ordner (mit Freigabe zum Löschen): `git worktree remove` eines beobachteten Worktrees neben dem Repo gelingt, der Wächter endet danach (Test `The_watchers_follow_the_board`).
