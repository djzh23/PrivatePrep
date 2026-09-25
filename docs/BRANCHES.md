# Branch-Struktur

Stand September 2026. Es gibt eine Live-Umgebung, kein Staging.

Render (Backend, dieses Repo) und Vercel (Frontend,
[SmartAssist-react](https://github.com/djzh23/SmartAssist-react)) deployen
von `main`.

## Aktive Branches

### main

Zweck: Arbeits- und Live-Branch der geschlossenen Beta
Status: geschlossene Beta, aktive Entwicklung
Deploy: automatisch auf Render (Backend) und Vercel (Frontend). GitHub
Actions Deploy-Jobs laufen bei Push auf `main`.

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
