# R1 Gesamt-Review 0.4.0 umgesetzt

Branch `wp/r1-review-0.4`, 2026-11-22, noch ohne Release.

## Anlass
- `/complete-code-review` nach Release 0.4.0: acht Agenten nach Themen auf `3318da0`, Befunde, Vorschläge und Fragen in [`docs/review-0.4.0.md`](../review-0.4.0.md).
- **Entscheidungen des Nutzers (2026-11-22):** stehen im Review-Dokument; kurz: alles in einem Paket R1, A1 als erster Commit; der Scan fragt vor git (A9); nur „derselbe Fehler einmal“ (A2); eigenes Icon und Aktivität beim Entfernen (A10); erst messen (A12); Esc/Fokus und `lang` jetzt, Zeile per Tastatur in den Backlog (A30); Fenster nur begrenzen (A37); ADR 0002, 0003 (Regeln) und 0005 bestätigt; Doku neutral formulieren.

## Gebaut
- **Teil A – Fehler und Sicherheit**
  - A1: Pull und Fast-Forward mit `--no-autostash` (Pull auch `--no-rebase`) – vorher nachgestellt: `merge.autoStash` legte lokale Änderungen mit Konfliktmarkern zurück, Exit 0. A4: der Fast-Forward läuft über `Network` (3 min, im Hintergrund ohne Anmeldung).
  - A2: `RepoState.LastAutoError`; ein gescheiterter Fetch lässt den Grund eines übersprungenen Auto-Pulls stehen.
  - A3: Auto-Sync mit höchstens `MaxParallelNetwork - 1` Repos gleichzeitig, „Alle fetchen“ mit `MaxParallelNetwork` und ohne fehlende Repos. Der vorgeschlagene Zufallsversatz entfällt: jedes Repo setzt seine Zeit erst beim Start, die Intervalle verteilen sich so.
  - A5: Remote-Branch über `switch -c <lokal> --track`, lokaler Name aus `LocalNameOf` (im Test zeigte sich: git schneidet bei einem Remote `up/stream` nur `up/` ab); lokal `switch --no-guess`.
  - A6: Ordner ohne `.git` ist „fehlt“, keine Operation läuft dort (ein ganz fehlender Ordner bleibt git überlassen: das Entfernen eines Worktrees läuft in seinem Repo).
  - A7: `log -z`/`show` mit NUL, Parser bricht bei Ausgabe ab, die nicht im Takt ist. A19: Zeiten als Zahl, `upstream:track` als Zahlen (in `for-each-ref` unübersetzt – Frage aus dem Review damit geklärt), Monate und Jahre in `RelativeTime`. A39: `CommitMessage` (Betreff wie `%s`, Rumpf nach dem ersten Absatz).
  - A9 (ADR 0014): `RepoScanner.Inspect`, `ScanCandidate`, `ScanSelection`, `ScanDialog`; Worktrees auf Freigaben nur für Repos auf Freigaben; README-Satz.
  - A8: Windows Terminal bekommt den Ordner als Arbeitsordner (`-d .`). A11: zweiter Start versucht das Signal bis 10 s, `RunningInstance` entfernt. A33: `UnauthorizedAccessException` beim Öffnen des Signals gefangen. A34/A35: `DesktopPaths` (Startordner aus den Repos, `SafeForCmd`, `FindOnPath`), lokalisierte Fehler. A36: Fehlerleiste in der Shell. A37: Fenster auf die Arbeitsfläche begrenzt. A30: `lang` per Host-Skript nach dem Laden.
  - A10: Icon `RemoveFromBoard` (Lucide `list-x`), Aktivitätseintrag. A13: `SetLogAsync` wartet auf das Tor. A14: `ChangeDebouncer.Retry`, `UnwatchedRefresh`. A15: `RepoState.FamilyNetwork` – vorher nachgestellt: 48-mal „cannot lock ref“ in 25 Runden. A17: `GitRunner` meldet fehlende Ordner. A18: leere Clones zählen die Commits des Upstreams. A20: `GitRunner.EnvironmentFor` lässt die git-Variablen des Elternprozesses weg – vorher nachgestellt (`GIT_DIR` zeigt in einem fremden Ordner den Status eines anderen Repos). A21: `GitRunner.Command`. A22: git’s Meldung beim Hinzufügen. A23: volle Pfade in der Konfiguration. A24: `VisibleText` – vorher im Chromium der WebView nachgestellt. A25, A26, A27 (`FocusQuietlyAsync`), A28 (Kontrast berechnet: warn 5,7:1, danger 5,9:1, green 5,8:1 auf Weiß), A29 (`--scrim`), A31 (Schlüssel der Dateizeile nach Fehlschlag), A32, A38 (`HeadOnRemote` als `bool?`).
- **Teil B – Struktur:** B1 `DiffBody`, B2 `PickCheck`, B3 `Paths`, B4 FerretSharp-Reste und -Verweise, B5 `Prunable` entfernt, B6 `MaxParallelReads`/`RefreshManyAsync`, B7 `CommitText`, `TextCut`, `UpdateComponent`, `ReadDiffAsync`, `WindowInFront`, B8 `UpdateIntervals`, B9 eigene Dateien, `RepoDetails` nach `Core/Board`, B10 CSS-Namen (`.pick-*`, `.note`), B11 Release-Publish in der CI, B12 `docker build --pull`.
- **Teil C – Tests:** C1–C15 wie im Review vorgeschlagen (u. a. `AutoSyncQueueTests` mit einem zählenden `GitRunner`, `GitRunnerTests`, `RepoScannerTests`, `AtomicFileTests`, `PathsTests`, `ScanSelectionTests`, `DesktopPathsTests`, `VisibleTextTests`, `CommitTextTests`, `UpdateIntervalsTests`); `Boards.New` für den Testaufbau; Board-Tests in `Board/BoardTests.cs`, Graph-Tests in `GraphDrawingTests.cs`.
- **Teil D – Doku:** ADR 0002/0003/0005 bestätigt, ADR 0014, Nachträge in 0001, 0003, 0004; `git.md` geteilt in `git.md` und `board.md`, `app-host.md` gekürzt, Globs ergänzt, Doppeltes durch Verweise auf `CLAUDE.md` ersetzt; Release-Skill verfolgt `release.yml`; persönliche Angaben neutral; README, Architektur, Index, Backlog, CHANGELOG.

## Nicht umgesetzt, mit Grund
- **A12** gemessen statt umgebaut (Entscheidung: erst messen): 3 000 Dateien in der offenen Zeile, ein Zeichen in der Commit-Nachricht bis zur fertigen Darstellung **14 ms** (eine Datei: 2 ms); Browser-Seite 7 ms je Taste. Nicht spürbar – kein Umbau.
- **A16** (gerissenes Lesen von `LastFetch`/`LastAutoSync`) nicht nachgestellt: theoretisch, kein Szenario im Test erzeugt – nach der Regel ohne Änderung, im Backlog.
- **A8** nicht nachgestellt (öffnete Fenster auf dem Desktop des Nutzers, und Windows Terminal läuft dort in einem Prozess für alle Fenster). Statt einer Maskierung, die vom Verdacht abhängt, bekommt Windows Terminal den Pfad gar nicht mehr als Argument – das gilt unabhängig davon, ob es `;` zerlegt.
- **A33** nicht nachgestellt (erhöhte Instanz): das Verhalten von `TryOpenExisting` steht in der Doku; der Fang ändert nichts im Normalfall.
- **B13** (Paket-Patches): mit FerretSharp zusammen heben – Backlog.
- **C9** Prüfung der UI-Assembly gegen WPF: der Linux-Build fängt es; Backlog. **C16** (fünf Wächter-Fälle in einer Sandbox) optional, nicht gemacht: der erste Fehler verdeckte die folgenden.
- In den Backlog nach Entscheidung: Überschneidung beim Auto-Pull vorher erkennen (A2), Zeile per Tastatur (A30), Fenstergröße merken (A37).

## Geprüft
- `dotnet build -warnaserror` ohne Warnung; `dotnet test` 536 grün (vorher 421). `dotnet publish -c Release -r win-x64 --self-contained` lokal ohne Warnung (B11).
- Neue Tests gegen die alte Fassung gebrochen: A1 (zwei Fälle rot ohne `--no-autostash`), A2 (rot mit dem alten Vergleich), A3 (rot mit beliebiger Parallelität).
- **E2E** (Testinstanz im Scratchpad, `--lang=de --theme=light`, Test-Repos dort):
  - `lang="de"` an der Seite, Fehlerleiste auf Deutsch, eigenes Icon zum Entfernen, Branch-Liste „1 voraus · gerade eben“, Historie mit verbundenem Betreff, Richtungszeichen im Diff als ⟨U+202E⟩ (CDP).
  - **Echte Eingabe** (`Invoke-RealClick.ps1`, jetzt mit `-Shift` und `-Key`): Klick und Shift+Klick am Rand des Diffs wählen zwei Zeilen („Zeilen stagen (2)“), dabei wird kein Text markiert; Esc schließt den Diff.
  - Nur per CDP (danach holte Windows das Testfenster nicht mehr nach vorn, der Nutzer arbeitete in einem anderen Fenster – abgebrochen wie vorgesehen): Commit-Panel und Einstellungsmenü nehmen den Fokus und schließen mit Esc; das Intervall-`<select>` übernimmt einen neuen Wert.
  - Gefunden und behoben: `Error="_error"` an `DiffBody` (und `Root="scan.Root"` am `ScanDialog`) übergab den Text wörtlich – der Diff zeigte „_error“. Regel in `ui.md`.
  - Log der Testinstanz ohne `[ERR]`/`[WRN]`.
- **Nicht geprüft:** der Scan-Dialog im Fenster (der native Ordnerdialog ist nicht per CDP bedienbar; Logik in `ScanSelectionTests`, Scan in `RepoScannerTests`/`WorktreeTests`), die `<select>`-Felder und der Release-Link mit echter Maus, Esc im Commit-Panel mit echter Taste, „Terminal“ mit Windows Terminal, `Test-Linux.ps1`.

## Nachtrag: Code-Review (2026-11-22)
`/branch-code-review` vor dem Push, ein Reviewer-Agent auf `3318da0..2cd5d47`, danach ein zweiter Durchgang auf den Fix-Commits.
- **Entscheidung des Nutzers, erster Durchgang:** „Alle fixen“.
  - M1 behoben: `Parallel.ForEachAsync` bricht sein Token beim ersten Fehler ab, und das beendete git in den anderen Repos (Fast-Forward mitten im Checkout). `EachAsync` fängt je Repo und reicht den Token des Aufrufers weiter; Test mit zwei Repos.
  - M2 behoben: Der Scan prüft eine Positivliste. Eine unbekannte Einstellung (Filter, Diff-Treiber, Credential-Helper, `ext::`-Remote) heißt „weitere eigene Einstellungen“ und ist nicht angehakt. `config.worktree` wird mitgelesen.
  - M3 behoben: Ein Git-Ordner auf einer fremden Freigabe wird erst gelesen, wenn der Nutzer das Repo angehakt hat (eigener Hinweis).
  - K1 behoben: Der Takt wartet nicht mehr auf den Auto-Sync; es läuft ein Durchlauf zur Zeit.
  - K2 behoben: `CountAsync` lehnt Bereiche mit führendem `-` ab.
  - K3 behoben: „2 Monate“ bzw. „2 Jahre“ als Mindestwert.
  - K4: zuerst nicht nachgestellt, im zweiten Durchgang (Befund 4) doch.
  - K5 behoben: Shift im E2E-Skript wird immer losgelassen.
  - K6 behoben: `VisibleText` auch für Pfade, Branch-Namen, Autoren, Refs, Hunk-Köpfe und die Aktivität.
  - K7 behoben: verwaiste Doc-Kommentare entfernt.
  - K8 behoben: git ≥ 2.27 im README.
  - K9 behoben: Ein Worktree ohne `.git` hat im Aufräum-Dialog keinen eigenen Status mehr.
- **Entscheidung des Nutzers, zweiter Durchgang:** „Alle fixen“.
  - 1 (M) behoben: Der Scan liest auch die Konfiguration von Submodulen (`.git/modules/**`, alte Submodule über die Pfade aus `.gitmodules`, nur innerhalb des Repos, keine Links). `git status` startet in jedem ausgecheckten Submodul ein eigenes git.
  - 2 (M) behoben: In `[lfs]` gelten nur bekannte Schlüssel als harmlos. `customtransfer`, `extension` und `standalonetransferagent` starten Programme.
  - 3 (K) behoben: Ein Fehler in einem Durchlauf über viele Repos kommt mit Stacktrace ins Log (optionaler `ILogger` am `BoardService`).
  - 4 (K) behoben, nachgestellt: Ein Stresstest (zwei Threads nehmen die Sperre und geben sie wieder frei) scheiterte 3 von 3 Mal. Endet die erste Instanz zwischen dem gescheiterten Öffnen und `File.Exists` (die Datei verschwindet mit der Sperre, und solange Windows sie löscht, scheitert das Öffnen mit „Zugriff verweigert“), startete die zweite ohne Sperre. `TryAcquire` versucht es dann erneut. Damit ist auch K4 erledigt; die Begründung im ersten Durchgang („FileShare.None“) traf nicht zu.
  - 5 (K) behoben, nachgestellt: Ein `.git`-Link auf eine Freigabe ergab „keine Hinweise“, das Repo war angehakt. Der Scan liest Link für Link, ohne ihm zu folgen, und meldet die Freigabe.
  - 6 (K) behoben: `VisibleText` jetzt auch für die Branch-Namen im Worktree-Dialog. Eine Remote- oder LFS-URL auf einer Freigabe (`\\host`, `//host`, `file://host`) zählt als „weitere eigene Einstellungen“; der Hinweistext nennt das.
- Dritter Durchgang: M-1 behoben, ein Fehler im Fix von Befund 1, nachgestellt. git schreibt für jedes Submodul `core.worktree`, und damit war jeder Klon mit Submodulen nicht angehakt. `core.worktree` gilt jetzt als harmlos, außer es zeigt auf eine Freigabe. Der Test nimmt die Konfiguration aus einem echten `git submodule add`.
- **Entscheidung des Nutzers, Restbefunde des dritten Durchgangs:** „Alle drei fixen“.
  - K-1 behoben: Ein Link im Git-Ordner auf eine Freigabe (`config`, `config.worktree`, `hooks`, `commondir`, ein Ordner unter `modules`, `.gitmodules`, ein Submodul-Ordner auf dem Weg dorthin) bekommt den Hinweis und wird nicht gelesen. Ein Link auf einen lokalen Ordner wird jetzt mitgelesen, wie git es tut. Vorher übersprang der Scan jeden Link unter `modules`, und dessen Einstellungen fehlten.
  - K-2 behoben: `branch.<name>.remote`/`pushremote` und `remote.pushdefault` werden wie eine Remote-URL geprüft (ein Remote-Name bleibt harmlos, vorher war `pushdefault` eine Fehlanzeige). `lfs.storage` auf einer Freigabe zählt als „weitere eigene Einstellungen“.
  - K-3 behoben: Eine Sperrdatei, die das Konto nicht öffnen darf, gilt nur als „läuft bereits“, solange der Prozess aus `instance.pid` läuft. Sonst startet RepoBoard wie bei einem nicht beschreibbaren Ordner. Test mit einer Verweigerungsregel in der ACL.
- Vierter Durchgang: alle drei Fixes bestätigt. Vier Lücken darin gleich mitbehoben, weil jede einen freigegebenen Fix unvollständig ließ:
  - M-A: Unter Linux sieht `GetDirectories` einen Link ins Leere nicht, der neue Test wäre in der CI rot gewesen. `GitFolders` geht jetzt über alle Einträge (nur gelesen, nicht nachgestellt: Docker war aus).
  - M-B: Das Folgen lokaler Links (K-1) hatte keine Obergrenze; Links im Kreis hätten den Scan hängen lassen. Jetzt höchstens 1 000 Einträge unter `modules`, darüber gilt „weitere eigene Einstellungen“.
  - K-C: Auch ein Ordner in der Mitte des Wegs zu `gitdir`/`commondir`, der auf eine Freigabe zeigt, wird gemeldet. Nicht übernommen: verbundene Netzlaufwerke (`Z:`) – die verbindet der Nutzer selbst, ein Archiv wählt damit keinen fremden Host.
  - K-D: `remote.<name>.gh-resolved` (GitHub CLI) und `[gui]` (git gui) gelten als harmlos, sonst wäre jedes Repo mit `gh` nicht angehakt gewesen.
- `dotnet build -warnaserror` ohne Warnung, `dotnet test` 574 grün. `Test-Linux.ps1` nicht gelaufen (Docker aus) – die CI testet zuerst unter Linux. Jeder neue Test scheitert gegen die alte Fassung.
