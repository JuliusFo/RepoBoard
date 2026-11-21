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
- Nach dem Review: **vorab angehakt nur „Upstream gelöscht“ und „Ordner fehlt“** (und der einzeln gewählte Worktree); „gemergt“ und „detached“ stehen nur da. Der **Tooltip im Infobereich zählt weiter nur Repos**, nicht ihre Worktrees.

## Vorschläge (nicht bestätigt)
- Einzelheiten nach ADR 0010: Entfernen nur über `git worktree remove -- <pfad>` (auch für fehlende Ordner, statt `prune`, das alle auf einmal abräumt); was einen Worktree hält (gesperrt, Änderungen, laufendes Merge/Rebase, detached mit Commits ohne Branch, Zustand unlesbar) unmittelbar davor noch einmal prüfen; alle ignorierten Dateien, die git mitlöscht, in der Rückfrage auflisten und vor dem Entfernen neu lesen (ein neuer Eintrag hält den Worktree); Hinweis bei angehakten Claude-Worktrees.
- „Gemergt“ nur für einen Branch mit Upstream (oder detached), nicht genau auf dem Commit des Standardbranches und nicht für den Standardbranch selbst (Upstream = `origin/HEAD` oder gleicher Name): ein frischer, nie gepushter Branch einer Session, die vor einem Merge eines anderen begonnen hat, ist nicht „erledigt“.
- Nicht-Entfernbares steht im Dialog mit Grund da und ist nicht anhakbar.
- Der Chip „Alle“ zählt Repos, die anderen Chips zählen jede Zeile (auch Worktrees). Die Suche prüft bei Worktrees Name und Branch, nicht den Pfad (der enthält den des Repos – die Suche nach dem Repo öffnete sonst alle Worktrees).
- An einer Worktree-Zeile kein Fetch (Refs geteilt) und „Worktree entfernen“ (Mülleimer) statt „vom Board entfernen“ (X); „Gefetcht“ zeigt die spätere Zeit von Worktree und Repo. Ein fehlender Ordner zeigt „Ordner fehlt“ und nur Entfernen.
- Wählt man beim Hinzufügen oder findet der Scanner einen verknüpften Worktree (sein Git-Ordner enthält `commondir`, sein Hauptworktree hat ein `.git`), kommt sein Repo aufs Board – mit Eintrag im Protokoll, wenn es neu ist. Ein Submodul oder ein Repo mit eigenem Git-Ordner bleibt, wie es gewählt wurde.

## Gebaut
- **Core:** `WorktreeInfo` (`ParseList` für `worktree list --porcelain -z`), `GitClient` `WorktreesAsync`, `DefaultBranchAsync` (`origin/HEAD`), `CommitOfAsync`, `IsAncestorAsync`, `IgnoredAsync`, `RemoveWorktreeAsync`; `RepoState` `MainRepo`, `Worktree`, `Worktrees`, `IsClaudeWorktree`; `BoardService` liest Worktrees mit jedem Status (Hauptworktree), `RefreshAllAsync` in zwei Stufen, Worktrees neu nach Fetch/Pull/Push/Auto-Sync des Repos, `InspectWorktreeAsync`, `FindCleanupAsync`, `RemoveWorktree(s)Async`, `AddAsync` ersetzt einen Worktree durch sein Repo; `WorktreeCandidate`/`WorktreeCleanup`/`WorktreeReason`/`WorktreeBlock`; Texte in `SyncText`. Seit dem Review: `BoardService.BlockAsync`/`BlockText`, `GitClient.CountUnreferencedAsync`, `WorktreeCandidate.Ticked`, `RemoveWorktree(s)Async` mit dem bestätigten Kandidaten, `RepoState.ReplaceWorktrees`/`RemoveWorktree`, `MainOfLinkedWorktreeAsync`, Fehler je Repo in `RefreshAsync` gefangen.
- **Beim E2E dazu:** `RepoStatus.UpstreamGone` – ein Upstream ohne `branch.ab` gibt es auf dem Remote nicht mehr; die Zeile zeigt „Upstream gelöscht“ statt „aktuell“ (vorher schon so, auch bei Repos).
- **UI:** `RepoRow` für Repo und Worktree (Zähler-Badge, „Claude“, „gesperrt“, Mülleimer, keine Schalter), `Shell` (offene Gruppen, Unterzeilen, „Aufräumen …“, Dialog), `WorktreeDialog`, `BoardFilter` (`VisibleWorktrees`, `ShownWorktrees`, `Count`, `IsNarrowed`), Icons `layers`, `lock`, `trash-2` (Lucide 1.55), CSS, Texte in `BoardText` (en/de).
- **Doku:** ADR 0010, README, CHANGELOG, Architektur (Board, UI-Konzept), Roadmap (WP-04 raus), Backlog (Worktrees).

## Geprüft
- `dotnet build -warnaserror`, `dotnet test`: Core 172, UI 93 grün, nach dem Review 178 und 93; `WorktreeTests` gegen echte Repos (Liste, Claude-Erkennung, Hinzufügen eines Worktrees, Scanner mit Submodul, Entfernen sauber/mit Änderungen je Art/gesperrt, ignorierte Dateien, Aufräumen mit allen Gründen und Nicht-Fällen, Fetch frischt Worktrees auf).
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

## Nachtrag: Code-Review (2026-11-21)
Getrennter Reviewer-Agent über `dba40b7..cbd42d3`, danach ein zweiter Durchgang über die Fix-Commits. Entscheidung des Nutzers: „Alle beheben“, zu 7B und 11 die vorgeschlagenen Lösungen. Jeder Fix mit Test wurde einmal gebrochen: der genannte Test scheiterte.

1. S – Ein detached Worktree mit eigenen Commits galt als entfernbar und war vorab angehakt; danach hingen die Commits an keinem Ref mehr (Reflog mitgelöscht), auch bei „Ordner fehlt“ → behoben: `WorktreeBlock.UnreferencedCommits` (`rev-list --count <head> --not --branches --remotes --tags`), im Dialog und vor dem Entfernen. Test `A_detached_worktree_with_commits_of_its_own_stays` (mit und ohne Ordner).
2. M – Vor dem Entfernen nur `IsClean` geprüft (ein Rebase bei `edit` ist sauber), `GitDir` zwischengespeichert → behoben: `BlockAsync` prüft alles mit frischem Git-Ordner, auch unmittelbar vor `worktree remove`. Test `A_worktree_with_work_in_it_stays` (Fall `rebase`).
3. M – Die ignorierten Dateien galten für den Moment, in dem der Dialog öffnete → behoben: der bestätigte Kandidat geht ans Board, ein neuer ignorierter Eintrag hält den Worktree (`SyncText.Worktree_IgnoredChanged`). Test `Only_the_ignored_files_the_question_named_are_deleted_with_the_worktree`.
4. K – Höchstens acht ignorierte Einträge gezeigt → behoben: alle, die Liste scrollt.
5. M – Ein Submodul (oder `--separate-git-dir`) brachte beim Hinzufügen seinen Git-Ordner aufs Board → behoben: ersetzt wird nur ein verknüpfter Worktree (`commondir`); im zweiten Durchgang auch für einen Worktree eines Submoduls (Hauptworktree muss ein `.git` haben). Test `Adding_a_worktree_puts_its_repository_on_the_board_but_a_submodule_stays`.
6. M – Ein Worktree-Eintrag ohne Pfad warf in `GetFullPath` und stoppte Status und Auto-Sync aller Repos → behoben: solche Einträge fallen weg, und `RefreshAsync` fängt einen unerwarteten Fehler je Repo. Fall im Theory `The_porcelain_list_is_read_field_by_field`.
7. M – Der Worktree des Standardbranches (hinter `origin/main`) galt als „gemergt“ und war angehakt; ein gepushter Branch ohne eigene Commits ebenso → behoben: Standardbranch ausgenommen; „gemergt“ und „detached“ nicht vorab angehakt (`WorktreeCandidate.Ticked`). Test `Clean_up_suggests_the_worktrees_that_look_done`.
8. K – Der Scanner verwarf Worktrees, deren Repo außerhalb des gescannten Ordners liegt → behoben: der Scanner liefert sie wieder, `AddAsync` ersetzt sie durch ihr Repo. Test `A_scan_puts_the_repository_of_the_worktrees_it_finds_on_the_board`.
9. K – Ein entfernter Worktree konnte für einen Tick zurückkommen (Liste ohne das Tor des Repos ersetzt) → behoben: `ReplaceWorktrees` tauscht nur, wenn sich die Liste nicht geändert hat; kein Test (Wettlauf).
10. K – Pull/Push an einer Worktree-Zeile frischte Repo und andere Worktrees nicht auf → behoben. Test `A_pull_in_a_worktree_brings_its_repository_and_the_others_up_to_date`.
11. K – Der Tooltip im Infobereich zählt nur Repos, die Chips auch Worktrees → so gelassen (Entscheidung des Nutzers), in `TrayStatus` festgehalten.
12. K – Eine aufgeklappte, aber ausgeblendete Worktree-Zeile las weiter Branches und Log → behoben: `Shell.OnAfterRenderAsync` schließt jede nicht gezeigte (`BoardFilter.ShownWorktrees`). E2E per CDP: aufgeklappt, Gruppe zu und wieder auf → zu.
- Zweiter Durchgang: alle zwölf abgedeckt; dazu der Restfall zu 5 (behoben, s. o.) und veraltete Doku (ADR 0010, Protokoll, Architektur, CHANGELOG – nachgezogen).
- E2E der Fixes (Testinstanz): „detached“ im Aufräumen nur gelistet, nicht angehakt; Mülleimer am detached Worktree mit eigenem Commit zeigt den Grund, Knopf gesperrt (per CDP – Windows holte das Fenster nicht nach vorn, `Invoke-RealClick.ps1` brach vor dem Klick ab).
- Vorher schon da, Backlog: Push auf einem Branch mit gelöschtem Upstream legt den Remote-Branch wieder an; lokale Commits eines solchen Branches zählen nicht als „Voraus“.
