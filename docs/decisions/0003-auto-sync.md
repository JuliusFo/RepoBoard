# ADR 0003: Auto-Fetch und Auto-Pull – nur was keine Arbeit kosten kann

- Status: akzeptiert. **Entscheidung des Nutzers (2026-11-22, Review 0.4.0):** die Sicherheitsregeln unten, das gemeinsame Intervall und der Takt von 20 s sind bestätigt. Vorher teilweise entschieden: **Entscheidung des Nutzers (2026-11-20):** Auto-Fetch und Auto-Pull je Repo, ein- und ausschaltbar, „damit die auf jeden Fall immer auf dem aktuellen Stand sind“. **Vorschlag (nicht bestätigt):** die Sicherheitsregeln unten, ein gemeinsames Intervall, der Takt von 20 s.
- Datum: 2026-11-20

## Kontext

Ein Pull, den niemand ansieht, darf nichts kaputt machen: keine Merge-Commits, keine Konflikte, keine verlorenen lokalen Änderungen, keine Anmeldefenster, die im Hintergrund auf niemanden warten.

## Entscheidung

- **Auto-Fetch:** `git fetch --all --prune` im Hintergrund; danach zeigt die Übersicht, wie weit ein Repo hinterher ist.
- **Auto-Pull** schließt Auto-Fetch ein (an schaltet beides an, Auto-Fetch aus schaltet beides aus). Nach dem Fetch nur `git merge --ff-only @{u}` – und nur, wenn `PullCheck` nichts findet: Branch ausgecheckt (kein detached HEAD), Upstream vorhanden, kein Merge/Rebase/Cherry-Pick/Revert offen, keine Konflikte, **nichts lokal voraus** (sonst divergiert der Branch, das muss der Nutzer selbst lösen). Lokale Änderungen bleiben; überschneidet sich der Fast-Forward mit ihnen, bricht git ab und RepoBoard meldet es.
- **Ohne Anmeldung:** Hintergrundaufrufe setzen `GCM_INTERACTIVE=never` und `SSH_ASKPASS_REQUIRE=never`. Fehlt eine Anmeldung, scheitert der Fetch; ein Klick auf „Fetch“ (interaktiv) holt sie nach.
- **Takt:** `AutoSyncService` alle 20 s – Status aller Repos lesen (seit WP-03 nur die ohne Dateiwächter, beobachtete alle 2 min als Rückfall, ADR 0011), dann die Repos synchronisieren, deren Intervall (1–60 min, Standard 5) abgelaufen ist. Ein Repo, das gerade etwas tut, wird übersprungen.
- **Melden:** neue Commits und jeder Auto-Pull ins Aktivitätsprotokoll; derselbe Fehler und derselbe Grund für einen übersprungenen Auto-Pull nur einmal.

## Konsequenzen

- „Immer aktuell“ gilt nur, solange RepoBoard läuft: seit WP-02 läuft es nach dem Schließen im Infobereich weiter und startet auf Wunsch mit Windows (ADR 0006). Auto-Sync lässt sich bis zum Neustart pausieren.
- Ein Branch mit eigenen, ungepushten Commits wird nie automatisch aktualisiert; die Übersicht zeigt ihn als divergiert.
- `AutoSyncTests` (gegen echte Repos) und `PullCheckTests` halten jeden Fall fest.

## Nachtrag Review 0.4.0 (2026-11-22, R1)

- Der Fast-Forward ist `git merge --ff-only --no-autostash @{u}`, der Pull auf Klick `pull --ff-only --no-rebase --no-autostash`: mit dem `merge.autoStash` (bzw. `pull.rebase` + `rebase.autoStash`) des Nutzers stashte git überschneidende lokale Änderungen, spulte vor und legte sie mit Konfliktmarkern zurück – mit Exit 0 (nachgestellt). Ohne Autostash bricht git ab, wie diese ADR verspricht.
- Der Fast-Forward läuft als Netzwerkaufruf (3 min, im Hintergrund ohne Anmeldung): LFS, Partial Clone und `post-merge`-Hooks laden beim Auschecken nach.
- „Derselbe Fehler nur einmal“ vergleicht mit dem zuletzt von Auto-Sync gemeldeten Fehler (`RepoState.LastAutoError`), nicht mit dem der Zeile – der erfolgreiche Fetch davor leerte ihn, und ein scheiternder Fast-Forward kam bei jedem Intervall neu.
- Auto-Sync startet höchstens so viele Repos gleichzeitig, wie Netzwerkplätze frei sind, einen weniger (einer bleibt für Klicks); jedes setzt seine Zeit beim Start, die Intervalle verteilen sich so von selbst. Netzwerkoperationen eines Repos und seiner Worktrees laufen nacheinander (gemeinsame Remote-Refs).
- Bei einem gestoppten Revert spult git selbst vor (`REVERT_HEAD` hält ihn nicht auf): dort ist `PullCheck` der einzige Schutz – ein Test hält es fest.
