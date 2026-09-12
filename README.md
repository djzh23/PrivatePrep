# PrivatePrep

ASP.NET Core 9 backend for **PrivatePrep** ([betweenatna.de](https://www.betweenatna.de)), an AI-powered career workspace that helps job seekers prepare interviews, analyse job listings, manage applications and build CVs.

**Live API:** [smartassist-api.onrender.com](https://smartassist-api.onrender.com)
**Frontend:** [github.com/djzh23/SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

## What it does

The API is the sole backend for a production SaaS. It handles:

- **AI agent pipeline** with five specialised modes (career coach, job analyser, interview prep, language learning, programming). Requests are routed by tool type, system prompts are built dynamically with a cached/uncached split for Anthropic-style prompt caching, and responses are streamed over SSE.
- **Retrieval-augmented memory** using pgvector and ONNX sentence embeddings. Insights extracted from conversations are embedded and retrieved at inference time to give the agent long-term context about the user's career.
- **Full career workspace** covering profile onboarding, CV parsing (PdfPig), job applications with pipeline status tracking, chat sessions with transcripts, saved notes and a learning insights feed.
- **CV Studio** with resume CRUD, snapshot versioning, category management, and export to PDF (QuestPDF) and DOCX (OpenXML).
- **Auth and billing** via Clerk JWT verification and Stripe subscriptions with checkout, portal and webhook handling.
- **Resilient storage layer** with a Postgres/Redis pair per feature domain. If Supabase is unreachable, features degrade to Redis and signal this via response headers rather than returning errors.

## Tech stack

| Area | Technology |
|---|---|
| Framework | ASP.NET Core 9, C# 13 |
| Primary LLM | Groq (llama-3.3-70b-versatile) |
| Fallback LLM | Anthropic Claude Sonnet and Haiku |
| Database | Supabase PostgreSQL with pgvector |
| Cache | Upstash Redis REST |
| Auth | Clerk JWT (JWKS verification) |
| Payments | Stripe |
| TTS | Azure Cognitive Speech |
| CV export | QuestPDF, OpenXML |
| Embeddings | ONNX runtime with a local sentence-transformer model |
| Tests | xUnit (196 tests) |
| CI/CD | GitHub Actions to Render via deploy hook, Dockerised |

## Architecture

```
PrivatePrep/
  Controllers/        one controller per feature domain
  Services/           business logic, Postgres/Redis pairs, Groq, Clerk, Stripe, Azure
  Services/Tools/     pluggable agent tools (JobAnalyzer, LanguageLearning, ...)
  Services/VectorStore/  pgvector ingest and retrieval for career memory
  Data/               PrivatePrepDbContext, embedded SQL migrations
  Middleware/         JWT resolution, per-request user context
  vendor/cv-studio/   resume domain with QuestPDF and EF Core schema
```

The main `AgentService` builds a two-part system prompt (a stable cached prefix and a dynamic uncached block) to minimise token cost on repeated requests. Tool routing, LLM selection and context assembly all happen inside the service before any LLM call is made.

Startup runs EF Core migrations and embedded SQL scripts synchronously so the database schema is always in sync before the first request is served.

## AI modes

| Tool type | Behaviour |
|---|---|
| `general` | Career coaching and open advice |
| `jobanalyzer` | Keyword extraction, gap analysis and CV tips from a job ad |
| `interview` / `interviewprep` | STAR-style interview practice |
| `language` | Target-language conversation with translation and grammar tips |
| `programming` | Code help with Markdown output |

## API surface (selected)

| Group | Endpoints |
|---|---|
| Agent | POST `/api/agent/stream` (SSE), `/ask`, `/demo`, `/context`, `/speak`, `/usage` |
| Profile | GET/PUT `/api/profile`, onboarding flow, CV upload and parse, target jobs |
| Sessions | CRUD `/api/sessions`, transcript read/write |
| Notes | CRUD `/api/chat-notes` |
| Applications | CRUD `/api/applications`, status, cover letter, interview notes |
| CV Studio | Resumes, versions, categories, PDF/DOCX export |
| Payments | `/api/stripe/checkout`, `/portal`, `/webhook`, `/confirm-plan` |
| Admin | Usage dashboards, token stats, Redis to Postgres backfill |

Full endpoint list is in the controller source files.

## Subscription plans

| Plan | Messages per day |
|---|---|
| Anonymous | 2 |
| Free | 20 |
| Premium | 200 |
| Pro | Unlimited |

## Frontend

The React client lives at [github.com/djzh23/SmartAssist-react](https://github.com/djzh23/SmartAssist-react) and is deployed on Vercel at [betweenatna.de](https://www.betweenatna.de).

| Area | Technology |
|---|---|
| Framework | React 18 + TypeScript + Vite |
| Styling | Tailwind CSS v3 |
| Auth | Clerk (publishable key via `VITE_CLERK_PUBLISHABLE_KEY`) |
| Icons | Lucide React |
| Routing | React Router v6 |
| Deployment | Vercel |

The client calls `/api/*` routes exclusively. In development Vite proxies those requests to the local backend (`VITE_PROXY_TARGET=http://localhost:5108`). In production `VITE_API_BASE_URL` points to the Render deployment.

```bash
git clone https://github.com/djzh23/SmartAssist-react.git
cd SmartAssist-react
cp .env.example .env.local   # fill in VITE_CLERK_PUBLISHABLE_KEY
npm install
npm run dev                   # proxies /api/* to http://localhost:5108
```

## Local development

Requires .NET 9 SDK.

```bash
git clone https://github.com/djzh23/PrivatePrep.git
cd PrivatePrep
dotnet run --project PrivatePrep
```

The API binds to `http://localhost:5108`. Point the React dev proxy (`VITE_PROXY_TARGET`) at that URL.

Add secrets via `dotnet user-secrets` or `appsettings.Development.json`:

```json
{
  "Groq": { "ApiKey": "gsk_..." },
  "ConnectionStrings": { "Supabase": "Host=...;Username=...;Password=..." },
  "Upstash": { "RestUrl": "https://...", "RestToken": "..." },
  "Clerk": { "Issuer": "https://your-instance.clerk.accounts.dev" },
  "Stripe": { "SecretKey": "sk_test_...", "WebhookSecret": "whsec_..." }
}
```

Optional: `AZURE_SPEECH_KEY`, `GROQ_API_KEY`, `ADMIN_USER_IDS`, `CORS_ALLOWED_ORIGINS`.

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
