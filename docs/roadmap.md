# Roadmap – geplante Pakete und offene Fragen

Geplante Pakete mit den Entscheidungen des Nutzers. Eine Reihenfolge gibt es nicht: Welches Paket als Nächstes kommt, entscheidet der Nutzer jeweils selbst; festgehalten werden nur Abhängigkeiten („braucht WP-NN“). Beim Start eines Pakets den Abschnitt lesen und den Plan mit dem Nutzer abstimmen. Ist das Paket fertig, wandert sein Abschnitt als „Planung“ in die Paketdatei unter `docs/work-packages/`. Nicht eingeplante Ideen: `docs/backlog.md` (dort die ganze Ideenliste vom 2026-11-20, nach Priorität).

## Vorschläge für die nächsten Pakete (nicht bestätigt)

Aus der Ideenliste vom 2026-11-20 – Empfehlung von Claude, noch keine Entscheidung des Nutzers.

### Tray-Icon und Autostart
Damit „immer aktuell“ auch gilt, wenn das Fenster zu ist (ADR 0003, Konsequenzen). Fenster schließen → in den Infobereich; Autostart mit Windows (Registry `Run` des Nutzers oder Verknüpfung); Windows-Benachrichtigung bei Auto-Pull und Fehlern. Offen: Schließen = Beenden oder Minimieren? Benachrichtigung bei jedem Auto-Pull oder nur bei Fehlern?

### Sofort aktualisieren statt Abfragen
Dateiwächter auf `.git/HEAD`, Index und Refs je Repo; der 20-s-Takt bleibt als Rückfallebene. Braucht: Grenzen für viele Repos (Watcher je Repo), Ruhezeit nach Änderungen.

### Worktrees
Worktrees als Unterzeilen ihres Repos (Branch, Status, Pfad); verwaiste oder gemergte Worktrees aufräumen (mit Rückfrage, `git worktree remove` – nie ein Ordner mit ungesicherter Arbeit). Passt zu den vielen `.claude/worktrees` des Nutzers.

### Installer und Updates
Velopack wie FerretSharp (ADR 0024 dort): `Program.Main` ruft Velopack zuerst, Release-Workflow auf Tag, Update-Dialog. Mit App-Icon (`tools/icon` wie FerretSharp).
