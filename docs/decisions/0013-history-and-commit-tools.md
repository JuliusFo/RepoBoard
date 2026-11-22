# ADR 0013: Historie als Graph, Commit-Details, Amend, Revert und Cherry-Pick

- Status: teilweise entschieden. **Entscheidungen des Nutzers (2026-11-22, WP-09):**
  - Den Graph zeichnet RepoBoard selbst als SVG, ohne Bibliothek.
  - Die Historie zeigt standardmäßig HEAD, ein Schalter „Alle Branches“ alle lokalen und Remote-Branches.
  - Bleibt ein Revert oder Cherry-Pick an einem Konflikt stehen, bleibt er für IDE oder Terminal stehen; RepoBoard bricht nichts selbst ab.
  - Die Commit-Details stehen in der geöffneten Zeile anstelle von Branches und Historie, wie der Diff.

  **Vorschlag (nicht bestätigt):** die Einzelheiten unten.
- Datum: 2026-11-22

## Kontext

Bis WP-09 zeigte die geöffnete Zeile die letzten 15 Commits als Liste, ohne Eltern und ohne Details. Gewünscht waren laut Roadmap Commit-Details mit Diff, ein Graph über mehrere Branches, Nachladen, Amend, Revert und Cherry-Pick. Drei dieser Werkzeuge schreiben: Amend ersetzt einen Commit, Revert und Cherry-Pick legen Commits an und können an Konflikten stehen bleiben. Für alle drei gilt `CLAUDE.md` §2: Lokale Arbeit geht nie verloren, und es gibt keinen Force-Push.

## Entscheidung

### Graph

- `git log --topo-order -n<N+1> --format=%H%x1f%h%x1f%P…` liefert die Commits, Kinder vor ihren Eltern. Das zusätzliche Commit zeigt nur an, ob es weitergeht. `HEAD --branches --remotes` steht nur mit „Alle Branches“ dabei. Immer mit `--` am Ende, sonst wäre eine Datei namens `HEAD` mehrdeutig. Dazu immer `--no-show-signature`, weil ein `log.showSignature` des Nutzers sonst in die Ausgabe schriebe.
- **Spurverteilung** über `CommitGraph.Build` (Core, rein):
  - Jede Spur wartet auf den Commit, zu dem sie führt.
  - Ein Commit nimmt die Spur, die auf ihn wartet; wartet keine, die erste freie.
  - Der erste Elternteil setzt die Spur fort. Weitere Eltern bekommen eine eigene oder die, die schon auf sie wartet.
  - Wartet eine Spur rechts auf denselben ersten Elternteil, wechselt sie herüber. So bleibt die Linie der ersten Eltern links, wie bei `git log --graph`.
  - Spuren rücken sonst nicht nach links. Eine Lücke nimmt der nächste Branch.
- **Zeichnen** in `UI/State/GraphDrawing` und `HistoryRow`:
  - Je Zeile ein SVG in voller Zeilenhöhe. Der Commit sitzt auf fester Höhe neben der ersten Textzeile, eine Linie zum Elternteil endet bei `100%`. So treffen sich die Linien benachbarter Zeilen, ohne dass eine Zeile die Höhe der anderen kennt.
  - Höchstens 8 Spuren, der Rest wird abgeschnitten.
  - Eine Linie zwischen zwei Spuren hat die Farbe der rechten Spur: der Branch, der dazukommt oder geht (E2E WP-09).
  - Sechs Farben aus den Tokens, hell und dunkel.
  - Jede Zeile ist eine Komponente mit `ShouldRender`, weil die Seite bei jeder Änderung rendert und bis zu 1 000 Commits zeigt.
- Nachladen in Schritten von 50, höchstens 1 000. Die Länge setzt sich beim Schließen der Zeile zurück, „Alle Branches“ bleibt bis zum Neustart.

### Commit-Details

- `git show -s --format=…%B` und `git show --format= --name-status -z -M --diff-merges=first-parent`. Ein Merge-Commit zeigt seine Änderungen gegen den ersten Elternteil, also was der Merge in den Branch gebracht hat (ein Hinweis in der Ansicht sagt das).
- Diff einer Datei: `git show --format= --no-ext-diff -M --diff-merges=first-parent <id> -- [<alt>] <pfad>` mit `--literal-pathspecs`, geparst von `FileDiff.Parse` (ADR 0008).
  - Vorher misst `cat-file --batch-check` beide Seiten über stdin. Über 5 MB wird der Diff nicht gelesen.
  - `--diff-merges` braucht git ≥ 2.31.
- **Commit-IDs** gehen nur an git, wenn sie volle Hex-IDs sind (`GitClient.IsCommitId`: 40 oder 64 Zeichen `0-9a-f`). So sind sie nie Option, Revision (`HEAD~1`), Bereich oder Ref – eine kurze ID löst git zuerst als Branch auf (Review WP-09).
- Nur lesend wie der Diff: kein Tor, Fehler stehen an Stelle der Details, Arbeit auf dem Thread-Pool.

### Amend

- `commit --amend -m <nachricht>` nimmt nur die gestagten Änderungen; anders als beim normalen Commit wird nichts automatisch gestaged.
- Erlaubt nur, wenn kein Remote-Branch HEAD enthält (`rev-list -n1 HEAD --not --remotes`). Im Tor wird das unmittelbar vor dem Aufruf erneut geprüft, dazu ob noch derselbe HEAD wie beim Vorfüllen und kein Merge/Rebase läuft (Review WP-09). „Unbekannt“ gilt als gepusht.
- Ein Force-Push kommt nie vor. Der alte Commit bleibt im Reflog.
- In der UI füllt das Häkchen „Letzten Commit ändern“ die Nachricht des letzten Commits vor (`UI/State/AmendDraft`). Ist HEAD gepusht, ist das Häkchen aus und der Tooltip nennt den Grund. Wird ein anderer Commit HEAD, solange es gesetzt ist, löst es sich mit einem Hinweis daneben (Review WP-09).

### Revert und Cherry-Pick

`git revert --no-edit <id>` bzw. `git cherry-pick <id>`. Vorher prüft RepoBoard im Tor, mit frischem Status und frisch gelesenem Git-Ordner:
- keine laufende Operation;
- keine gestagte, geänderte oder Konfliktdatei; neue Dateien sind erlaubt, denn git weigert sich, eine zu überschreiben – außer einer **ignorierten**: die überschreibt git ohne Rückfrage, daher keine ignorierte Datei an einem Pfad des Commits und keine an der Stelle eines Ordners, den er braucht (`foo` für `foo/bar`); ein ignorierter Ordner oberhalb (`node_modules`) stört nicht (`ls-files --others --ignored`, in Teilen gegen die Längengrenze der Kommandozeile; Review WP-09);
- kein Merge-Commit (der bräuchte `-m`);
- Revert nur für einen Commit in HEAD, Cherry-Pick nur für einen außerhalb;
- Cherry-Pick nicht, wenn HEAD dieselbe Änderung schon hat (`git cherry HEAD <id> <eltern>`, Patch-ID).

Der letzte Punkt kam aus dem E2E: Ein leerer Cherry-Pick bleibt sonst als unfertige Operation mit `CHERRY_PICK_HEAD` stehen. Ein leerer Revert scheitert dagegen ohne Rest.

`git cherry` erkennt nur 1:1-Kopien; nach einem Squash mehrerer Commits bleibt ein leerer Cherry-Pick trotzdem stehen – dann nennt die Meldung `--skip` (Entscheidung des Nutzers: nur die Meldung, kein automatisches `--skip`; Review WP-09). Ob die Änderung schon da ist, liest die Ansicht erst nach den Details (es kann dauern).

Weil der Worktree vorher sauber war, verliert ein späteres `--abort` nichts vom Nutzer. Bleibt git an einem Konflikt stehen, lässt RepoBoard den Zustand stehen (Entscheidung des Nutzers). Die Zeile zeigt die laufende Operation, und die Meldung nennt `--continue` bzw. `--abort`. Abbrechen und Fortsetzen in RepoBoard kommen mit WP-07.

Dieselben Prüfungen zeigt die UI vorab als gesperrten Knopf mit Grund (`UI/State/CommitTools`). Nach Erfolg schließt sich die Commit-Ansicht, und der neue Commit steht oben in der Historie.

## Folgen

- Kein neues Werkzeug, kein Paket; alle Befehle in `GitClient`, alle Prüfungen im Tor.
- Mit „Alle Branches“ und vielen Branches (Claude-Worktrees) wird der Graph breit, ab acht Spuren abgeschnitten. `--topo-order` geht ohne Commit-Graph-Datei die ganze Historie durch. Die Zeit bei sehr großen Repos ist noch nicht gemessen (Backlog).
- Revert und Cherry-Pick von Merge-Commits, Amend gepushter Commits (Force-Push) und Abbrechen/Fortsetzen fehlen bewusst.
