---
paths:
  - ".github/**"
  - "Directory.Build.props"
  - "Directory.Build.targets"
  - "Directory.Packages.props"
  - "nuget.config"
  - "global.json"
  - "tools/linux/**"
  - "dotnet-tools.json"
  - ".gitattributes"
---

# CI, Build, Pakete

Vor jeder Änderung hier `docs/release.md` lesen.

- **Version** nur in `Directory.Build.props` (`<Version>`). `TreatWarningsAsErrors` und `NeutralLanguage=en` gelten für alle Projekte. Tag und Push nur nach Rückfrage beim Nutzer.
- **Pakete:** Central Package Management, dieselben Versionen wie FerretSharp (zusammen heben). Repo-lokales `nuget.config` nur mit nuget.org: ist global eine weitere Paketquelle eingerichtet (beim Nutzer der Fall), verlangt CPM Package Source Mapping – ohne bricht der Restore mit NU1507 ab (im Spike passiert).
- **CI-Concurrency:** PR-Läufe nach der PR-Nummer gruppieren, nie nur nach `github.ref` (FerretSharp `fix-ci-concurrency`).
- **Zeilenenden:** der Job `line-endings` bricht ab, wenn eine committete Textdatei CRLF hat; beheben mit `git add --renormalize <Datei>`. `*.md text` steht in `.gitattributes`, weil ein einzelnes CR eine Datei unter `text=auto` binär macht (FerretSharp `fix-line-endings`).
- PowerShell in Workflows: `$LASTEXITCODE` eines nativen Befehls direkt nach ihm prüfen, nie nach einer Pipeline mit `Select-Object -First` (beendet die Pipeline, der Wert bleibt leer). Schritte in einem frischen `pwsh -NoProfile -File` ausprobieren, nicht in einer Sitzung mit altem Exit-Code (Review WP-10).
- Ein Job, der pushen darf, pinnt seine Actions per Commit-SHA (Tag als Kommentar); Dependabot hält sie aktuell.
- **Pflicht-Checks:** Das Ruleset „Protect main“ verlangt die CI-Jobs nach ihrem `name:` („Core, UI and tests (Linux)“, „Solution (Windows)“, „Line endings“) – einen Job umbenennen oder einen Pflicht-Job ergänzen nur zusammen mit dem Ruleset (`docs/release.md`), sonst hängt jeder PR.
