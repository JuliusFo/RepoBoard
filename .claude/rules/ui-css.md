---
paths:
  - "src/RepoBoard.UI/wwwroot/css/**"
---

# CSS

Aus FerretSharp (`ferretsharp.css`, `.claude/rules/ui-css.md` dort).

- Eigenes CSS mit Design-Tokens (`repoboard.css`), hell/dunkel über `prefers-color-scheme`; keine CSS-Frameworks. Farben nur als Tokens mit Dunkel-Variante. Helle Flächen nicht als `rgba(…)` festschreiben, sondern aus dem Token mischen (`--warn-weak: color-mix(in srgb, var(--warn) 14%, transparent)`) – dann folgt der Dunkelmodus von selbst. Kontrast im Dunkelmodus berechnen, nicht nur ansehen.
- Farbige Button-Varianten (`.btn.primary`) setzen im `:hover` ihren Hintergrund selbst: `.btn:hover:not(:disabled)` setzt `--hover` und gewinnt sonst (Text unlesbar). Text auf farbigen Flächen über Tokens (`--accent-text`).
- Rahmen um einen Container nie als `box-shadow: inset` auf dem Container: der Schatten liegt unter den Kindern.
- Sticky-Kopfzeilen (`.row.head`): kein Vorfahre zwischen Kopf und Scroll-Container darf `overflow: hidden/auto` haben (für runde Ecken `overflow: clip`); das Padding des Scroll-Containers rechnet Sticky mit ein (daher `top: -14px` bei 14px Padding von `.board`).
- Scrollbar-Teile nicht über `:hover` am Container umschalten: Chromium malt den Daumen dann nicht neu.
