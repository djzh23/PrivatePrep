# PrivatePrep API

[![Build, Test & Deploy](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml/badge.svg)](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml)

ASP.NET Core 9 backend for [PrivatePrep](https://www.betweenatna.de), an AI-powered career workspace that helps job seekers prepare for interviews, analyse job listings, manage applications and build CVs.

**Live API:** [smartassist-api.onrender.com](https://smartassist-api.onrender.com)  
**Frontend:** [github.com/djzh23/SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

## Features

- **AI agent pipeline** with five specialised modes routed by tool type. System prompts are assembled dynamically with a cached/uncached split to reduce token cost, and responses are streamed token-by-token over SSE.
- **Retrieval-augmented memory** using pgvector and local ONNX sentence embeddings. Insights extracted from conversations are embedded and retrieved at inference time for persistent career context.
- **Career workspace** covering profile onboarding, CV upload and AI parsing (PdfPig), job applications with pipeline status tracking, chat sessions with transcripts, notes and a learning insights feed.
- **CV Studio** with resume CRUD, snapshot versioning, category management, and export to PDF (QuestPDF) and DOCX (OpenXML).
- **Auth and billing** via Clerk JWT verification (JWKS) and Stripe subscriptions with checkout, portal and webhook handling.
- **Dual-storage architecture** per feature domain: a PostgreSQL table paired with a Redis key. The active backend is reported transparently in response headers.

## Tech Stack

| Area | Technology |
|---|---|
| Framework | ASP.NET Core 9, C# 13 |
| LLM | Groq API (configurable model) |
| Database | Supabase PostgreSQL with pgvector |
| Cache | Upstash Redis |
| Embeddings | ONNX runtime (local sentence-transformer) |
| Auth | Clerk JWT |
| Payments | Stripe |
| TTS | Azure Cognitive Speech |
| CV export | QuestPDF, OpenXML SDK |
| Tests | xUnit |
| CI/CD | GitHub Actions + Render (Docker) |

## Project Structure

```
PrivatePrep/
  Controllers/          15 focused controllers, one per feature domain
  Services/
    Agent/              LLM pipeline, tool routing, prompt assembly
    Chat/               Sessions, transcripts, conversation state
    CvStudio/           Resume logic, PDF/DOCX rendering
    Payments/           Stripe checkout, portal, webhook handling
    Profile/            Career profile, CV parsing, onboarding
    Tracking/           Daily usage limits, token cost tracking
    VectorStore/        pgvector ingestion and RAG retrieval
    ...                 Auth, Background, Learning, Notes, Speech, Embeddings
  Data/                 EF Core context, migrations, embedded SQL scripts
  Middleware/           JWT resolution, per-request user context
```

## AI Modes

| Tool | Behaviour |
|---|---|
| `general` | Open career coaching |
| `jobanalyzer` | Gap analysis and keyword extraction from a job listing |
| `interview` | STAR-style interview practice |
| `language` | Conversation practice with grammar and translation feedback |
| `programming` | Code help with syntax-highlighted Markdown responses |

## Subscription Plans

| Plan | Messages / day |
|---|---|
| Anonymous | 2 |
| Free | 20 |
| Premium | 200 |
| Pro | Unlimited |

## Local Development

Requires .NET 9 SDK.

```bash
git clone https://github.com/djzh23/PrivatePrep.git
cd PrivatePrep
dotnet run --project PrivatePrep
```

Configure secrets via `dotnet user-secrets` or `appsettings.Development.json`:

```json
{
  "Groq":              { "ApiKey": "..." },
  "ConnectionStrings": { "Supabase": "Host=...;Username=...;Password=..." },
  "Upstash":           { "RestUrl": "...", "RestToken": "..." },
  "Clerk":             { "Issuer": "https://your-instance.clerk.accounts.dev" },
  "Stripe":            { "SecretKey": "...", "WebhookSecret": "..." }
}
```

```bash
dotnet test
docker compose up --build
```

## Deployment

```
push to main -> GitHub Actions (build + test) -> Render deploy hook -> Docker on Render
```

## License

MIT
