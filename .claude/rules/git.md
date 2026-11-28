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
- Teile eines Diffs gehen nur als Patch über stdin an `git apply --cached` (ADR 0012, `HunkPatch`, `GitClient.ApplyToIndexAsync`) – nie `git apply` ohne `--cached` (das schriebe in den Worktree). Immer `--whitespace=nowarn` (`apply.whitespace=error` des Nutzers), bei einem Hunk ohne Kontext `--unidiff-zero`. Pfade im Patch C-gequotet wie von git (`HunkPatch.Quote`). Der Patch-Text kommt aus `diff --no-textconv`, nie aus dem angezeigten Diff: der Text eines Diff-Treibers käme sonst als Datei in den Index (git-crypt: Klartext; Review WP-25).
- Eine gestagte Umbenennung (`OriginalPath`) unstagt man mit beiden Pfaden – `restore --staged -- <neu>` allein lässt die Löschung des alten gestaged und den neuen ungetrackt (Review WP-25).
- `log` und `show` immer mit `--no-show-signature`: ein `log.showSignature` des Nutzers schreibt sonst gpg-Ausgaben in das geparste Format (WP-09). Revisionen nach `--` abschließen (`log … HEAD --branches --`), sonst macht eine Datei `HEAD` die Revision mehrdeutig.
- Commit-IDs aus der Historie gehen nur an git, wenn `GitClient.IsCommitId` sie annimmt (volle ID: 40 oder 64 Hex-Zeichen; eine kurze löst git zuerst als Branch auf, Review WP-09) – nie eine Revision oder ein Bereich aus der UI (ADR 0013).
- Ein Cherry-Pick, dessen Änderung HEAD schon hat, bleibt als unfertige Operation stehen (`CHERRY_PICK_HEAD`, git 2.41); vorher `GitClient.IsAppliedAsync` (`git cherry`, Patch-ID). Die erkennt nur 1:1-Kopien – nach einem Squash mehrerer Commits bleibt er trotzdem leer stehen, dann sagt die Meldung „Überspringen“ (Review WP-09, seit WP-07 in der Zeile). Ein leerer Revert scheitert dagegen ohne Rest (E2E WP-09).
- `git status` zeigt ignorierte Dateien nicht, und git überschreibt sie bei Revert, Cherry-Pick, Merge und Branchwechsel ohne Rückfrage (für git entbehrlich). Wer vor so einem Befehl „keine Änderungen“ prüft, prüft die betroffenen Pfade zusätzlich mit `GitClient.IgnoredAmongAsync` – auch die Eltern-Ordner: eine ignorierte Datei `foo` verschwindet, wenn der Commit `foo/bar` bringt (`BoardService.IgnoredInTheWayAsync`, Review WP-09).
- Viele Pfade auf einmal an git: in Teilen (`GitClient.Batches`), die Kommandozeile unter Windows endet bei 32 767 Zeichen.
- `GIT_EDITOR=:` setzt `GitRunner` für alles (WP-07): `merge|rebase|cherry-pick|revert --continue` öffnet sonst einen Editor für die Nachricht und wartet auf niemanden; `:` ist gits eigenes „kein Editor“ (kein Prozess, kein `true.exe` nötig; Review WP-07). Befehle, die Commits schreiben und signieren können oder den Worktree zurücksetzen (Merge, Rebase, Fortsetzen, Überspringen, Abbrechen), laufen mit `GitRunner.CommitTimeout` (3 min) statt 30 s.
- `git merge` schreibt den übergebenen Namen in die Nachricht: `refs/heads/x` ergibt „Merge branch 'refs/heads/x'“. Den kurzen Namen nur nehmen, wenn `IsBranchNameAsync` ihn annimmt, er auf denselben Commit zeigt wie der volle Ref und es keinen Tag dieses Namens gibt (git löst den Tag zuerst auf, auch auf demselben Commit; Review WP-07), sonst den vollen Ref.
- Während eines Rebase ist HEAD detached (`branch.head (detached)`); den Branch nennt `rebase-merge/head-name` (`OperationInfo.Branch`). `rebase --continue` lehnt geänderte, nicht gestagte Dateien ab; Merge/Cherry-Pick/Revert committen nur den Index. `--abort` verweigert, statt eine neue Datei zu überschreiben.
- Konfliktmarker über `diff --check -- <pfad>` (Exit 2 mit Ausgabe), mit `-c core.whitespace=-blank-at-eol,…`: sonst zählt ein Leerzeichen am Zeilenende als Fund.
- `GitResult.Message` zeigt eine Zeile wie ein Terminal: nur den Teil nach dem letzten `\r` (Fortschritt „Rebasing (1/2)“), ohne ANSI-Steuerfolgen (`ESC[K`, wenn `TERM` gesetzt ist; Review WP-07).
- Eine Aktion auf eine angehaltene Operation, die etwas verwirft (`--abort`, `--skip`), nimmt den Schlüssel (`OperationInfo.Key`, mit `HEAD` – ein Commit im Terminal ändert sonst weder Schritt noch `REBASE_HEAD`) und die Dateiliste der Rückfrage mit; das Tor vergleicht beide (ADR 0014, Review WP-07).
- `--abort`/`--skip` setzen **jede gestagte Datei** zurück, auch eine neue: `merge|cherry-pick|revert` per `reset --merge` (eigene ungestagte Änderungen an unbeteiligten Dateien bleiben), `rebase` per `reset --hard` (nichts bleibt außer untracked). Neue Dateien des Nutzers vorher mit `rm --cached -f` untracked machen (`BoardService.KeepNewFilesAsync`; ohne `-f` verweigert git eine nach dem Stagen bearbeitete Datei; Review WP-07).
- `GIT_OPTIONAL_LOCKS=0` setzt `GitRunner` für alles: `status` legt sonst `index.lock` an und kollidiert mit IDE oder Terminal im selben Repo.
- In Literalen von Tests `\u001f` statt `\x1f` schreiben: C# liest `\x` mit bis zu vier Hex-Ziffern, `"\x1f2 hours"` ist U+01F2 (im Spike passiert).
- `git worktree list` nennt bei einem Submodul oder `--separate-git-dir` den Git-Ordner als Hauptworktree: verknüpft ist ein Worktree nur, wenn sein Git-Ordner `commondir` enthält und der Hauptworktree ein `.git` hat (Review WP-04).

## Board
- Jede Operation läuft durch das Tor ihres Repos (`RepoState.Gate`); `RefreshAsync` und `AutoSyncAsync` überspringen ein belegtes Repo statt zu warten, Klicks warten. Netzwerk zusätzlich über `_network` (höchstens vier).
- `RepoState`-Properties werden nur als Ganzes ersetzt (die UI liest sie von einem anderen Thread); keine Listen darin verändern.
- Zustand des Boards, den mehr als ein Thread ändert, liest und ändert unter einer Sperre: `UpdateSettingsAsync` kommt auch vom Thread-Pool (Update-Prüfung), sonst geht eine Änderung des Nutzers verloren (Review WP-10).
- Eine Board-Operation, die einen Diff liest und parst, läuft über `Task.Run`: Klicks kommen aus dem Renderer-Kontext, und ohne Thread-Wechsel parst sie auf dem UI-Thread (`DiffAsync`, `StageHunkAsync`; Review WP-05 und WP-25).
- Ein Fehler steht in `RepoState.Error` bis zum nächsten Erfolg. Derselbe Fehler bei jedem Auto-Sync kommt nur einmal ins Aktivitätsprotokoll; ein übersprungener Auto-Pull nur, wenn sich der Grund ändert.
- Eine Aktion, die sich auf einen gezeigten Stand bezieht (Amend auf den Commit, dessen Nachricht vorgefüllt ist), nimmt diesen Stand mit und vergleicht ihn im Tor (`expectedHead`) – eine parallele Session committet zwischen Anzeige und Klick (Review WP-09).
- Vor allem, was Dateien oder Refs entfernt: was die Rückfrage geprüft hat, unmittelbar vor dem git-Aufruf noch einmal prüfen (Status, laufende Operation mit frisch gelesenem Git-Ordner, Commits eines detached HEAD ohne Ref, Liste dessen, was mitgelöscht wird) – zwischen Frage und Klick arbeitet eine Session weiter (Review WP-04).
- Ein lesender git-Aufruf kann trotzdem in den Git-Ordner schreiben – mit Erweiterungen der Repo-Konfiguration: fsmonitor legt bei jedem `status` ein Cookie an, Git LFS Dateien in `lfs/tmp` (Review WP-03). Der Dateiwächter (ADR 0011) zählt im Git-Ordner daher nur eine Positivliste (`WatchMap.StateFiles`/`StateFolders`), nie „alles außer …“; Tests mit Wächtern auch mit `core.fsmonitor=true` (`RepoWatchTests.Reading_the_status_does_not_read_it_again`).
- Texte für den Nutzer aus `SyncText`, nie zusammengeklebt (`TextFormat`, Plural über `CommitsOne`/`CommitsOther`).

## Tests
- Git-Tests laufen gegen echte Repos in `GitSandbox` (Bare-Remote, `Mine`, `Theirs`) – die globale und System-Konfiguration der Maschine ist per Umgebung abgeschaltet (Signieren, Hooks, Standard-Branch des Entwicklers), Identität fest. Neue Git-Tests nutzen sie, nie ein eigenes `git init` mit der Konfiguration der Maschine.
- Ohne git überspringen sie, in der CI (`CI=true`) scheitern sie.
- Zwei Commits mit gleichem Baum, Eltern, Nachricht und Zeit in derselben Sekunde sind derselbe Commit: ein Cherry-Pick auf den eigenen Elternteil ergibt im Test denselben Hash. Vorher den Branch weiterlaufen lassen (WP-09).
