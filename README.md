# PrivatePrep

Deutsche KI-gestützte Bewerbungsanalyse: Lebenslauf plus Stellenausschreibung
ergeben einen strukturierten Match-Report mit konkreten Bullet-Point-Vorschlägen.

Live: [betweenatna.com](https://betweenatna.com)

[![Build, Test & Deploy](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml/badge.svg)](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml)

## Status

Version 1, September 2026. Diese Version fokussiert auf einen einzigen Kern-Use-Case:
CV plus Stellenausschreibung ergeben einen strukturierten Match-Report mit Skill-Gap-
Analyse und Bullet-Rewrite-Vorschlägen. Weitere geplante Features (Chat, CV Studio,
Interview-Vorbereitung, Sprachlernen, Programmier-Hilfe) liegen im Branch
[v2-features](https://github.com/djzh23/PrivatePrep/tree/v2-features) und werden
nach V1-Launch schrittweise geprüft.

Die vorherige Version (5 AI-Modi, CV Studio, RAG, Redis) ist unter dem Tag
[v0-legacy](https://github.com/djzh23/PrivatePrep/tree/v0-legacy) sowie im
Branch [archive/v0-legacy-2026-09-17](https://github.com/djzh23/PrivatePrep/tree/archive/v0-legacy-2026-09-17)
archiviert. Historische README:
[docs/history/README-v0-legacy.md](docs/history/README-v0-legacy.md).

## About

PrivatePrep ist ein Solo-Projekt zur strukturierten Bewerbungsvorbereitung für den
deutschen Arbeitsmarkt. Nutzer legen ein Kurzprofil und eine persönliche Story an,
laden einen Lebenslauf hoch und fügen eine konkrete Stellenausschreibung ein.
Der Analyze-Endpoint erzeugt daraus:

1. Einen globalen Match-Score von 1.0 bis 5.0 mit vier Sub-Dimensionen (CV-Match,
   Rollen-Passung, Culture-Screen, Red Flags)
2. Eine Skill-Gap-Klassifikation: welche geforderten Skills sind im CV vorhanden,
   welche sind indirekt belegt, welche fehlen (Taxonomie in Deutsch und Englisch)
3. Bis zu fünf Bullet-Rewrite-Vorschläge, die bestehende CV-Punkte für die
   konkrete Ausschreibung umformulieren, ohne Fakten zu erfinden

Zwei Schutzschichten außerhalb des Language-Model-Calls verhindern typische Fehler
generativer Systeme: eine deterministische Skill-Klassifizierung vor dem Call und
eine Fact-Verification nach dem Call, die den Report blockiert, wenn der generierte
Text Skills oder Zahlen enthält, die nicht im CV oder in der Story stehen. Details
siehe Architektur-Abschnitt.

## Tech Stack

### Backend
- ASP.NET Core 9 (C# 13)
- PostgreSQL via Supabase, Entity Framework Core
- Clerk für Authentication (JWT)
- Stripe für Payments
- Groq API für LLM-Inferenz (`meta-llama/llama-4-scout-17b-16e-instruct`), kein Fallback-Provider in V1
- Serilog für strukturiertes Logging
- Deployment: Render (GitHub Actions Build, Test, Deploy-Hook)

### Frontend
- React 18, TypeScript, Vite, TailwindCSS, Clerk React SDK
- Deployment: Vercel
- Separates Repository: [SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

### Tooling
- xUnit für Tests, Moq für Mocks
- Docker Compose für den Container-Build (`docker compose up --build`), erwartet eine
  externe `DATABASE_URL`
- GitHub Actions für CI (Build, Test, Deploy)

## Architektur

Der zentrale Analyze-Endpoint (`POST /api/agent/analyze`) durchläuft eine feste Pipeline:

```
CV + Story + JD
    -> Input-Validierung (Laenge, Vollstaendigkeit)
    -> SkillGapService (deterministisch, ohne LLM)
    -> Prompt-Aufbau mit Skill-Gap als Kontext
    -> Groq Completion (mit einmaligem JSON-Retry bei Parse-Fehler)
    -> FactGateService (deterministisch, ohne LLM, Hard Block)
    -> AnalyzeReport zurueck an Client
```

Die beiden deterministischen Gates sind der Kern der Halluzinations-Prävention. Der
`SkillGapService` klassifiziert Skills aus der Stellenausschreibung gegen den CV-Text
in drei Buckets (vorhanden, indirekt belegt, fehlend) und übergibt das Ergebnis als
Referenz an den Prompt. Der `FactGateService` prüft den LLM-Output gegen CV und
Story: enthält der Output Skills oder Zahlen, die dort nicht stehen, wird der Report
ohne Bullet-Rewrites zurückgegeben, mit einer entsprechenden Warnung.

Stripe steuert das Nutzungslimit: anonym 2, kostenlos 3, Premium und Pro unbegrenzt
Analysen pro Tag (`UsageService.GetDailyLimit`).

Insgesamt bestehen 23 HTTP-Endpoints über 6 Controller (Agent, CV, Jobs, Onboarding,
Profile, Stripe), unter anderem für Profil-Onboarding, CV-Text-Extraktion (PdfPig),
Job-Posting-Vorschau und Stripe-Checkout/Webhook.

## Prompt-Design

Die Prompt-Struktur ist inspiriert von den öffentlich dokumentierten Rubriken des
career-ops-Projekts. Der System-Prompt liegt in
`PrivatePrep/Services/Agent/Prompts/analyze-system-prompt.md`. Kern-Regeln:

- CV und User-Story sind die einzigen Quellen für Skill- und Metrik-Angaben
- Reformulierung ist erlaubt, Erfindung ist verboten
- Die Stellenausschreibung wird als Untrusted-Input behandelt (Schutz gegen Prompt-Injection)

## Lokale Entwicklung

Voraussetzungen:
- .NET 9 SDK
- Groq API Key (kostenlos: console.groq.com)
- Zugriff auf eine Postgres-Instanz (z.B. Supabase)

Setup:

```bash
git clone https://github.com/djzh23/PrivatePrep.git
cd PrivatePrep

dotnet user-secrets set "Groq:ApiKey" "<dein-key>" --project PrivatePrep
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=...;Username=...;Password=..." --project PrivatePrep
dotnet user-secrets set "Clerk:Issuer" "https://your-instance.clerk.accounts.dev" --project PrivatePrep
dotnet user-secrets set "Stripe:SecretKey" "<dein-key>" --project PrivatePrep

dotnet run --project PrivatePrep
```

API läuft dann auf `http://localhost:5108`. Legal-Stubs:
`http://localhost:5108/legal/impressum.html`, `http://localhost:5108/legal/datenschutz.html`.
Für das Frontend siehe das separate Repo
[SmartAssist-react](https://github.com/djzh23/SmartAssist-react).

Alternativ per Docker: `docker compose up --build` (erwartet die Umgebungsvariablen
aus `docker-compose.yml`, unter anderem eine externe `DATABASE_URL`).

Beispiel-Analyse (JWT eines Users mit hochgeladenem CV):

```bash
curl -X POST http://localhost:5108/api/agent/analyze \
  -H "Authorization: Bearer $CLERK_JWT" \
  -H "Content-Type: application/json" \
  -d "{\"jobDescription\":\"Anforderungen\\nC# und ASP.NET Core fuer das Backend-Team.\"}"
```

## Tests

```bash
dotnet test
```

94 Tests decken unter anderem die zwei deterministischen Gates (SkillGapService,
FactGateService), Stripe-Webhook-Verarbeitung, Job-Context-Extraktion und
Admin-Autorisierung ab.

## Docs

- `PrivatePrep/docs/blueprint/`: Prompt- und Rubrik-Analyse, Quelle für das V1-Design
  (Attribution in `NOTICE.md`)
- `PrivatePrep/docs/blueprint/00-v1-v2-decisions.md`: V1- und V2-Scope-Definition
- `docs/history/`: archivierte Dokumente aus früheren Versionen
- `docs/deployment/`: Pre-Launch-Checkliste und offene technische Schulden

## Attribution

Die Scoring- und Prompt-Struktur ist inspiriert von
[career-ops](https://github.com/santifer/career-ops) (MIT License, Copyright
Santiago Fernández de Valderrama). Details siehe [NOTICE.md](NOTICE.md).

## License

Proprietäre Software. Alle Rechte vorbehalten. Kontakt: [betweenatna.com](https://betweenatna.com).

## Kontakt

Zouhair, B.Sc. Angewandte Informatik. GitHub: [djzh23](https://github.com/djzh23).
