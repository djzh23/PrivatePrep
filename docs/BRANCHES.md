# Branch-Struktur

Stand 25. September 2026. Es gibt eine Live-Umgebung, kein Staging.

Render (Backend, dieses Repo) und Vercel (Frontend,
[privateprep-frontend](https://github.com/djzh23/privateprep-frontend)) deployen
von `main`.

## Aktiver Branch

### main

Zweck: einziger Arbeits- und Live-Branch (vormals `v1-focus`, im September 2026
konsolidiert)
Status: aktive Entwicklung
Deploy: automatisch auf Render (Backend) und Vercel (Frontend, ueber GitHub
Actions). Deploy-Jobs laufen bei Push auf `main`.

Alle frueheren Branches (`v1-focus`, `v2-features`, `staging`, `develop`,
`privateprep-v2`, `feat/analyse-v2`, `archive/v0-legacy-2026-09-17`,
`backup/pre-v1-refactor-2026-09-15`) waren reine Vorfahren von `v1-focus` und
wurden bei der Konsolidierung geloescht, ohne dass Commits verloren gingen.
Das Frontend-Repo hatte dieselbe Struktur und wurde parallel konsolidiert.

## Tags

- `v0-legacy`: zeigt auf den Stand der ursprünglichen Version (Backend-Repo)
