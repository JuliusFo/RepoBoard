# Backlog

Ideen und Themen, die nicht zum aktuellen Paket gehören – nach Bereich. Geplante Pakete: `docs/roadmap.md` (dort seit 2026-11-20 die ganze Ideenliste als WP-02 … WP-24). Neue Einträge in den passenden Bereich, erledigte entfernen; was ein Paket offen ließ, steht hier als „Fortsetzung von WP-NN“.

## Bekannte Lücken

- Icon `Terminal` (Lucide `square-terminal`) ist nicht aus FerretSharp kopiert, sondern ohne Paket geschrieben – gegen `lucide-static` 1.55.0 prüfen (`wwwroot/lib/lucide/README.md`).
- Kein App-Icon – kommt mit WP-10 (FerretSharp: `tools/icon/New-AppIcon.ps1`).
- Theme und Sprache folgen Windows; eine Einstellungsseite fehlt (ADR 0005). Passt zu WP-12 oder WP-16, falls dort Einstellungen dazukommen.
- Ein Repo zu scannen heißt, seiner Konfiguration zu vertrauen (`core.fsmonitor`, Hooks laufen mit jedem `git status`, ADR 0002) – beim Scannen die Liste vorher bestätigen lassen?
- Fehlermeldungen von git können eine Remote-URL mit Token enthalten (`https://user:token@host`); vor Aktivitätsprotokoll und Log schwärzen.
- Status-Durchlauf alle 20 s über alle Repos: bei sehr vielen Repos messen (Prozessstarts); Abhilfe in WP-03.
- WebView-Bild nach Ruhezustand/Dock/RDP: FerretSharp hat `WebViewCapture`/`CopyFence` (ADR 0038/0039 dort); hier nur das WebView2-Pin. Tritt es auf, von dort übernehmen.
- Ein Push ohne Remote `origin` (anderer Name) setzt keinen Upstream – Remote wählen lassen.
