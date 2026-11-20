# ADR 0002: Git über die installierte CLI, an genau einer Stelle

- Status: Vorschlag (nicht bestätigt) – so im Spike gebaut und übernommen
- Datum: 2026-11-20

## Kontext

Ein Git-Client kann git selbst implementieren (libgit2/LibGit2Sharp) oder die installierte `git.exe` aufrufen. Der Nutzer arbeitet mit Git for Windows, dem Git Credential Manager, SSH, Hooks und Worktrees (viele `.claude/worktrees`).

## Entscheidung

- RepoBoard ruft die **git-CLI** auf. Anmeldung (Credential Manager, SSH-Agent), Hooks, LFS, Includes und `safe.directory` verhalten sich dann genau wie im Terminal und in der IDE. libgit2 kennt weder den Credential Manager noch alle Protokolle und hinkt bei neuen Repo-Formaten hinterher.
- Nur `GitRunner` startet git: `ArgumentList` (keine Shell), Zeitlimit je Aufruf (lokal 30 s, Netzwerk 3 min), `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`, `core.quotepath=false`, keine Farben. `ArchitectureTests` hält fest, dass sonst nur der Host Prozesse startet.
- Jeder Befehl ist eine Methode in `GitClient`; gelesen werden nur maschinenlesbare Formate (`--porcelain=v2 -z`, `--format`). Werte von außen gehen nach `--` oder werden vorher geprüft (Branch-Namen: kein führendes `-`, `check-ref-format --branch`).

## Konsequenzen

- Ein Prozess je Aufruf: Bei vielen Repos kostet der Status-Durchlauf (alle 20 s) Prozessstarts; bis acht parallel. Wird das spürbar, zuerst den Takt oder einen Dateiwächter auf `.git` prüfen (Backlog).
- Ohne installiertes git geht nichts – die Meldung sagt das (`SyncText.GitMissing`).
- Die Konfiguration eines Repos (`core.fsmonitor`, Hooks) läuft bei jedem Aufruf mit: Ein Repo zu scannen heißt, ihm zu vertrauen (Backlog: Bestätigung beim Scannen).
