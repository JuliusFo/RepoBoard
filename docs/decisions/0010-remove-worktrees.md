# ADR 0010: Worktrees entfernen nur ohne `--force`, mit Rückfrage und Liste der ignorierten Dateien

- Status: teilweise entschieden. **Entscheidung des Nutzers (2026-11-21, WP-04):** Worktrees als eingeklappte Unterzeilen mit Zähler; Claude-Worktrees mit Badge; „Aufräumen“ schlägt gemergte Branches, gelöschte Upstreams, Detached im Standardbranch und fehlende Ordner vor; keine eigenen Auto-Sync-Schalter je Worktree. Aus der Roadmap: `git worktree remove` nur ohne `--force`, ein Worktree mit ungesicherter Arbeit wird nur gezeigt. **Vorschlag (nicht bestätigt):** die Einzelheiten unten.
- Datum: 2026-11-21

## Kontext

`CLAUDE.md` §2 verbietet alles, was ungesicherte Arbeit verwerfen kann; Ausnahmen brauchen eine enge Methode in `GitClient`, eine Rückfrage, einen ADR und Tests. `git worktree remove` löscht einen Ordner des Nutzers. Ohne `--force` lehnt git einen Worktree mit geänderten, gestagten oder neuen Dateien ab und einen gesperrten (`git worktree lock`) auch – **ignorierte Dateien löscht es aber ohne Rückfrage mit**: nicht nur `bin/` und `obj/`, auch eine `.env`, `appsettings.Development.json` oder `.claude/settings.local.json`. Der Branch bleibt; Commits gehen nicht verloren.

Die Worktrees des Nutzers liegen meist unter `<repo>\.claude\worktrees` und gehören Claude-Code-Sessions, die dort noch laufen können.

## Entscheidung

- **Eine Methode:** `GitClient.RemoveWorktreeAsync` ruft `git worktree remove -- <pfad>` im Hauptworktree auf (ein Prozess im Ordner selbst hielte ihn unter Windows fest) – nie `--force`, nie `prune` (das räumte alle verwaisten Einträge auf einmal ab). Für einen Worktree, dessen Ordner fehlt, entfernt derselbe Befehl nur die Daten in `.git/worktrees`.
- **Rückfrage mit Liste** (`WorktreeDialog`), für einen Worktree (Knopf an der Zeile) wie fürs Aufräumen: je Worktree der Grund des Vorschlags, ob er bleibt (Änderungen, gesperrt, laufendes Merge/Rebase, Zustand nicht lesbar – nicht anhakbar) und die **ignorierten Dateien und Ordner**, die git mitlöscht (`status --ignored=traditional`, ein ignorierter Ordner als Ganzes). Lässt sich diese Liste nicht lesen, bleibt der Worktree. Ist ein Claude-Worktree angehakt, ein Hinweis, dass eine laufende Session ihren Ordner verliert.
- **Noch einmal prüfen:** `BoardService.RemoveWorktreeAsync` liest den Status unmittelbar vor dem Entfernen neu; hat der Worktree inzwischen Änderungen, bleibt er (eigene Meldung statt git's Hinweis auf `--force`). Das Tor des Worktrees hält andere Operationen darin fern.
- **Was „erledigt“ heißt** (`BoardService.FindCleanupAsync`, gemessen an `origin/HEAD`): Upstream „gone“; Branch mit Upstream, dessen Commit im Standardbranch enthalten ist; detached auf einem darin enthaltenen Commit; Ordner fehlt. Nicht vorgeschlagen: ein Worktree genau auf dem Commit des Standardbranches oder auf einem nie gepushten Branch ohne eigene Commits – das ist eine Session, die gerade erst begonnen hat. Ohne `origin/HEAD` nur „gone“ und „Ordner fehlt“, mit Hinweis im Dialog.

## Folgen

- Uncommittete und neue Dateien gehen nie verloren; ignorierte nur, nachdem der Nutzer sie in der Liste gesehen und bestätigt hat.
- Branches bleiben liegen (Aufräumen von Branches: WP-08).
- Squash-Merges erkennt nur „Upstream gelöscht“, nicht „gemergt“.
- Eine Session in einem entfernten Claude-Worktree verliert ihren Ordner; das sagt der Dialog, verhindern kann RepoBoard es nicht.
