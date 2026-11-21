---
paths:
  - "src/RepoBoard.Core/Git/**"
  - "src/RepoBoard.Core/Board/**"
  - "src/RepoBoard.Core/Sync/**"
  - "tests/RepoBoard.Core.Tests/Git/**"
  - "tests/RepoBoard.Core.Tests/Sync/**"
  - "tests/Shared/GitSandbox.cs"
---

# Git, Board und Auto-Sync

Invarianten: `CLAUDE.md` §2; Begründungen: ADR 0002 (CLI), ADR 0003 (Auto-Sync).

## Aufrufe
- Ein neuer Befehl kommt als Methode in `GitClient`, nie als Argumentliste anderswo. Lesende Befehle über `Local` (30 s), Netzwerk über `Network` (3 min) mit `GitCallMode` – im Hintergrund immer `Background`.
- Ausgabe nur in stabilen Formaten parsen: `status --porcelain=v2 -z`, `for-each-ref --format`, `log --format` mit `%x1f`/`%x1e` als Trenner. Nie die menschenlesbare Ausgabe (sie folgt der Sprache von git). Meldungen für den Nutzer kommen aus `GitResult.Message` (stderr ohne `hint:`, Zugangsdaten über `GitSecrets` geschwärzt) – nie `Error`/`Output` roh anzeigen oder loggen.
- Pfade aus dem Status gehen nach `--` zurück an git; Branch-Namen über `IsBranchNameAsync` (ein Name mit führendem `-` wäre in `git switch` eine Option).
- Diffs (ADR 0008) aus `git diff --no-color --no-ext-diff`, geparst von `FileDiff.Parse` nach den Zählern im Hunk-Kopf (nicht nach Leerzeilen). `diff --no-index` endet mit 1, wenn die Dateien verschieden sind, und schreibt bei `core.autocrlf` eine Warnung auf stderr: nur `fatal:`/`error:` sind ein Fehler (E2E WP-05).
- `GIT_OPTIONAL_LOCKS=0` setzt `GitRunner` für alles: `status` legt sonst `index.lock` an und kollidiert mit IDE oder Terminal im selben Repo.
- In Literalen von Tests `\u001f` statt `\x1f` schreiben: C# liest `\x` mit bis zu vier Hex-Ziffern, `"\x1f2 hours"` ist U+01F2 (im Spike passiert).

## Board
- Jede Operation läuft durch das Tor ihres Repos (`RepoState.Gate`); `RefreshAsync` und `AutoSyncAsync` überspringen ein belegtes Repo statt zu warten, Klicks warten. Netzwerk zusätzlich über `_network` (höchstens vier).
- `RepoState`-Properties werden nur als Ganzes ersetzt (die UI liest sie von einem anderen Thread); keine Listen darin verändern.
- Zustand des Boards, den mehr als ein Thread ändert, liest und ändert unter einer Sperre: `UpdateSettingsAsync` kommt auch vom Thread-Pool (Update-Prüfung), sonst geht eine Änderung des Nutzers verloren (Review WP-10).
- Ein Fehler steht in `RepoState.Error` bis zum nächsten Erfolg. Derselbe Fehler bei jedem Auto-Sync kommt nur einmal ins Aktivitätsprotokoll; ein übersprungener Auto-Pull nur, wenn sich der Grund ändert.
- Texte für den Nutzer aus `SyncText`, nie zusammengeklebt (`TextFormat`, Plural über `CommitsOne`/`CommitsOther`).

## Tests
- Git-Tests laufen gegen echte Repos in `GitSandbox` (Bare-Remote, `Mine`, `Theirs`) – die globale und System-Konfiguration der Maschine ist per Umgebung abgeschaltet (Signieren, Hooks, Standard-Branch des Entwicklers), Identität fest. Neue Git-Tests nutzen sie, nie ein eigenes `git init` mit der Konfiguration der Maschine.
- Ohne git überspringen sie, in der CI (`CI=true`) scheitern sie.
