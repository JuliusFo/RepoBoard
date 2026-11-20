---
paths:
  - "src/RepoBoard.UI/State/**"
  - "tests/RepoBoard.UI.Tests/**"
---

# UI – Zustand und Host-Schnittstellen

- `State/` enthält testbare Logik ohne Rendern (`BoardFilter`, `RelativeTime`) und die Schnittstellen zum Host (`IDesktop`). Namespace `RepoBoard.UI.State`. Braucht eine Regel die Komponente, bekommt sie ein schmales Interface, das die Komponente implementiert.
- Was nur der Host kann (nativer Dialog, Programme starten), ist ein Interface hier, implementiert in `RepoBoard.App/Services`. Keine WPF-Typen in `State/`.
- Der Zustand eines Repos kommt nur aus `RepoState` (vom `BoardService` geschrieben). Die UI hält nur, was die Seite selbst betrifft (offene Zeilen, Filter, Suche) – nach einem Neuladen der Seite ist das weg, das Board nicht.
- Tests der State-Klassen in `tests/RepoBoard.UI.Tests/State`; `RepoState`-Werte setzt ein Test direkt (interne Setter sind für die UI-Tests sichtbar).
