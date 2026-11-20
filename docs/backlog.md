# Backlog

Ideen und Themen, die nicht zum aktuellen Paket gehören – nach Bereich. Geplante Pakete: `docs/roadmap.md` (dort seit 2026-11-20 die ganze Ideenliste als WP-02 … WP-24). Neue Einträge in den passenden Bereich, erledigte entfernen; was ein Paket offen ließ, steht hier als „Fortsetzung von WP-NN“.

## Bekannte Lücken

- Icon `Terminal` (Lucide `square-terminal`) ist nicht aus FerretSharp kopiert, sondern ohne Paket geschrieben – gegen `lucide-static` 1.55.0 prüfen (`wwwroot/lib/lucide/README.md`).
- Programmsymbol (WP-02, `tools/icon/New-AppIcon.ps1`) ist ein schlichter Entwurf – WP-10 hat es nicht überarbeitet; es steht jetzt auch in `Setup.exe` und im Startmenü.
- Die Kopfzeile passt bei 1440 px Fensterbreite nicht ganz: der Einstellungsknopf liegt am Rand (WP-10 E2E, schon vorher so); mit dem Update-Knopf fehlen ~200 px. Chips oder Suche schmaler, Knöpfe nur als Symbol, oder eine zweite Zeile.
- Installer und Updates unsigniert (WP-10, ADR 0007): SmartScreen warnt; Signieren wie in FerretSharp (Kosten, SignPath) im Blick behalten.
- Theme und Sprache folgen Windows; eine Einstellungsseite fehlt (ADR 0005). Passt zu WP-12 oder WP-16, falls dort Einstellungen dazukommen.
- Ein Repo zu scannen heißt, seiner Konfiguration zu vertrauen (`core.fsmonitor`, Hooks laufen mit jedem `git status`, ADR 0002) – beim Scannen die Liste vorher bestätigen lassen?
- Schwärzen in git-Meldungen (`GitSecrets`, vor v0.1.0) erkennt Tokens ohne festes Präfix nur in einer URL: ein Azure-DevOps-PAT oder ein anderes Geheimnis, das ein Server frei in eine `remote:`-Zeile schreibt, bliebe sichtbar. Weitere Muster ergänzen, wenn eines auftaucht.
- Beenden während eines Auto-Pulls bricht `git merge --ff-only` nach 3 s ab (`host.StopAsync`, `GitRunner` beendet den Prozessbaum): lokale Arbeit bleibt, aber ein halb aktualisierter Worktree oder `index.lock` kann liegen bleiben. Seit `--exit` (WP-02) gibt es dafür einen Auslöser von außen, seit WP-10 auch „Jetzt neu starten“ in der Kopfzeile (ein Klick ohne Rückfrage); laufende Operationen beim Beenden bis zum Zeitlimit zu Ende laufen lassen (Review WP-02, vorher schon da).
- Status-Durchlauf alle 20 s über alle Repos: bei sehr vielen Repos messen (Prozessstarts); Abhilfe in WP-03.
- WebView-Bild nach Ruhezustand/Dock/RDP: FerretSharp hat `WebViewCapture`/`CopyFence` (ADR 0038/0039 dort); hier nur das WebView2-Pin. Tritt es auf, von dort übernehmen.
- Ein Push ohne Remote `origin` (anderer Name) setzt keinen Upstream – Remote wählen lassen.

## Infobereich und Benachrichtigungen (Fortsetzung von WP-02)

- Klick auf eine Benachrichtigung im Benachrichtigungscenter tut nichts: Balloon-Tips melden Klicks nur als Popup. Abhilfe: echte Toasts (`Windows.UI.Notifications` bzw. Windows App SDK) mit eigener AUMID und COM-Aktivierung – dann auch Knöpfe in der Meldung („Öffnen“, „Pullen“).
- Symbol im Infobereich mit Zustand (pausiert grau, Problem mit Punkt), wie der Tooltip.
- Autostart verweist auf das Programm, das ihn eingeschaltet hat: nach einem Verschieben oder einem Build aus einem anderen Ordner bleibt der alte Pfad, bis man ihn neu einschaltet. Mit WP-10 (Installer, fester Pfad) erledigt sich das meist.
- „Neue Commits“ bei Auto-Fetch ohne Auto-Pull als vierte Stufe der Benachrichtigungen?
