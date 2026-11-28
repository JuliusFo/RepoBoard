---
paths:
  - "**/*.resx"
  - "src/*/Resources/**"
---

# Texte in Englisch und Deutsch (ADR 0005)

Anleitung und Bereiche: `docs/localization.md`.

- Neuer Text = Schlüssel in **beiden** Dateien (`<Bereich>Text.resx` Englisch, `.de.resx` Deutsch), gleiche Platzhalter; `<comment>` in der englischen Datei bei jedem Platzhalter und bei mehrdeutigen kurzen Wörtern (`ResourceTests` prüft die Platzhalter; mehrdeutige Wörter prüft niemand).
- Schlüssel nach der Bedeutung, nicht nach dem Wortlaut; mit Bereich als Präfix, wo es hilft (`Col_Branch`, `Op_Fetch`). Plural über `One`/`Other` und `TextFormat.Plural`.
- Ganze Sätze mit Platzhaltern (`TextFormat.Format`), nie Satzteile zusammenkleben.
- Ein Satz, den der Core schon hat, kommt in der UI nicht ein zweites Mal: die UI zeigt Core-Sätze direkt (`SyncText.AutoPullSkipped`).
- Englisch: US-Schreibweise, Sentence case, „you“, typografische Anführungszeichen. Deutsch: du-Form, „…“, Gedankenstrich –. Git-Begriffe bleiben englisch (Branch, Commit, Fetch, Pull, Push, Stage).
