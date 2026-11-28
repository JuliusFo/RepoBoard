# ADR 0014: Merge, Rebase und angehaltene Operationen zu Ende bringen

- Status: teilweise entschieden. **Entscheidungen des Nutzers (2026-11-28, WP-07)** – er hat den Plan mit diesen Empfehlungen freigegeben:
  - Kein Standard beim Zusammenführen: „Mergen“ und „Rebasen“ sind zwei Aktionen am Branch.
  - Abbrechen (`--abort`) und Überspringen (`--skip`) gibt es, beide mit Rückfrage. Das ist die Ausnahme zu `CLAUDE.md` §2.
  - Fortsetzen übernimmt die Nachricht, die git vorbereitet hat; sie zu bearbeiten kommt später (Backlog).
  - Ein Rebase schreibt keine Commits um, die ein Remote schon hat (kein Force-Push).
  - Konfliktdateien öffnen sich in VS Code; `git mergetool` kommt nicht in dieses Paket (Backlog).
  - „Unsere/ihre Version nehmen“ (`checkout --ours/--theirs`) kommt nicht in dieses Paket (Backlog).
  - „Als gelöst markieren“ fragt nach, wenn git in der Datei noch Konfliktmarker findet.

  **Vorschlag (nicht bestätigt):** die Einzelheiten unten.
- Datum: 2026-11-28

## Kontext

Bis WP-07 erkannte RepoBoard eine angehaltene Operation (Merge, Rebase, Cherry-Pick, Revert) nur an ihren Markerdateien: Die Zeile zeigte „Merge läuft“, Auto-Pull blieb weg. Zu Ende bringen musste man sie in IDE oder Terminal – auch die Reverts und Cherry-Picks, die RepoBoard seit WP-09 selbst startet (ADR 0013). Einen Branch zusammenführen konnte RepoBoard nur als Fast-Forward (Pull).

`CLAUDE.md` §2 verbietet alles, was ungesicherte Arbeit verwerfen kann. `--abort` setzt Index und Dateien auf den Stand vor der Operation zurück, und `--skip` verwirft die Lösung des aktuellen Commits. Beides braucht laut §2 eine eigene enge Methode, eine Rückfrage, einen ADR und Tests.

## Entscheidung

### Was läuft: `OperationInfo`

`OperationInfo.Read(gitDir)` liest nur gits eigene Dateien:
- Merge, Cherry-Pick, Revert: den Commit aus `MERGE_HEAD`, `CHERRY_PICK_HEAD` bzw. `REVERT_HEAD` und die erste Zeile von `MERGE_MSG`.
- Rebase: aus `rebase-merge` (Standard, auch `-i`) bzw. `rebase-apply` den Branch (`head-name`), den Schritt (`msgnum`/`end` bzw. `next`/`last`), den Commit, an dem er steht (`REBASE_HEAD`), und dessen Betreff (`message`).

Dazu kommt ein **Schlüssel** je Operation und Schritt: der hereinkommende Commit, beim Rebase der Stand des Branches davor plus Schritt plus Commit. Abbrechen und Überspringen nehmen den Schlüssel aus der Rückfrage mit; das Tor vergleicht ihn (wie `expectedHead` bei Amend, Review WP-09). Kann eine Datei gerade nicht gelesen werden, gilt die Operation trotzdem als laufend, mit einem Schlüssel, den kein Klick trifft. `RepoState.Unfinished` hält das Ergebnis, `RepoState.Operation` ist nur noch die Art.

### Befehle (`GitClient`)

| Aktion | Befehl | Zeitlimit |
|---|---|---|
| Mergen | `merge --no-edit <branch>` | 3 min |
| Rebasen | `rebase refs/heads/<x>` bzw. `refs/remotes/<x>` | 3 min |
| Fortsetzen | `<op> --continue` | 3 min |
| Überspringen | `<op> --skip` (nicht bei Merge) | 3 min |
| Abbrechen | `<op> --abort` | 30 s |
| Marker prüfen | `-c core.whitespace=-… diff --check -- <pfad>` | 30 s |
| Als gelöst markieren | `add -A -- <pfad>` (wie Stagen) | 30 s |

- **Kein Editor:** `GitRunner` setzt `GIT_EDITOR=true` für jeden Aufruf. Sonst öffnet `--continue` einen Editor für die Nachricht und wartet, bis ihn jemand schließt. `true` übernimmt die vorbereitete Nachricht.
- **3 min** (`GitRunner.CommitTimeout`) für Befehle, die Commits schreiben und jeden signieren können. Wartet pinentry auf die Passphrase, bräche das 30-s-Limit git mitten im Rebase ab.
- **Merge-Name:** Der kurze Name kommt in gits Nachricht („Merge branch 'feature'“). Er wird über `IsBranchNameAsync` geprüft und nur genommen, wenn er auf denselben Commit zeigt wie der volle Ref. Sonst würde ein gleichnamiger Tag gewinnen, und RepoBoard nimmt den vollen Ref (dann lautet die Nachricht „Merge branch 'refs/heads/…'“).
- **Marker:** `diff --check` kennt `conflict-marker-size`. Die Whitespace-Regeln sind abgeschaltet, sonst zählte ein Leerzeichen am Zeilenende als Fund.

### Prüfungen im Tor (`BoardService`)

Alles mit frischem Status und frisch gelesenem Git-Ordner, unmittelbar vor dem git-Aufruf:
- **Mergen und Rebasen** (wie Revert und Cherry-Pick, ADR 0013):
  - Ein Branch ist ausgecheckt, er hat mindestens einen Commit, und es läuft keine Operation.
  - Keine gestagte, geänderte oder Konfliktdatei. Neue Dateien dürfen bleiben, git weigert sich, eine zu überschreiben.
  - Keine ignorierte Datei dort, wo der Merge (`diff HEAD...<branch>`) bzw. der Rebase (`diff HEAD <branch>`) etwas ändert (`IgnoredInTheWayAsync`).
  - So verliert ein späteres Abbrechen nichts vom Nutzer.
- **Rebase zusätzlich:** Keiner der Commits `<branch>..HEAD` liegt auf einem Remote (`rev-list --count` mit und ohne `--not --remotes`). Sonst bräuchte der nächste Push einen Force-Push.
- **Fortsetzen:**
  - Es ist noch dieselbe Art von Operation, und kein Konflikt ist offen.
  - Beim Rebase ist nichts außerhalb des Index geändert; git würde sonst ablehnen („You must edit all merge conflicts“).
- **Abbrechen und Überspringen:**
  - Der Schlüssel passt noch.
  - Die geänderten Dateien sind noch genau die, die die Rückfrage gezeigt hat (`BoardService.DiscardedFiles`: alle Änderungen außer neuen Dateien). Eine andere Session könnte weitergemacht haben (Review WP-04).

Hält git nach einem Start, Fortsetzen oder Überspringen wieder an (der nächste Commit eines Rebase hat auch einen Konflikt), meldet die Zeile das mit `Operation_Stopped` und gits Meldung. Die Leiste zeigt den neuen Schritt.

### Oberfläche

- **Leiste** über den Änderungen der geöffneten Zeile, solange eine Operation läuft:
  - Was läuft und wo sie steht („Rebase von feature läuft – Commit 2 von 5: …“), dazu „noch n Konflikte“ bzw. „Alle Konflikte gelöst“.
  - Knöpfe „Fortsetzen“ (gesperrt mit Grund, `OperationTools.ContinueBlocker`), „Überspringen“ (nicht bei Merge) und „Abbrechen“.
  - Abbrechen und Überspringen fragen nach (`ConfirmDialog`) und listen jede geänderte Datei, die zurückgehen kann. Das ist der ungünstigste Fall: Eine eigene Änderung an einer Datei, die die Operation nicht berührt, behält git (`merge --abort` ist `reset --merge`; E2E WP-07). Neue Dateien bleiben. Der Knopf zum Schließen heißt hier „Zurück“, denn „Abbrechen“ neben „Merge abbrechen“ läse sich wie dasselbe (E2E WP-07).
- **Gruppe „Konflikte (n)“** über Gestagt und Nicht gestaged:
  - Je Datei die Art des Konflikts als Tooltip (wie `git status`: beide geändert, von uns gelöscht …), „Öffnen“ (VS Code mit Repo und Datei, dort auch der Merge-Editor) und „Gelöst“.
  - Konfliktdateien haben kein Häkchen mehr. „Alle stagen“ und „Alle unstagen“ lassen sie aus, solange Konflikte offen sind. Unstagen setzte die Version aus HEAD in den Index, und „Fortsetzen“ ließe die andere Seite fallen.
- **Commit-Box** gesperrt, solange eine Operation läuft, mit dem Hinweis „mit ‚Fortsetzen‘ abschließen“. Ein Commit mitten im Rebase wäre einer zu viel.
- **Branch-Liste:** „Mergen“ und „Rebasen“ erscheinen beim Überfahren und bei Fokus (sonst wäre die Liste voller Knöpfe), gesperrt mit Grund (`OperationTools.MergeBlocker`). „Wechseln“ bleibt immer sichtbar.
- Mergen und Rebasen fragen nicht nach: Sie laufen nur mit sauberem Worktree, und Abbrechen bringt den Stand von vorher zurück.

## Folgen

- Keine neue Bibliothek. Alle Befehle stehen in `GitClient`, alle Prüfungen im Tor.
- ADR 0013 ist ergänzt: Revert und Cherry-Pick, die an einem Konflikt stehen bleiben, schließt man jetzt in der Zeile ab. Der leere Cherry-Pick nach einem Squash endet mit „Überspringen“.
- `GIT_EDITOR=true` gilt für jeden Aufruf von RepoBoard, auch für Hooks, die git startet. Ein Hook, der selbst einen Editor öffnen will, bekommt keinen.
- Offen (Backlog):
  - `git mergetool`, „unsere/ihre Version“ und die Nachricht beim Fortsetzen bearbeiten.
  - Ein Diff der Konfliktdatei (combined diff).
  - Merge und Rebase in mehreren Repos auf einmal.
