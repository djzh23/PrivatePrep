# PrivatePrep

**Deutsche KI-gestützte Bewerbungsanalyse.** Lade deinen Lebenslauf hoch, füge eine Stellenausschreibung ein, bekomme einen strukturierten Match-Report mit konkreten Bullet-Point-Verbesserungen.

🌐 **Live:** [betweenatna.com](https://betweenatna.com)  
🛠 **Tech:** ASP.NET Core 9 · React · PostgreSQL · Groq · Clerk · Stripe · Render

[![Build, Test & Deploy](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml/badge.svg)](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml)

## Was PrivatePrep macht

Du beschreibst dich (Profil + Story) und lädst deinen Lebenslauf hoch. Dann zeigst du eine Stelle (Text oder zuvor geparste JD). Das Backend klassifiziert fehlende Skills deterministisch, bewertet den Fit holistisch (1.0–5.0, ohne Gehalts-Dimension) und schlägt 3–5 umformulierte CV-Bullets vor — ohne erfundene Zahlen oder Skills.

Ein Fact-Gate prüft den Modell-Output nach dem LLM-Call und blockt ihn, wenn Metriken, Skills oder verbotene Floskeln nicht im CV oder in der Story stehen.

V1 hat einen Kern-Endpoint: `POST /api/agent/analyze`. Chat, CV-Studio, Interview-Prep und weitere Modi liegen auf dem Branch `v2-features`.

## Architektur

ASP.NET Core 9 API, Auth über Clerk-JWT, Profile und CV in Supabase PostgreSQL, ein LLM-Call über Groq (`llama-3.3-70b-versatile`). Vor dem Call läuft `SkillGapService` (Taxonomie, kein LLM), danach `FactGateService`. Stripe steuert Free (3 Analysen/Tag) vs. Premium. Das React-Frontend ist ein separates Repo.

```
CV + Story + JD
    → SkillGap (zero-LLM)
    → Groq Analyze-Prompt
    → FactGate (zero-LLM, Hard Block)
    → Report (Score, Gaps, Bullet-Rewrites)
```

Frontend: [github.com/djzh23/SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

## Lokale Entwicklung

Voraussetzungen: .NET 9 SDK. Docker ist optional (`docker compose up --build`).

```bash
git clone https://github.com/djzh23/PrivatePrep.git
cd PrivatePrep
dotnet user-secrets set "Groq:ApiKey" "..." --project PrivatePrep
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=...;Username=...;Password=..." --project PrivatePrep
dotnet user-secrets set "Clerk:Issuer" "https://your-instance.clerk.accounts.dev" --project PrivatePrep
dotnet user-secrets set "Stripe:SecretKey" "..." --project PrivatePrep
dotnet run --project PrivatePrep
```

API: `http://localhost:5108`  
Legal-Stubs: `http://localhost:5108/legal/impressum.html`, `http://localhost:5108/legal/datenschutz.html`

```bash
dotnet test
```

Beispiel-Analyse (JWT eines Users mit hochgeladenem CV):

```bash
curl -X POST http://localhost:5108/api/agent/analyze \
  -H "Authorization: Bearer $CLERK_JWT" \
  -H "Content-Type: application/json" \
  -d "{\"jobDescription\":\"Anforderungen\\nC# und ASP.NET Core fuer das Backend-Team. Die Stelle ist unbefristet, das Team sitzt in Deutschland und arbeitet hybrid.\"}"
```

## Attribution

Die Scoring- und Prompt-Struktur ist inspiriert von [career-ops](https://github.com/santifer/career-ops) (MIT). Siehe `NOTICE.md`.

## License

Proprietary. Alle Rechte vorbehalten.
