---
paths:
  - "src/RepoBoard.UI/Components/**"
  - "src/RepoBoard.UI/*.razor*"
---

# UI – Muster und Fallen

Zustand und testbare Logik: `ui-state.md`; CSS: `ui-css.md`; WPF-Host: `app-host.md`. Die Fallen unten stammen aus FerretSharp (dort mit echter Maus gefunden).

## Ordner
- Komponenten liegen nach Bereich in `Components/` (Common, Board); der Ordner ist der Namespace, ein neuer Ordner kommt in `_Imports.razor`. Die `.cs`-Helfer (`BoardComponent`, `Icons`) behalten `RepoBoard.UI.Components`.

## Ausnahmen und Rendern
- Komponenten erben von `BoardComponent`. Click-Handler laufen über `GuardAsync`/`Guard` (Fehler ins Aktivitätsprotokoll statt Blazor-Fehlerleiste); Arbeit, die nicht aus einem Blazor-Event startet (Timer, Dienst-Ereignis), über `Fire(...)`, nie `_ = …`. Ein Abbruch darf nie entkommen: der WPF-Dispatcher wirft ihn sonst als „Unerwarteter Fehler“ neu (FerretSharp `fixes-guard-cancel`).
- Ein Kind, das einen Wert des Elternteils zeigt und ändert (Schalter), meldet die Änderung als `EventCallback<T>`, nicht als `Func<T, Task>`: nur der Callback rendert den Empfänger neu (FerretSharp R6b, `Toggle`).
- `BoardService.Changed` feuert von beliebigen Threads und in Schüben: nur die Shell abonniert, rendert einmal je Schub (`OnBoardChanged`); Kinder rendern mit ihr. Wer selbst abonniert, meldet sich im `Dispose` ab.
- „Kinder rendern mit ihr“ gilt nur für Kinder **mit Parametern**: ein Kind ohne Parameter, das `Board` selbst liest, bleibt beim Rendern der Shell stehen. Was es vom Board zeigt, als Parameter übergeben (Review WP-02, `SettingsMenu`).
- Komponenten rufen git nie selbst: alles über `BoardService`.
- Lange Listen (Diff bis 5 000 Zeilen): die Zeile als eigene Komponente mit `ShouldRender`, sonst rendert jede kleine Änderung (eine gewählte Zeile) alle Zeilen samt neuer Event-Handler neu (~110 ms je Klick, Review WP-25, `DiffLineView`).

## Bausteine
- Icons nur aus `Icons.cs` (`@Icons.X`), nie als eigenes SVG im Markup. Ein Icon steht für genau eine Bedeutung (Summary in `Icons.cs`); neue aus Lucide 1.55 unverändert kopieren, möglichst aus FerretSharps `Icons.cs`.
- Tooltips immer als `title`-Attribut (auch an deaktivierten Knöpfen), keine eigenen Tooltip-Komponenten.
- Jeder sichtbare Text aus `BoardText` (bzw. Core-Sätze direkt aus `SyncText`), nie im Markup festgeschrieben.

## Blazor im WPF-Host (WebView2)
- **Kein `@ondblclick`:** der Host reicht beim zweiten Klick das Mouse-down doppelt weiter, `dblclick` feuert nie. Doppelklick über `@onclick` mit `e.Detail >= 2`.
- Kein `auxclick`-Event: `@onauxclick` wird still als HTML-Attribut ausgegeben. Mittelklick über `@onmouseup` mit `e.Button == 1`.
- Native `<select>`: die Auswahl über `value` am `<select>` setzen, nicht `selected=` an der Option.
- Listen, deren Einträge Eingabefelder haben (Checkbox) und wandern oder verschwinden, bekommen `@key` – sonst setzt Blazor das Element eines verschwundenen Eintrags für den nächsten ein, samt dem Häkchen, das der Klick gesetzt hat (E2E Review WP-05, Dateiliste). Schlüssel müssen unter dem gemeinsamen Elternteil eindeutig sein (`s:`/`u:` + Pfad).
- **UI-Kultur nur synchron setzen** (`Program.Main`): `CurrentUICulture` gehört zum `ExecutionContext`, eine Änderung in einer async-Methode wird beim Verlassen zurückgenommen.
- Mausinteraktionen mit echter Windows-Eingabe prüfen, nicht nur per CDP (`docs/e2e-testing.md`).
