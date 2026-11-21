---
paths:
  - "src/RepoBoard.Core/Git/**"
  - "src/RepoBoard.Core/Board/**"
  - "src/RepoBoard.Core/Sync/**"
  - "src/RepoBoard.Core/Watch/**"
  - "tests/RepoBoard.Core.Tests/Git/**"
  - "tests/RepoBoard.Core.Tests/Sync/**"
  - "tests/RepoBoard.Core.Tests/Watch/**"
  - "tests/Shared/GitSandbox.cs"
---

# Git, Board und Auto-Sync

Invarianten: `CLAUDE.md` §2; Begründungen: ADR 0002 (CLI), ADR 0003 (Auto-Sync), ADR 0011 (Dateiwächter).

## Aufrufe
- Ein neuer Befehl kommt als Methode in `GitClient`, nie als Argumentliste anderswo. Lesende Befehle über `Local` (30 s), Netzwerk über `Network` (3 min) mit `GitCallMode` – im Hintergrund immer `Background`.
- Ausgabe nur in stabilen Formaten parsen: `status --porcelain=v2 -z`, `for-each-ref --format`, `log --format` mit `%x1f`/`%x1e` als Trenner. Nie die menschenlesbare Ausgabe (sie folgt der Sprache von git). Meldungen für den Nutzer kommen aus `GitResult.Message` (stderr ohne `hint:`, Zugangsdaten über `GitSecrets` geschwärzt) – nie `Error`/`Output` roh anzeigen oder loggen.
- Pfade aus dem Status gehen nach `--` zurück an git; Branch-Namen über `IsBranchNameAsync` (ein Name mit führendem `-` wäre in `git switch` eine Option).
- Pfade aus dem Status sind Dateinamen, keine Muster: Befehle mit Pfaden bekommen `--literal-pathspecs` (sonst trifft `[id].tsx` auch `i.tsx`; Review WP-05) – aber nur Befehle, die keine Hooks starten. Die Option exportiert `GIT_LITERAL_PATHSPECS` an die Kindprozesse, und die Globs eines Hooks finden dann nichts mehr; daher nie global in `GitRunner` und für `commit -- <pfade>` oder `switch`/`checkout` mit Pfaden stattdessen `:(literal)<pfad>` je Pfad (Review WP-05).
- Diffs (ADR 0008) aus `git diff --no-color --no-ext-diff`, geparst von `FileDiff.Parse` nach den Zählern im Hunk-Kopf (nicht nach Leerzeilen). `diff --no-index` endet mit 1 bei Unterschied **und** bei Fehler, stderr kann eine bloße Warnung (CRLF) sein und git übersetzt seine Meldungen: Erfolg ist Exit 1 mit Diff auf stdout, nie ein Präfix wie `error:` (E2E und Review WP-05).
- `GIT_OPTIONAL_LOCKS=0` setzt `GitRunner` für alles: `status` legt sonst `index.lock` an und kollidiert mit IDE oder Terminal im selben Repo.
- In Literalen von Tests `\u001f` statt `\x1f` schreiben: C# liest `\x` mit bis zu vier Hex-Ziffern, `"\x1f2 hours"` ist U+01F2 (im Spike passiert).
- `git worktree list` nennt bei einem Submodul oder `--separate-git-dir` den Git-Ordner als Hauptworktree: verknüpft ist ein Worktree nur, wenn sein Git-Ordner `commondir` enthält und der Hauptworktree ein `.git` hat (Review WP-04).

## Board
- Jede Operation läuft durch das Tor ihres Repos (`RepoState.Gate`); `RefreshAsync` und `AutoSyncAsync` überspringen ein belegtes Repo statt zu warten, Klicks warten. Netzwerk zusätzlich über `_network` (höchstens vier).
- `RepoState`-Properties werden nur als Ganzes ersetzt (die UI liest sie von einem anderen Thread); keine Listen darin verändern.
- Zustand des Boards, den mehr als ein Thread ändert, liest und ändert unter einer Sperre: `UpdateSettingsAsync` kommt auch vom Thread-Pool (Update-Prüfung), sonst geht eine Änderung des Nutzers verloren (Review WP-10).
- Ein Fehler steht in `RepoState.Error` bis zum nächsten Erfolg. Derselbe Fehler bei jedem Auto-Sync kommt nur einmal ins Aktivitätsprotokoll; ein übersprungener Auto-Pull nur, wenn sich der Grund ändert.
- Vor allem, was Dateien oder Refs entfernt: was die Rückfrage geprüft hat, unmittelbar vor dem git-Aufruf noch einmal prüfen (Status, laufende Operation mit frisch gelesenem Git-Ordner, Commits eines detached HEAD ohne Ref, Liste dessen, was mitgelöscht wird) – zwischen Frage und Klick arbeitet eine Session weiter (Review WP-04).
- Was RepoBoard beim Lesen aufruft, darf in Repo und Git-Ordner nichts schreiben: der Dateiwächter (ADR 0011) läse sonst nach jedem Status den nächsten (`RepoWatchTests.Reading_the_status_does_not_read_it_again`). Ein neuer lesender Befehl, der Dateien anlegt (Cache, Lock), braucht eine Ausnahme in `WatchMap.Route`.
- Texte für den Nutzer aus `SyncText`, nie zusammengeklebt (`TextFormat`, Plural über `CommitsOne`/`CommitsOther`).

## Tests
- Git-Tests laufen gegen echte Repos in `GitSandbox` (Bare-Remote, `Mine`, `Theirs`) – die globale und System-Konfiguration der Maschine ist per Umgebung abgeschaltet (Signieren, Hooks, Standard-Branch des Entwicklers), Identität fest. Neue Git-Tests nutzen sie, nie ein eigenes `git init` mit der Konfiguration der Maschine.
- Ohne git überspringen sie, in der CI (`CI=true`) scheitern sie.
