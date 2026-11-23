---
paths:
  - "src/RepoBoard.Core/Board/**"
  - "src/RepoBoard.Core/Sync/**"
  - "src/RepoBoard.Core/Watch/**"
  - "src/RepoBoard.Core/Repos/**"
  - "tests/RepoBoard.Core.Tests/Board/**"
  - "tests/RepoBoard.Core.Tests/Sync/**"
  - "tests/RepoBoard.Core.Tests/Watch/**"
  - "tests/RepoBoard.Core.Tests/Repos/**"
---

# Board, Auto-Sync, Dateiwächter, Scan

Invarianten: `CLAUDE.md` §2; Begründungen: ADR 0003 (Auto-Sync), ADR 0011 (Dateiwächter), ADR 0014 (Scan). Git-Aufrufe: `git.md`.

## Tore und Netzwerk
- Jede Operation läuft durch das Tor ihres Repos (`RepoState.Gate`); `RefreshAsync` und `AutoSyncAsync` überspringen ein belegtes Repo statt zu warten, Klicks warten – auch „mehr laden“/„alle Branches“ der Historie (`SetLogAsync`), sonst behielt ein laufendes Lesen die alte Historie (Review 0.4.0, A13).
- Netzwerk über die Familie (`RepoState.FamilyNetwork`: ein Repo und seine Worktrees schreiben dieselben Remote-Refs, sonst „cannot lock ref“) und dann `_network` (höchstens vier) – immer in dieser Reihenfolge (Review 0.4.0, A15, nachgestellt).
- Wer viele Repos auf einmal anstößt (Auto-Sync, „Alle fetchen“), startet nie mehr als Netzwerkplätze frei sind (`Parallel.ForEachAsync`), Auto-Sync einen weniger – ein Repo, das auf einen Platz wartet, hielt sonst sein Tor (Spinner, Commit gesperrt, kein Status) minutenlang (Review 0.4.0, A3).
- `Parallel.ForEachAsync` bricht sein eigenes Token beim ersten Fehler ab: nur über `EachAsync` (fängt je Repo, reicht das Token des Aufrufers weiter), sonst beendet der Fehler eines Repos git in den anderen mitten im Checkout (Code-Review R1).
- `RepoState`-Properties werden nur als Ganzes ersetzt (die UI liest sie von einem anderen Thread); keine Listen darin verändern.
- Zustand des Boards, den mehr als ein Thread ändert, liest und ändert unter einer Sperre: `UpdateSettingsAsync` kommt auch vom Thread-Pool (Update-Prüfung), sonst geht eine Änderung des Nutzers verloren (Review WP-10).
- Eine Board-Operation, die einen Diff liest und parst, läuft über `Task.Run` (`ReadDiffAsync`): Klicks kommen aus dem Renderer-Kontext, und ohne Thread-Wechsel parst sie auf dem UI-Thread (Review WP-05 und WP-25).
- Ein Ordner ohne `.git` ist „fehlt“ und bekommt keinen git-Aufruf: git suchte sonst nach oben und arbeitete im Repo darum herum (Review 0.4.0, A6).

## Fehler und Meldungen
- Ein Fehler steht in `RepoState.Error` bis zum nächsten Erfolg. Derselbe Fehler bei jedem Auto-Sync kommt nur einmal ins Aktivitätsprotokoll – verglichen mit `RepoState.LastAutoError`, nicht mit `Error`: der erfolgreiche Fetch davor leert `Error` (Review 0.4.0, A2). Ein übersprungener Auto-Pull nur, wenn sich der Grund ändert; ein gescheiterter Fetch lässt den Grund stehen.
- Die Regeln für Revert, Cherry-Pick und Amend stehen einmal in `PickCheck` – für die Knöpfe (`CommitTools`) und die Prüfung im Tor (Review 0.4.0, B2).
- Eine Aktion, die sich auf einen gezeigten Stand bezieht (Amend auf den Commit, dessen Nachricht vorgefüllt ist), nimmt diesen Stand mit und vergleicht ihn im Tor (`expectedHead`) – eine parallele Session committet zwischen Anzeige und Klick (Review WP-09).
- Vor allem, was Dateien oder Refs entfernt: was die Rückfrage geprüft hat, unmittelbar vor dem git-Aufruf noch einmal prüfen (Status, laufende Operation mit frisch gelesenem Git-Ordner, Commits eines detached HEAD ohne Ref, Liste dessen, was mitgelöscht wird) – zwischen Frage und Klick arbeitet eine Session weiter (Review WP-04).
- Texte für den Nutzer aus `SyncText`, nie zusammengeklebt (`TextFormat`, Plural über `CommitsOne`/`CommitsOther`).

## Dateiwächter und Takt
- Ein lesender git-Aufruf kann trotzdem in den Git-Ordner schreiben – mit Erweiterungen der Repo-Konfiguration: fsmonitor legt bei jedem `status` ein Cookie an, Git LFS Dateien in `lfs/tmp` (Review WP-03). Der Dateiwächter (ADR 0011) zählt im Git-Ordner daher nur eine Positivliste (`WatchMap.StateFiles`/`StateFolders`), nie „alles außer …“; Tests mit Wächtern auch mit `core.fsmonitor=true` (`RepoWatchTests.Reading_the_status_does_not_read_it_again`).
- Ein übersprungenes Lesen kommt mit der Zeit seiner Änderung zurück (`ChangeDebouncer.Retry`), nicht mit der des neuen Versuchs: das Lesen am Ende der Operation deckt es dann schon ab (Review 0.4.0, A14). Der Takt liest ein Repo ohne Wächter erst, wenn sein Status `UnwatchedRefresh` alt ist, und wartet nie auf den Auto-Sync (offline dauert der Minuten; Code-Review R1).

## Pfade und Scan
- „Derselbe Pfad?“ und „liegt darunter?“ nur über `Core/IO/Paths`; die Konfiguration hält volle Pfade (Review 0.4.0, B3, A23).
- Ein Scan startet kein git: `RepoScanner.Inspect` liest aus dem Git-Ordner, was die Konfiguration des Repos ausführen würde, und der Nutzer hakt an (ADR 0014). Harmlos ist nur, was die Positivliste kennt – eine Liste der gefährlichen Schlüssel verfehlte Filter, Diff-Treiber und LFS-Agenten. Alles, was ein git-Aufruf im Repo liest, gehört dazu: `config.worktree`, die Submodule (Code-Review R1).
- Ein Pfad, der aus einer Datei im Repo kommt (Worktree-Einträge, `gitdir:`, `commondir`, `.gitmodules`, ein `.git`-Link), ist nicht vom Nutzer gewählt: nichts auf einer fremden Freigabe lesen, solange das Repo nicht selbst auf einer liegt – Lesen schickt eine Anmeldung an den Host. Links nur Schritt für Schritt auflösen (`LinkTarget`), nie über `ResolveLinkTarget` (öffnet das letzte Ziel); was durch einen Link liest (`ReadAllLines`, `EnumerateFiles`), meldet sich am Ziel an. Ein Test der Liste nimmt, was git selbst schreibt (`submodule add` schreibt `core.worktree`), nicht eine Datei von Hand (Code-Review R1).
