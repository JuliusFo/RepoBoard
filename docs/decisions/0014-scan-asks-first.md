# ADR 0014: Ein Scan fragt, bevor git in seinen Funden läuft

- Status: akzeptiert (Entscheidung des Nutzers, 2026-11-22, Review 0.4.0, A9)
- Datum: 2026-11-22
- Paket: R1 (`docs/work-packages/r1-review-0.4.md`)

## Kontext

RepoBoard ruft git mit der Konfiguration des Repos auf (ADR 0002): `status` startet ein `core.fsmonitor`, Fetch und Push ein `core.sshCommand`, Auto-Pull den `post-merge`-Hook. Ein Repo hinzuzufügen heißt also, seiner Konfiguration zu vertrauen. Bis 0.4.0 fügte „Ordner scannen“ jeden Fund sofort hinzu und las gleich danach seinen Status – ein entpacktes Archiv mit `.git/config` und `core.fsmonitor = <Programm>` hätte beim Scan das Programm gestartet, ohne dass der Nutzer das Repo je gesehen hätte. Seit WP-04 reichte das weiter: ein verknüpfter Worktree im Ordner setzt sein Hauptrepo aufs Board, auch außerhalb des gescannten Ordners, und Worktree-Einträge aus `.git/worktrees/*/gitdir` wurden eigene Zeilen. Geklonte Repos sind nicht betroffen (`.git` lässt sich nicht einchecken).

## Entscheidung

- Der Scan startet kein git. `RepoScanner.Inspect` liest aus dem Git-Ordner (auch hinter einer `.git`-Datei und über `commondir`), was die Konfiguration ausführen würde: `core.fsmonitor` (außer `true`/`false`, git’s eigener Dienst), `core.hooksPath`, `core.sshCommand`, eigene Hooks in `hooks/` (nicht `*.sample`), `include`/`includeIf` (kann alles davon setzen). Dazu, ob das Repo eines Worktrees außerhalb des Ordners liegt.
- Ein Dialog listet die Funde, die noch nicht auf dem Board sind; je Fund die Hinweise. Angehakt ist zu Beginn nur ein Repo im Ordner, das nichts Eigenes ausführt; der Nutzer hakt die anderen bewusst an. Erst „Hinzufügen“ ruft git dort auf.
- Ein Worktree-Eintrag auf einer Freigabe (`\\host\…`) wird nur übernommen, wenn das Repo selbst auf einer Freigabe liegt: sonst schickte jeder Status eine Anmeldung an einen Host, den eine Datei im Repo genannt hat.
- Das README sagt es in einem Satz: ein Repo hinzuzufügen heißt, seiner Konfiguration zu vertrauen.

## Konsequenzen

- Ein Scan zeigt jetzt immer einen Dialog, wenn er etwas Neues findet; „nichts Neues“ steht wie bisher in der Aktivität.
- „Hinzufügen“ eines einzelnen Ordners fragt nicht: den hat der Nutzer selbst gewählt.
- Die Leseregeln für git’s Konfiguration sind bewusst einfach (Abschnitt `[core]`, Schlüssel ohne Groß-/Kleinschreibung, `#`/`;` als Kommentar); eine Datei, die git anders liest, kann einen Hinweis verfehlen – deshalb auch der Hinweis auf eingebundene Konfiguration.
