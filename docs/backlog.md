# Backlog

Ideen und Themen, die nicht zum aktuellen Paket gehören – nach Priorität und Bereich. Geplante Pakete: `docs/roadmap.md`. Neue Einträge in den passenden Bereich, erledigte entfernen. Die Liste unter „Ideen“ stammt aus dem Gespräch vom 2026-11-20 (Vorschläge von Claude, nach Priorität sortiert, noch nicht vom Nutzer ausgewählt).

## Bekannte Lücken

- Icon `Terminal` (Lucide `square-terminal`) ist nicht aus FerretSharp kopiert, sondern ohne Paket geschrieben – gegen `lucide-static` 1.55.0 prüfen (`wwwroot/lib/lucide/README.md`).
- Kein App-Icon (FerretSharp: `tools/icon/New-AppIcon.ps1`).
- Theme und Sprache folgen Windows; eine Einstellungsseite fehlt (ADR 0005).
- Ein Repo zu scannen heißt, seiner Konfiguration zu vertrauen (`core.fsmonitor`, Hooks laufen mit jedem `git status`, ADR 0002) – beim Scannen die Liste vorher bestätigen lassen?
- Fehlermeldungen von git können eine Remote-URL mit Token enthalten (`https://user:token@host`); vor Aktivitätsprotokoll und Log schwärzen.
- Status-Durchlauf alle 20 s über alle Repos: bei sehr vielen Repos messen (Prozessstarts), ggf. Takt anpassen oder Dateiwächter (Roadmap).
- WebView-Bild nach Ruhezustand/Dock/RDP: FerretSharp hat `WebViewCapture`/`CopyFence` (ADR 0038/0039 dort); hier nur das WebView2-Pin. Tritt es auf, von dort übernehmen.
- Ein Push ohne Remote `origin` (anderer Name) setzt keinen Upstream – Remote wählen lassen.

## Ideen (2026-11-20), nach Priorität

### Unbedingt nötig für den Alltag
- Tray-Icon, Autostart, Windows-Benachrichtigung bei Auto-Pull/Fehlern (Roadmap).
- Diff-Ansicht (Monaco aus FerretSharp als Diff-Editor), später Hunks/Zeilen stagen.
- Änderungen verwerfen (Rückfrage, ADR – `CLAUDE.md` §2), Stash (Liste, push/pop), optional Auto-Stash um den Auto-Pull je Repo.
- Merge/Rebase zu Ende bringen: Konfliktdateien, Abbrechen/Fortsetzen, Mergetool/VS Code öffnen.
- Dateiwächter statt Abfragen (Roadmap).
- Branches aufräumen: lokal/remote löschen, alle mit „gone“-Upstream auf einmal.
- Amend, Revert, Cherry-Pick, Commit-Details mit Dateien, Graph der Historie.
- „Jetzt anmelden“, wenn Auto-Fetch an fehlender Anmeldung scheitert (interaktiver Fetch).

### Sehr nützlich (alle Repos auf einmal)
- Worktrees als Unterzeilen, aufräumen (Roadmap).
- Gruppen (Arbeit, Privat, FerretSharp-Umfeld) mit Gruppenaktionen und Auto-Sync je Gruppe.
- „Alles aktualisieren“: alle sauberen, hinterherhängenden Repos fast-forwarden, Rest mit Grund überspringen.
- Feinere Auto-Sync-Regeln: Intervall je Repo, Auto-Pull nur für bestimmte Branches, Pause bei Akku/getakteter Verbindung.
- Abstand zu `origin/main` für Feature-Branches (nicht nur zum eigenen Upstream).
- Erinnerungen: ungepushte Commits seit N Tagen, Branch ohne Upstream.
- GitHub über `gh`: PR-Status je Branch (offen, CI, Reviews), PR anlegen, CI-Badge je Repo.
- Branch in mehreren Repos gleichzeitig anlegen/wechseln.
- Fehlende Repos klonen (GitHub-Repos des Nutzers, die lokal fehlen).
- Suche über alle Repos (Ctrl+K): Repos, Branches, Commit-Nachrichten.

### Nice to have und kreativ
- Kollisionswarnung vor dem Pull: nach dem Fetch per `git merge-tree` prüfen, ob die Remote-Änderungen mit lokalen kollidieren würden.
- „Neu seit deinem letzten Blick“: Ungelesen-Zähler je Repo mit Zusammenfassung der hereingezogenen Commits.
- Standup-Ansicht: was habe ich gestern in allen Repos committet.
- Feierabend-Check: Uncommittetes/Ungepushtes vor dem Herunterfahren oder zu einer Uhrzeit.
- Morgenroutine: bei der Anmeldung alles fetchen/fast-forwarden, Zusammenfassung zeigen.
- Commit-Nachricht per KI aus dem gestagten Diff (Conventional Commits).
- Kompaktmodus (schmal, immer im Vordergrund) oder Zähler am Taskleisten-Icon.
- Deep-Links `repoboard://open?repo=…` (wie die Links in FerretSharp).
- Aktivitäts-Heatmap über alle Repos.
- Repo-Pflege: Größe, `git maintenance`, LFS-Hinweise.
