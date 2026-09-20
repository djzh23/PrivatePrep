# Branch-Struktur

Stand September 2026. Es gibt eine Live-Umgebung, kein Staging.

Render (Backend, dieses Repo) und Vercel (Frontend,
[SmartAssist-react](https://github.com/djzh23/SmartAssist-react)) deployen
aktuell von `v1-focus`. GitHub Actions Deploy-Jobs laufen nur bei Push auf
`main` und sind nicht der aktuelle Live-Pfad.

## Aktive Branches

### v1-focus

Zweck: Arbeits- und Live-Branch der geschlossenen Beta
Status: geschlossene Beta, aktive Entwicklung
Deploy: automatisch auf Render (Backend) und Vercel (Frontend). Das
GitHub-Actions-Workflow `deploy.yml` feuert hier nicht (nur `main`).

### main

Zweck: Default-Branch auf GitHub. Actions (Render-Hook / Vercel Production)
laufen nur bei Push auf `main`.
Status: hinter `v1-focus`; nicht die aktuelle Live-Quelle
Deploy: nicht ohne ausdrückliche Entscheidung mergen oder pushen. Kein
Auto-Push auf `main`.

### v2-features

Zweck: Features, die für V2 zurückgestellt wurden (CV Studio, Cover Letter,
Interview Prep, Portal Scanner)
Status: nicht in Entwicklung, nach V1 Public Launch schrittweise reaktivieren
Deploy: nie automatisch

## Remote-Branches ohne eigene Umgebung

### origin/staging und origin/develop

Existieren remote. Es ist kein Staging-Environment angeschlossen. In dieser
Session nicht einrichten.

Das Frontend-Repo hat zusätzlich lokale Branches `staging` und `develop` sowie
`backup/pre-v1-frontend-2026-09-15`.

## Archiv-Branches

### archive/v0-legacy-2026-09-17

Zweck: Zustand vor dem V1-Refactor, mit 5 AI-Modi, CV Studio, RAG, Redis
Status: eingefroren
Deploy: nie

### backup/pre-v1-refactor-2026-09-15

Zweck: Backup vor dem V1-Refactor, technisch identisch mit archive/v0-legacy
Status: eingefroren
Deploy: nie

## Tags

- `v0-legacy`: zeigt auf den Stand der ursprünglichen Version (Backend-Repo)
