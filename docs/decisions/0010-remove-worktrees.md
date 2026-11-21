# ADR 0010: Worktrees entfernen nur ohne `--force`, mit Rückfrage und Liste der ignorierten Dateien

- Status: teilweise entschieden. **Entscheidung des Nutzers (2026-11-21, WP-04):** Worktrees als eingeklappte Unterzeilen mit Zähler; Claude-Worktrees mit Badge; „Aufräumen“ schlägt gemergte Branches, gelöschte Upstreams, Detached im Standardbranch und fehlende Ordner vor; keine eigenen Auto-Sync-Schalter je Worktree. Aus der Roadmap: `git worktree remove` nur ohne `--force`, ein Worktree mit ungesicherter Arbeit wird nur gezeigt. Nach dem Review (2026-11-21): vorab angehakt sind nur „Upstream gelöscht“ und „Ordner fehlt“. **Vorschlag (nicht bestätigt):** die übrigen Einzelheiten unten.
- Datum: 2026-11-21

## Kontext

`CLAUDE.md` §2 verbietet alles, was ungesicherte Arbeit verwerfen kann; Ausnahmen brauchen eine enge Methode in `GitClient`, eine Rückfrage, einen ADR und Tests. `git worktree remove` löscht einen Ordner des Nutzers. Ohne `--force` lehnt git einen Worktree mit geänderten, gestagten oder neuen Dateien ab und einen gesperrten (`git worktree lock`) auch. Was git trotzdem mitnimmt:
- **ignorierte Dateien**, ohne Rückfrage: nicht nur `bin/` und `obj/`, auch eine `.env`, `appsettings.Development.json` oder `.claude/settings.local.json`;
- **Commits eines detached HEAD**, die kein Branch und kein Tag enthält: Ihr einziger Verweis ist der Reflog des Worktrees in `.git/worktrees/<id>`, und der geht mit (Review WP-04);
- **ein laufendes Rebase** bei sauberem Baum (angehalten bei `edit`) samt seiner schon neu geschriebenen Commits (Review WP-04).

Branches bleiben. Die Worktrees des Nutzers liegen meist unter `<repo>\.claude\worktrees` und gehören Claude-Code-Sessions, die dort noch laufen können.

## Entscheidung

- **Eine Methode:** `GitClient.RemoveWorktreeAsync` ruft `git worktree remove -- <pfad>` im Hauptworktree auf (ein Prozess im Ordner selbst hielte ihn unter Windows fest) – nie `--force`, nie `prune` (das räumte alle verwaisten Einträge auf einmal ab). Für einen Worktree, dessen Ordner fehlt, entfernt derselbe Befehl nur die Daten in `.git/worktrees`.
- **Was einen Worktree hält** (`WorktreeBlock`, `BoardService.BlockAsync`): gesperrt; geänderte, gestagte oder neue Dateien; ein laufendes Merge/Rebase/Cherry-Pick/Revert (Git-Ordner jedes Mal frisch gelesen); detached mit Commits, die kein Branch, Remote-Branch oder Tag enthält (`rev-list --count <head> --not --branches --remotes --tags`, auch bei fehlendem Ordner über den HEAD aus `worktree list`); Zustand nicht lesbar. Ein solcher Worktree wird gezeigt, ist aber nicht anhakbar.
- **Rückfrage mit Liste** (`WorktreeDialog`), für einen Worktree (Knopf an der Zeile) wie fürs Aufräumen: je Worktree der Grund des Vorschlags, was ihn hält, und **alle ignorierten Dateien und Ordner**, die git mitlöscht (`status --ignored=traditional`, ein ignorierter Ordner als Ganzes; eine lange Liste scrollt). Lässt sich diese Liste nicht lesen, bleibt der Worktree. Ist ein Claude-Worktree angehakt, ein Hinweis, dass eine laufende Session ihren Ordner verliert.
- **Noch einmal prüfen:** `BoardService.RemoveWorktreeAsync` bekommt den bestätigten Kandidaten und prüft unmittelbar vor dem Entfernen alles erneut (`BlockAsync`) und liest die ignorierten Dateien neu: Kommt ein Eintrag dazu, den die Rückfrage nicht zeigte, bleibt der Worktree. Das Tor des Worktrees hält andere Operationen darin fern.
- **Was „erledigt“ heißt** (`BoardService.FindCleanupAsync`, gemessen an `origin/HEAD`): Upstream „gone“; Branch mit Upstream, dessen Commit im Standardbranch enthalten ist; detached auf einem darin enthaltenen Commit; Ordner fehlt. Nicht vorgeschlagen: ein Worktree genau auf dem Commit des Standardbranches, auf einem nie gepushten Branch ohne eigene Commits (eine Session, die gerade erst begonnen hat) oder auf dem Standardbranch selbst (Upstream = `origin/HEAD` oder gleicher Name). Ohne `origin/HEAD` nur „gone“ und „Ordner fehlt“, mit Hinweis im Dialog.
- **Vorab angehakt** (`WorktreeCandidate.Ticked`, Entscheidung des Nutzers nach dem Review): der einzeln gewählte Worktree sowie „Upstream gelöscht“ und „Ordner fehlt“. „Gemergt“ und „detached“ stehen nur da: Ein gepushter Branch ohne eigene Commits, dessen Basis weitergezogen ist, sieht genauso aus wie ein gemergter.

## Folgen

- Uncommittete und neue Dateien, Commits ohne Branch und ein laufendes Rebase gehen nie verloren; ignorierte Dateien nur, nachdem der Nutzer genau sie in der Liste gesehen und bestätigt hat.
- Branches bleiben liegen (Aufräumen von Branches: WP-08).
- Squash-Merges erkennt nur „Upstream gelöscht“, nicht „gemergt“.
- Eine Session in einem entfernten Claude-Worktree verliert ihren Ordner; das sagt der Dialog, verhindern kann RepoBoard es nicht.
