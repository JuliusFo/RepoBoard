# WP-04 Worktrees

Branch `wp/04-worktrees`, 2026-11-21, noch ohne Release.

## Planung (aus `docs/roadmap.md`)
- Worktrees eines Repos (`git worktree list --porcelain`) als Unterzeilen mit Branch, Status, Pfad; dieselben Aktionen wie für ein Repo.
- Aufräumen: verwaiste (`prune`) und solche, deren Branch gemergt ist, mit Rückfrage und Liste.
- Invariante: `git worktree remove` nur ohne Änderungen (ohne `--force`); ein Worktree mit ungesicherter Arbeit wird nur gezeigt, nie entfernt.
- Kontext: die vielen `.claude/worktrees` des Nutzers. Offen waren: Claude-Worktrees besonders kennzeichnen? Worktrees zusammenklappbar?

## Entscheidungen des Nutzers (2026-11-21)
- **Eingeklappt mit Zähler:** die Repo-Zeile zeigt die Zahl der Worktrees, ein Klick klappt die Unterzeilen auf; der Zustand gilt, solange die App läuft. Unter einem Filter oder einer Suche erscheinen passende Worktrees von selbst.
- **Claude-Worktrees** (`<repo>\.claude\worktrees\…`) bekommen ein Badge „Claude“; beim Entfernen ein Hinweis, dass dort eine Session laufen könnte.
- **Aufräumen schlägt vor:** Branch gemergt, Upstream gelöscht („gone“), Detached auf einem Commit des Standardbranches, Ordner fehlt.
- **Keine eigenen Auto-Sync-Schalter** je Worktree (Refs geteilt; Auto-Pull je Worktree in den Backlog).
- Plan mit diesen Antworten freigegeben.

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0010: Entfernen nur über `git worktree remove -- <pfad>` (auch für fehlende Ordner, statt `prune`, das alle auf einmal abräumt), Status unmittelbar davor neu lesen, die ignorierten Dateien, die git mitlöscht, in der Rückfrage auflisten (ohne diese Liste kein Entfernen), Hinweis bei angehakten Claude-Worktrees.
- „Gemergt“ nur für einen Branch mit Upstream (oder detached) und nicht genau auf dem Commit des Standardbranches: ein frischer, nie gepushter Branch einer Session, die vor einem Merge eines anderen begonnen hat, ist nicht „erledigt“.
- Im Dialog ist alles Entfernbare vorab angehakt; Nicht-Entfernbares (Änderungen, gesperrt, laufendes Merge/Rebase, Zustand unlesbar) steht mit Grund da und ist nicht anhakbar.
- Der Chip „Alle“ zählt Repos, die anderen Chips zählen jede Zeile (auch Worktrees). Die Suche prüft bei Worktrees Name und Branch, nicht den Pfad (der enthält den des Repos – die Suche nach dem Repo öffnete sonst alle Worktrees).
- An einer Worktree-Zeile kein Fetch (Refs geteilt) und „Worktree entfernen“ (Mülleimer) statt „vom Board entfernen“ (X); „Gefetcht“ zeigt die spätere Zeit von Worktree und Repo. Ein fehlender Ordner zeigt „Ordner fehlt“ und nur Entfernen.
- Wählt man beim Hinzufügen einen Worktree-Ordner, kommt sein Repo aufs Board (mit Eintrag im Protokoll); der Scanner überspringt verknüpfte Worktrees (`.git`-Datei → Ordner mit `commondir`, ein Submodul hat keinen).

## Gebaut
- **Core:** `WorktreeInfo` (`ParseList` für `worktree list --porcelain -z`), `GitClient` `WorktreesAsync`, `DefaultBranchAsync` (`origin/HEAD`), `CommitOfAsync`, `IsAncestorAsync`, `IgnoredAsync`, `RemoveWorktreeAsync`; `RepoState` `MainRepo`, `Worktree`, `Worktrees`, `IsClaudeWorktree`; `BoardService` liest Worktrees mit jedem Status (Hauptworktree), `RefreshAllAsync` in zwei Stufen, Worktrees neu nach Fetch/Pull/Push/Auto-Sync des Repos, `InspectWorktreeAsync`, `FindCleanupAsync`, `RemoveWorktree(s)Async`, `AddAsync` ersetzt einen Worktree durch sein Repo; `WorktreeCandidate`/`WorktreeCleanup`/`WorktreeReason`/`WorktreeBlock`; `RepoScanner.IsLinkedWorktree`; Texte in `SyncText`.
- **Beim E2E dazu:** `RepoStatus.UpstreamGone` – ein Upstream ohne `branch.ab` gibt es auf dem Remote nicht mehr; die Zeile zeigt „Upstream gelöscht“ statt „aktuell“ (vorher schon so, auch bei Repos).
- **UI:** `RepoRow` für Repo und Worktree (Zähler-Badge, „Claude“, „gesperrt“, Mülleimer, keine Schalter), `Shell` (offene Gruppen, Unterzeilen, „Aufräumen …“, Dialog), `WorktreeDialog`, `BoardFilter` (`VisibleWorktrees`, `Count`, `IsNarrowed`), Icons `layers`, `lock`, `trash-2` (Lucide 1.55), CSS, Texte in `BoardText` (en/de).
- **Doku:** ADR 0010, README, CHANGELOG, Architektur (Board, UI-Konzept), Roadmap (WP-04 raus), Backlog (Worktrees).

## Geprüft
- `dotnet build -warnaserror`, `dotnet test`: Core 172, UI 93 grün; `WorktreeTests` gegen echte Repos (Liste, Claude-Erkennung, Hinzufügen eines Worktrees, Scanner mit Submodul, Entfernen sauber/mit Änderungen je Art/gesperrt, ignorierte Dateien, Aufräumen mit allen Gründen und Nicht-Fällen, Fetch frischt Worktrees auf).
- E2E (2026-11-21, Testinstanz mit `--data-dir` im Scratchpad, Repo `project` mit Bare-Remote und sieben Worktrees: detached, mit Änderungen, gesperrt, gemergt mit `bin/` und `.env`, Ordner verschoben, Claude frisch, Claude mit gelöschtem Upstream). Eingeschleuste Eingaben kamen durch (`Test-InjectedInput.ps1`), Klicks mit `Invoke-RealClick.ps1`:
  - Klick aufs Zähler-Badge klappt die Gruppe auf, ohne die Zeile zu öffnen; Chips zählen richtig (Änderungen 1, Voraus 4, Probleme 1 = fehlender Ordner).
  - „Aufräumen …“: vorgeschlagen detached, gemergt (mit `.env` und `bin/` als „wird mitgelöscht“), Ordner fehlt, Claude gone; Claude-Hinweis. Detached abgehakt (Knopf „3 Worktrees entfernen“), entfernt: drei weg, detached bleibt, alle Branches bleiben, der verschobene Ordner unberührt.
  - Mülleimer am Worktree mit Änderungen: Dialog mit „Hat geänderte oder neue Dateien – bleibt.“, Knopf gesperrt; Abbrechen schließt. Mülleimer am Claude-Worktree: entfernt.
  - Per CDP: zugeklappte Gruppe + Chip „Änderungen“ zeigt nur den Worktree mit Änderungen, Suche „lock“ den gesperrten; Worktree-Zeile aufklappen zeigt Änderungen und Branches.
  - **Gefunden und behoben:** ein Worktree mit gelöschtem Upstream zeigte „aktuell“ (→ `RepoStatus.UpstreamGone`).
  - Bilder hell (de) und dunkel (en); Log ohne `[ERR]`/`[WRN]`.
- Nicht geprüft: Hinzufügen und Scannen über den nativen Ordnerdialog (nur Tests), sehr viele Worktrees (Zeit des Status-Durchlaufs).

## Offen
- Worktree anlegen, Branch mitlöschen (WP-08), Auto-Pull je Worktree, Messung bei vielen Worktrees, doppelte Anzeige eines schon vorher hinzugefügten Worktrees, Aufräumen über alle Repos: Backlog.
