# ADR 0016: Änderungen verwerfen und Stash

- Status: teilweise entschieden. **Entscheidungen des Nutzers (2026-11-28, WP-06)** – er hat den Plan mit diesen Empfehlungen freigegeben („Ja, so umsetzen“):
  - Verwerfen gibt es je nicht gestagter Datei und für alle; gestagte Änderungen bleiben unangetastet.
  - Geänderte getrackte Dateien werden mit Sicherung verworfen: ihre Änderungen kommen in einen Stash-Eintrag, aus dem „Anwenden“ sie zurückholt.
  - Neue Dateien kommen in den Papierkorb, nicht `git clean`.
  - Stash: Liste, anlegen (mit Nachricht, optional mit neuen Dateien), anwenden, Pop, löschen; Löschen fragt nach.
  - Kein Auto-Stash um den Auto-Pull (Backlog).
  - Das ist die zweite Ausnahme zu `CLAUDE.md` §2 nach ADR 0015.

  **Vorschlag (nicht bestätigt):** die Einzelheiten unten.
- Datum: 2026-11-28

## Kontext

`CLAUDE.md` §2 verbietet alles, was ungesicherte Arbeit verwerfen kann, darunter `restore` auf den Worktree, `clean` und `stash drop`. Ohne Verwerfen und Stash musste man für beides in IDE oder Terminal wechseln. §2 verlangt für eine Ausnahme eine eigene enge Methode in `GitClient`, eine Rückfrage, einen ADR und Tests.

## Entscheidung

### Verwerfen

- **Was:** die nicht gestagten Änderungen einer Datei (`StashCheck.CanDiscard`): eine geänderte, gelöschte oder neue Datei. Keine Konfliktdatei (die löst man), kein Repository im Worktree (seine Historie ginge mit in den Papierkorb), nichts Gestagtes.
- **Getrackte Dateien** (`GitClient.DiscardIntoStashAsync`): `stash push --keep-index --quiet -m "<Nachricht>" --pathspec-from-file=- --pathspec-file-nul`.
  - git legt einen Stash-Eintrag mit dem Stand dieser Dateien an und setzt sie auf ihren gestagten Stand zurück (ohne gestagte Änderung auf HEAD). Der Index bleibt.
  - Die Pfade kommen über stdin: ein Eintrag für beliebig viele Dateien, unabhängig von der Länge der Kommandozeile.
  - Jeder Pfad als `:(literal)<pfad>`: `[id].txt` trifft sonst auch `i.txt`. Nicht `--literal-pathspecs`, das an einen Hook weitergereicht würde (Regel `git.md`).
  - Die Nachricht ist „Von RepoBoard verworfen: <Dateien>“ (in der Sprache der App, auf 120 Zeichen gekürzt).
  - 3 min Zeitlimit (`GitRunner.CommitTimeout`): bricht git mittendrin ab, sind die Dateien halb zurückgesetzt.
- **Neue Dateien** über `IRecycleBin` (Core) in den Papierkorb. Den Papierkorb kennt nur der Host (`App/Services/ShellRecycleBin`):
  - `SHFileOperation` mit `FOF_ALLOWUNDO`, ohne Bestätigung, Fortschritt und Fehlerfenster (RepoBoard hat gefragt und meldet selbst).
  - `FOF_WANTNUKEWARNING`: Wo es keinen Papierkorb gibt (Netzlaufwerk), fragt Windows, bevor es endgültig löscht. Ein „Nein“ meldet RepoBoard als abgebrochen.
  - Auf einem eigenen STA-Thread, nie auf dem UI-Thread.
  - Ohne Papierkorb (Board ohne `IRecycleBin`) verwirft RepoBoard gar nichts und sagt warum.
- **Rückfrage** (`ConfirmDialog`): alle Dateien und wohin ihre Änderungen gehen – Stash-Liste, Papierkorb oder beides (`StashTools.DiscardText`).
- **Im Tor** (`BoardService.DiscardChangesAsync`), mit frischem Status:
  - Jede Datei der Rückfrage hat noch eine Änderung, die Verwerfen anbietet. Hat eine andere Session sie inzwischen gestaged oder committet, lehnt das Tor ab („seit der Rückfrage haben sich Dateien geändert“). Eine weiter bearbeitete Datei geht mit ihrem neuen Stand in den Stash bzw. Papierkorb – verloren geht nichts. Deshalb keine Prüfsumme je Datei (Abweichung vom Plan, der eine vorsah).
  - Getrackte Dateien nur mit einem Commit und ohne offenen Konflikt: git schreibt den Stash sonst nicht (`StashCheck.Discard`).
  - Alles, was der Papierkorb braucht (Pfad liegt im Repo, es gibt einen Papierkorb), wird geprüft, bevor etwas in den Stash geht.

### Stash

| Aktion | Befehl | Zeitlimit |
|---|---|---|
| Liste | `stash list -z --no-show-signature --format=%H%x00%ct%x00%gs` | 30 s |
| Dateien eines Eintrags | `diff --name-only -z <id>^1 <id>` und, mit neuen Dateien, `ls-tree -r --name-only -z <id>^3` | 30 s |
| Anlegen | `stash push --quiet [--include-untracked] [-m <nachricht>]` | 3 min |
| Anwenden | `stash apply --quiet <id>` | 3 min |
| Pop | Anwenden, dann Löschen – nur ohne Konflikt | 3 min |
| Löschen | `stash drop --quiet stash@{n}` | 30 s |

- **Ein Eintrag zählt nach seinem Commit** (`StashEntry.Id`), nicht nach seiner Position: legt eine andere Session einen Stash an, rutscht `stash@{0}` auf `stash@{1}`. Anwenden nimmt die Commit-ID. Pop und Löschen suchen die Position im Tor neu (`FindStashAsync`) – Pop nach dem Anwenden noch einmal. Ist der Eintrag weg, lehnt das Tor ab.
- Die Dateien eines Eintrags nicht über `stash show --include-untracked`, das git 2.32 braucht (RepoBoard verlangt 2.27).
- **Anlegen** prüft vorher (`StashCheck.Create`):
  - Es gibt einen Commit, es läuft keine Operation, es ist kein Konflikt offen.
  - Es gibt etwas zu sichern. git meldet sonst „No local changes to save“, legt nichts an und endet trotzdem mit 0 – „nur neue Dateien“ sagt, dass man „Mit neuen Dateien“ ankreuzen kann.
- **Anwenden und Pop:**
  - Nicht während einer Operation und nicht mit offenem Konflikt (`StashCheck.Apply`).
  - git weigert sich selbst, eine lokale Änderung oder eine neue Datei zu überschreiben.
  - Bei einem Konflikt bleibt der Eintrag. Die Dateien erscheinen als Konflikte der geöffneten Zeile (WP-07), Meldung `Stash_ApplyConflicts`.
- **Löschen** fragt nach und listet die Dateien des Eintrags.
- **Liste:** Das Board liest sie mit den Details einer geöffneten Zeile (`RepoDetails.Stashes`). Sie gehört dem Repository, alle Worktrees teilen sie (Tooltip am Kopf). Der Dateiwächter liest bei `refs/stash` (wie jede Ref) und bei `logs/refs/stash` neu: Wird ein älterer Eintrag gelöscht, ändert sich nur dieses Reflog.

### Oberfläche

- **Nicht gestaged:** „Alle verwerfen“ im Kopf der Gruppe; je Datei ein Knopf (Lucide `undo-2`), der beim Überfahren und bei Fokus erscheint (wie Mergen/Rebasen in der Branch-Liste). Gesperrt mit Grund im Tooltip.
- **Stash** unter der Commit-Box:
  - „Stash (n)“ und – mit Änderungen – Nachricht, „Mit neuen Dateien“ und „Stashen“.
  - Die Einträge mit Nachricht von git („On main: …“), Zeit, „Anwenden“, „Pop“ und Löschen (Papierkorb-Symbol).
  - Ein Klick auf einen Eintrag klappt seine Dateien auf.
  - Ohne Änderungen und ohne Einträge fehlt der Abschnitt.

## Folgen

- `IRecycleBin` ist eine neue Host-Schnittstelle in Core (`Core/IO`), weil das Board sie im Tor braucht. Bisher lagen Host-Schnittstellen in `UI/State`.
- Grenze, bewusst so: Eine Datei, die gestaged **und** danach weiter geändert war, kann beim Anwenden ihres Eintrags einen Konflikt geben. git wendet einen Stash gegen HEAD als Basis an, und der Index hat inzwischen dieselbe Zeile anders. Baut die verworfene Änderung auf der gestagten auf (der übliche Fall), geht es ohne Konflikt. Verloren geht nichts: Der Eintrag bleibt, die Konfliktdatei zeigt beide Seiten.
- Neue Dateien, die in den Papierkorb gegangen sind, holt man im Explorer zurück, nicht in RepoBoard.
- Offen (Backlog):
  - Auto-Stash um den Auto-Pull.
  - Stash mit `--index` anwenden (auch den Index zurückholen).
  - Nur einen Teil einer Datei verwerfen (Hunk, Zeilen).
  - Stash-Einträge umbenennen oder als Branch auschecken (`stash branch`).
