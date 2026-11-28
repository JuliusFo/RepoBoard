# Backlog

Ideen und Themen, die nicht zum aktuellen Paket gehören – nach Bereich. Geplante Pakete: `docs/roadmap.md`. Neue Einträge in den passenden Bereich, erledigte entfernen; was ein Paket offen ließ, steht hier als „Fortsetzung von WP-NN“.

## Bekannte Lücken

- Icon `Terminal` (Lucide `square-terminal`) ist nicht aus FerretSharp kopiert, sondern ohne Paket geschrieben – gegen `lucide-static` 1.55.0 prüfen (`wwwroot/lib/lucide/README.md`).
- Programmsymbol (WP-02, `tools/icon/New-AppIcon.ps1`) ist ein schlichter Entwurf – WP-10 hat es nicht überarbeitet; es steht jetzt auch in `Setup.exe` und im Startmenü.
- Die Kopfzeile passt bei 1440 px Fensterbreite nicht ganz: der Einstellungsknopf liegt am Rand (WP-10 E2E, schon vorher so); mit dem Update-Knopf fehlen ~200 px. Chips oder Suche schmaler, Knöpfe nur als Symbol, oder eine zweite Zeile.
- Installer und Updates unsigniert (WP-10, ADR 0007): SmartScreen warnt; Signieren wie in FerretSharp (Kosten, SignPath) im Blick behalten.
- Theme und Sprache folgen Windows; eine Einstellungsseite fehlt (ADR 0005). Passt zu WP-12 oder WP-16, falls dort Einstellungen dazukommen.
- Schwärzen in git-Meldungen (`GitSecrets`, vor v0.1.0) erkennt Tokens ohne festes Präfix nur in einer URL: ein Azure-DevOps-PAT oder ein anderes Geheimnis, das ein Server frei in eine `remote:`-Zeile schreibt, bliebe sichtbar. Weitere Muster ergänzen, wenn eines auftaucht.
- Beenden während eines Auto-Pulls bricht `git merge --ff-only` nach 3 s ab (`host.StopAsync`, `GitRunner` beendet den Prozessbaum): lokale Arbeit bleibt, aber ein halb aktualisierter Worktree oder `index.lock` kann liegen bleiben. Seit `--exit` (WP-02) gibt es dafür einen Auslöser von außen, seit WP-10 auch „Jetzt neu starten“ in der Kopfzeile (ein Klick ohne Rückfrage); laufende Operationen beim Beenden bis zum Zeitlimit zu Ende laufen lassen (Review WP-02, vorher schon da).
- Repos ohne Dateiwächter (Netzlaufwerk, inotify-Grenze, Fehler) liest der Takt weiter alle 20 s (ADR 0011): bei sehr vielen solchen Repos messen.
- WebView-Bild nach Ruhezustand/Dock/RDP: Seit ADR 0009 gibt es `WebViewCapture` mit `CopyFence` (Bild-Teil aus FerretSharp); der Neustart der Aufnahme nach Ruhezustand, Sitzungswechsel, zurückkehrendem Front Buffer und Wiederherstellen aus minimiert sowie die Software-Anzeige ohne Front Buffer (RDP) fehlen noch (FerretSharp ADR 0038 mit Nachträgen). Tritt ein stehendes Bild auf, von dort in `WebViewCapture` übernehmen.
- Ein Klick von 70 blieb auch mit ADR 0009 bis zum nächsten unsichtbar, ohne Spur im Log (`fix-webview-copy-fence.md`). Tritt es beim Nutzer auf: mit `Measure-PictureDelay.ps1 -Click` und den Debug-Zeilen von `WebViewCapture` eingrenzen.
- Ein Push ohne Remote `origin` (anderer Name) setzt keinen Upstream – Remote wählen lassen.

## Worktrees (Fortsetzung von WP-04)

- Worktree anlegen (Branch wählen oder neu, Ordner vorschlagen, z. B. neben dem Repo).
- Beim Entfernen auch den Branch löschen, wenn er gemergt ist – gehört zu WP-08 (Branches aufräumen).
- Auto-Pull je Worktree (Fast-Forward seines Branches nach dem Auto-Fetch des Repos); bräuchte Konfiguration Version 2 – vom Nutzer erst einmal nicht gewollt (2026-11-21).
- Status je Worktree: seit WP-03 nur bei Änderungen und alle 2 min als Rückfall; bei Repos mit sehr vielen Worktrees noch messen (Prozessstarts beim Start und bei „Status neu lesen“).
- Ein verknüpfter Worktree, der schon vor WP-04 selbst auf dem Board stand, erscheint doppelt (oben und unter seinem Repo); ihn entfernen oder automatisch übernehmen?
- Worktrees aller Repos auf einmal aufräumen (wie „gone“-Branches überall in WP-08).
- Push auf einem Branch mit gelöschtem Upstream (`HasUpstream`, also ohne `-u`) legt den gelöschten Remote-Branch wieder an – seit „Upstream gelöscht“ sichtbar (vorher schon da, Review WP-04).
- Lokale Commits eines Branches mit gelöschtem Upstream zählen nicht als „Voraus“ (Chip, Tray; vorher schon da, Review WP-04).

## Diff (Fortsetzung von WP-05)

- Side-by-Side als zweite Ansicht neben inline (der Nutzer wollte erst nur inline, 2026-11-21); Auswahl merken.
- Zeilennummern beim seitlichen Scrollen stehen lassen (`position: sticky` links); heute scrollen sie mit.
- Syntax-Highlighting (ADR 0008: bewusst ohne Monaco) – z. B. ein kleiner Tokenizer je Sprache oder doch eine Bibliothek, dann per ADR.
- Kodierungen außer UTF-8 (Windows-1252, UTF-16): `GitRunner` liest UTF-8, solche Dateien erscheinen mit Ersatzzeichen.
- Diff im externen Werkzeug öffnen (`git difftool`, VS Code `--diff`).
- Mit Tastatur durch die Dateiliste (↑/↓ wechselt den Diff).
- Hunks und Zeilen stagen (Fortsetzung von WP-25): Zeilen per Ziehen am Rand wählen; mit der Tastatur (↑/↓, Leertaste, „s“ für stagen); Auswahl über mehrere Hunks auf einmal (heute ein Hunk je Patch).
- Teile eines Hunks stagen, dessen letzte Zeile keinen Zeilenumbruch hat und mit anderen Zeilen getauscht wird: manche Auswahl lehnt RepoBoard ab („mit den Zeilen daneben auswählen“, ADR 0012) – `git add -p` kann hier mehr.
- Größengrenze bei gelöschten Dateien (Review WP-05, Punkt 11): gemessen wird die Datei im Worktree – eine gelöschte große Datei hat keine, git liest ihren Diff bei offenem Diff alle 20 s ganz ein (geparst wird höchstens bis 5 000 Zeilen, nicht auf dem UI-Thread). Abhilfe: Größe aus Index bzw. HEAD (`git cat-file -s :<pfad>` / `HEAD:<pfad>`).

## Historie (Fortsetzung von WP-09)

- Amend eines gepushten Commits mit `push --force-with-lease` – eigene Rückfrage, ADR (Ausnahme zu `CLAUDE.md` §2).
- Revert und Cherry-Pick von Merge-Commits (`-m 1`), mehrere Commits auf einmal; Abbrechen und Fortsetzen eines angehaltenen kommt mit WP-07.
- Konflikt vorher erkennen (`git merge-tree --write-tree`, wie WP-17) statt erst beim Revert/Cherry-Pick.
- `git log --topo-order` geht ohne Commit-Graph-Datei die ganze Historie durch, mit jedem Status bei offener Zeile: bei sehr großen Repos messen; ggf. nur neu lesen, wenn sich HEAD oder Refs geändert haben.
- Graph: Spuren nach links rücken lassen (wie `git log --graph`), mehr als acht Spuren (seitlich scrollen), Spuren je Branch einfärben statt je Spalte.
- Commit-Details: Tastatur (↑/↓ durch die Historie), Commit-ID kopieren, „im Terminal zeigen“, Suche in der Historie (mit WP-16).
- „Alle Branches“ je Repo merken (Konfiguration Version 2).
- Ignorierte Dateien überschreibt git auch beim Branchwechsel (`SwitchAsync`) und beim Fast-Forward von Auto-Pull, ebenso lokal geänderte Dateien mit `skip-worktree`/`assume-unchanged` – dort wie bei Revert/Cherry-Pick vorher prüfen (Review WP-09, vorher schon da).
- Commit, Amend, Revert und Cherry-Pick laufen mit 30 s Zeitlimit (`Local`): wartet eine gpg-Passphrase (pinentry) oder ein langer `pre-commit`-Hook länger, bricht `GitRunner` git mitten im Schreiben ab – für Befehle, die signieren oder Hooks starten, ein längeres Limit (Review WP-09, vorher schon da; der Fast-Forward läuft seit Review 0.4.0 mit 3 min).
- Leerer Cherry-Pick nach Squash: auf Wunsch `--skip` selbst ausführen (Nutzer wollte zunächst nur die Meldung, 2026-11-22).

## Dateiwächter (Fortsetzung von WP-03)

- Die Ordner, deren Änderungen nichts auslösen (`bin`, `obj`, `node_modules`, `.vs`, `.idea`, `TestResults`), sind fest: stattdessen die ignorierten Pfade von git fragen (`check-ignore --stdin`) oder die Liste einstellbar machen.
- Ein Repo mit eigenem Git-Ordner außerhalb (Submodul, `--separate-git-dir`) wird nur beobachtet, wenn auch dieser Ordner eine Wurzel ist – gelöst über `WatchMap.Roots`, aber nicht end-to-end geprüft.
- Ein erfolgreicher Status löscht einen Status-Fehler in der Zeile nicht; er steht bis zum Ausblenden oder zur nächsten erfolgreichen Operation (vorher schon da, Review WP-03). Bei Erfolg löschen?
- Unter Linux löst `git worktree lock` (nur `worktrees/<n>/locked` entsteht) kein Lesen des Repos aus, unter Windows schon (Review WP-03, ohne Belang für Windows).
- Wächter, die nicht starten oder scheitern, stehen nur im Log; im Board sieht man nicht, welches Repo nur mit dem Takt gelesen wird.

## Infobereich und Benachrichtigungen (Fortsetzung von WP-02)

- Klick auf eine Benachrichtigung im Benachrichtigungscenter tut nichts: Balloon-Tips melden Klicks nur als Popup. Abhilfe: echte Toasts (`Windows.UI.Notifications` bzw. Windows App SDK) mit eigener AUMID und COM-Aktivierung – dann auch Knöpfe in der Meldung („Öffnen“, „Pullen“).
- Symbol im Infobereich mit Zustand (pausiert grau, Problem mit Punkt), wie der Tooltip.
- Autostart verweist auf das Programm, das ihn eingeschaltet hat: nach einem Verschieben oder einem Build aus einem anderen Ordner bleibt der alte Pfad, bis man ihn neu einschaltet. Mit WP-10 (Installer, fester Pfad) erledigt sich das meist.
- „Neue Commits“ bei Auto-Fetch ohne Auto-Pull als vierte Stufe der Benachrichtigungen?

## Aus Review 0.4.0 (Entscheidungen des Nutzers, 2026-11-22)

- Auto-Pull: eine Überschneidung lokaler Änderungen mit dem Pull vorher erkennen (`diff --name-only HEAD @{u}` ∩ geänderte Pfade) und als `PullCheck`-Grund „übersprungen“ führen statt als Fehler (A2).
- Die Repo-Zeile per Tastatur öffnen (`tabindex=0`, `role=button`, `aria-expanded`, Enter/Leertaste) (A30).
- Größe, Position und Maximiert des Fensters merken (`AppSettings`); heute nur auf die Arbeitsfläche begrenzt (A37).
- Paket-Patches mit FerretSharp zusammen heben: AspNetCore.Components.Web und Extensions.Hosting 10.0.15, Components.WebView.Wpf 10.0.130 (Prüfliste ADR 0009), TimeProvider.Testing 10.11.1 (B13).
- `RepoState.LastFetch`/`LastAutoSync` sind `DateTimeOffset?` ohne Schutz zwischen Threads (A16, Verdacht, nicht nachgestellt): bei einem gerissenen Lesen wie `StatusReadAt` als `long` speichern.
- Nach einem erfolgreichen Auto-Fetch steht die Fehlerzeile eines Auto-Pulls, der gleich wieder scheitert, kurz nicht da (A2, Flackern; gemeldet wird nur einmal).
- Ein Test, der die UI-Assembly gegen WPF-Referenzen prüft wie `ArchitectureTests.Core_does_not_reference_ui_assemblies` den Core (C9; der Linux-Build fängt es heute schon).
- Die Kopfleiste passt auf Deutsch nicht in 1440 px Fensterbreite (Seite 1424 px, Leiste 1448 px): Der Einstellungs-Knopf ist halb abgeschnitten. Die Leiste umbrechen lassen oder die Chips kürzen (E2E R1, 2026-11-23).
- Ein vorgefertigtes Sandbox-Template, das jeder Git-Test kopiert, statt init/clone/commit/push neu (C14, nicht gemessen).
