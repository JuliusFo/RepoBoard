# Roadmap – geplante Pakete und offene Fragen

Geplante Pakete mit den Entscheidungen des Nutzers. Eine Reihenfolge gibt es nicht: Welches Paket als Nächstes kommt, entscheidet der Nutzer jeweils selbst; festgehalten werden nur Abhängigkeiten („braucht WP-NN“). Beim Start eines Pakets den Abschnitt lesen, die Fragen unter „Offen“ mit dem Nutzer klären, dann den Plan abstimmen. Ist das Paket fertig, wandert sein Abschnitt als „Planung“ in die Paketdatei unter `docs/work-packages/`. Nicht eingeplante Ideen und bekannte Lücken: `docs/backlog.md`.

## Pakete aus der Ideenliste vom 2026-11-20

Der Nutzer wollte die Ideenliste als Pakete abgelegt haben (2026-11-20). Zuschnitt, Inhalt und die Gruppierung nach Priorität sind **Vorschläge von Claude (nicht bestätigt)**; die Nummern sind nur Kennungen, keine Reihenfolge. Was ein Paket ändert, das ungesicherte Arbeit verwerfen kann, braucht nach `CLAUDE.md` §2 eine eigene enge Methode in `GitClient`, eine Rückfrage, einen ADR und Tests – das steht bei den betroffenen Paketen als „Invariante“.

### Gruppe A – nötig für den Alltag

#### WP-08 Branches aufräumen
Branch `wp/08-branch-cleanup`.
- Lokalen Branch löschen (`branch -d`, nur gemergt), Remote-Branch löschen (`push --delete`), alle Branches mit Upstream „gone“ auf einmal (Liste, Häkchen).
- Invariante: `branch -D` (ungemergt) nur mit eigener Rückfrage, ADR.
- Offen: auch über alle Repos auf einmal („gone“-Branches überall)?

### Gruppe B – sehr nützlich, weil RepoBoard alle Repos sieht

#### WP-11 Gruppen und „Alles aktualisieren“
Branch `wp/11-groups`.
- Repos in Gruppen (z. B. Arbeit, Privat, FerretSharp-Umfeld), zuklappbar, Reihenfolge per Ziehen; Fetch/Pull/Auto-Sync je Gruppe.
- „Alles aktualisieren“: alle sauberen, nur hinterherhängenden Repos fast-forwarden (dieselben Regeln wie `PullCheck`), Rest mit Grund überspringen, Ergebnis als Liste.
- Konfiguration Version 2 (Migration aus Version 1).
- Offen: Gruppen von Hand oder automatisch nach Ordner? Ein Repo in mehreren Gruppen?

#### WP-12 Feinere Auto-Sync-Regeln und Anmeldung nachholen
Branch `wp/12-sync-rules`.
- Intervall je Repo (statt nur global), Auto-Pull nur für bestimmte Branches (z. B. `main`, `develop`), Pause bei Akkubetrieb oder getakteter Verbindung.
- Scheitert Auto-Fetch an fehlender Anmeldung: Knopf „Jetzt anmelden“ (interaktiver Fetch) an der Fehlerzeile.
- Braucht WP-11, falls Regeln auch je Gruppe gelten sollen. Offen: welche Regeln wirklich gebraucht werden.

#### WP-13 Abstand zum Hauptbranch und Erinnerungen
Branch `wp/13-reminders`.
- Für Feature-Branches: wie weit hinter/vor `origin/main` (bzw. dem Standardbranch des Remotes), nicht nur zum eigenen Upstream.
- Erinnerungen: ungepushte Commits seit N Tagen, Branch ohne Upstream, lange nicht gefetcht – als Chip „Probleme“ bzw. Hinweis.
- Offen: Schwellen (Tage) einstellbar? Mit WP-02 auch als Benachrichtigung?

#### WP-14 GitHub-Anbindung
Branch `wp/14-github`.
- Über `gh` (Anmeldung des Nutzers): PR-Status je Branch (offen, Entwurf, CI grün/rot, Reviews), Link zum PR, „PR anlegen“, CI-Status des Standardbranches je Repo.
- Abfragen sparsam (Rate-Limit), im Hintergrund, nur für Repos mit GitHub-Remote.
- Offen: nur GitHub oder auch Azure DevOps/GitLab? `gh` voraussetzen oder REST mit eigenem Token (Credential Manager)?

#### WP-15 Mehrere Repos zugleich: Branches, Klonen
Branch `wp/15-multi-repo`.
- Branch in mehreren ausgewählten Repos anlegen bzw. wechseln (Auswahl per Häkchen), Ergebnis je Repo.
- Fehlende Repos klonen: GitHub-Repos des Nutzers (über WP-14), die lokal fehlen, mit Zielordner.
- Klonen braucht WP-14. Offen: Mehrfachauswahl in der Tabelle generell (auch für Fetch/Pull)?

#### WP-16 Suche und Befehlspalette
Branch `wp/16-command-palette`.
- Ctrl+K: Repos, Branches, Aktionen; Suche in Commit-Nachrichten über alle Repos (`git log --grep`, im Hintergrund, abbrechbar).
- Tastenkürzel einstellbar wie in FerretSharp (WP-25 dort: keine Standardtasten außer bewusst entschiedenen).
- Offen: welche Aktionen in die Palette, welche Kürzel.

### Gruppe C – nice to have und kreativ

#### WP-17 Kollisionswarnung vor dem Pull
Branch `wp/17-conflict-preview`.
- Nach jedem Fetch mit `git merge-tree --write-tree` (git ≥ 2.38) still prüfen, ob die Remote-Änderungen mit lokalen Commits oder geänderten Dateien kollidieren würden; Warnung an der Zeile, bevor gepullt wird.
- Ergänzt ADR 0003 (Auto-Pull überspringt dann mit konkretem Grund und Dateiliste).
- Offen: auch für divergierte Branches (Vorschau eines Merges)?

#### WP-18 „Neu seit deinem letzten Blick“
Branch `wp/18-unread`.
- Je Repo ein Ungelesen-Zähler der hereingezogenen bzw. gefetchten Commits seit dem letzten Öffnen der Zeile, mit kurzer Liste (Autor, Betreff).
- Zustand „zuletzt gesehen“ je Repo in einer eigenen Datei (`AtomicJsonFile`).
- Mit WP-09 (erledigt) als Sprung in die Commit-Details.

#### WP-19 Tagesrückblick: Standup, Feierabend, Morgen
Branch `wp/19-daily`.
- Standup-Ansicht: eigene Commits von gestern bzw. seit dem letzten Arbeitstag über alle Repos, gruppiert, kopierbar.
- Feierabend-Check: zu einer Uhrzeit oder beim Abmelden Hinweis auf Uncommittetes und Ungepushtes.
- Morgenroutine: bei der Anmeldung alles fetchen und sicher fast-forwarden, Zusammenfassung.
- Baut auf WP-02 auf (Infobereich, Benachrichtigungen, Autostart – erledigt). Offen: Identität „eigene Commits“ (E-Mail aus der Git-Konfiguration)?

#### WP-20 Commit-Nachricht per KI
Branch `wp/20-ai-commit-message`.
- Vorschlag einer Commit-Nachricht aus dem gestagten Diff im Stil Conventional Commits, editierbar.
- Offen: Anbieter, API-Schlüssel (Credential Manager), was den Rechner verlässt (Diff an einen Dienst – ausdrücklich je Repo erlauben?). Vor dem Start ADR.

#### WP-21 Kompaktansicht und Taskleiste
Branch `wp/21-compact`.
- Schmale Ansicht, wahlweise immer im Vordergrund; Zähler am Taskleisten-Symbol (z. B. Repos hinterher oder mit Problemen).
- Offen: eigener Fenstermodus oder zweites Fenster?

#### WP-22 Deep-Links
Branch `wp/22-links`.
- `repoboard://open?repo=…` öffnet ein Repo in RepoBoard (aus Browser, PR-Mail, Notizen), wie die Links in FerretSharp (ADR 0026 dort: Registrierung, Pipe an die laufende Instanz, Rückfrage).
- Braucht WP-10 (der Installer registriert das Protokoll; WP-10 ist erledigt, die Registrierung kommt mit diesem Paket über Velopacks Hooks wie in FerretSharp).

#### WP-23 Aktivitäts-Heatmap
Branch `wp/23-heatmap`.
- Zeitleiste bzw. Heatmap: wann in welchem Repo committet wurde (eigene Commits oder alle), über Wochen.
- Offen: als eigener Bereich der Seite oder Dialog?

#### WP-24 Repo-Pflege
Branch `wp/24-maintenance`.
- Größe je Repo (`.git`, Worktree), `git maintenance` ein/aus, `gc`, Hinweise zu LFS (fehlende Objekte, große Dateien ohne LFS).
- Offen: nur anzeigen oder auch ausführen?
