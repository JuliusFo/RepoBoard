# ADR 0001: Stack, Aufbau und Arbeitsweise wie FerretSharp

- Status: akzeptiert (Entscheidung des Nutzers, 2026-11-20)
- Datum: 2026-11-20

## Kontext

Für die Übersicht über alle Repos mit Auto-Fetch/Auto-Pull je Repo gibt es kein fertiges Tool, das alles abdeckt (Recherche 2026-11-20: GitHub Desktop, Fork, Sourcetree, GitKraken ohne Übersicht über alle Repos und ohne Auto-Pull je Repo; RepoZ/RepoM zeigen Branch und Status, pullen aber nicht und committen nicht; GitSplash pullt Gruppen nur auf Klick). Ein Spike in einem Scratch-Ordner hat die Idee bestätigt („Der Spike sieht richtig gut aus!“). Der Nutzer wollte den Spike „mit C# und dem selben UI-Stack wie FerretSharp“ und das Projekt danach „direkt sauber aufbauen und das Gelernte aus FS übernehmen“ – Projektstruktur, Regeln, Claude-Skills.

## Entscheidung

- **Stack wie FerretSharp:** .NET 10, WPF-Host mit `BlazorWebView`, plattformneutrale Razor Class Library, eigenes CSS mit Design-Tokens (aus `ferretsharp.css`), Lucide-Icons als Pfade, Generic Host, Serilog, xUnit v3 auf der Microsoft Testing Platform, Central Package Management mit denselben Paketversionen.
- **Aufbau wie FerretSharp:** `Core` (ohne Windows-APIs, baut und testet unter Linux), `UI`, `App`; `CLAUDE.md`, `.claude/rules/` mit `paths`, Skills (`branch-code-review`, `complete-code-review`, `release`, `ship-wp`), `docs/` mit ADRs, Arbeitspaketen, Roadmap und Backlog, CI mit Linux zuerst, Zeilenenden-Check.
- **Übernommene Bausteine** (aus FerretSharp kopiert, auf RepoBoard gekürzt): `SafeDelete`, `AtomicFile`/`AtomicJsonFile`, `VersionedJsonFile`, `InstanceLock`, `TextFormat`, `RunningInstance`, `WindowTheme`; Test-Helfer `TestFolder`, `Wait`, `UiCulture`, `ResourceChecks`, `Repository`; `tools/linux`, `tools/e2e`.
- **Nicht übernommen** (FerretSharp-spezifisch): Oracle, ModelHost, AppTrace, AG Grid, Monaco, Links/Pages, Velopack (kam als eigenes Paket: WP-10, ADR 0007).

## Konsequenzen

- Was in FerretSharp gelernt wurde (Fallen von WebView2 im WPF-Host, Linux-CI, atomare Dateien, Ressourcen-Tests), gilt hier vom ersten Tag an; die Regeln nennen die Herkunft.
- Paketversionen laufen mit FerretSharp gleich – ein Update dort ist eins hier.
- Beide Projekte teilen das Design: Ein Nutzer, der beide offen hat, sieht dieselbe Sprache.
