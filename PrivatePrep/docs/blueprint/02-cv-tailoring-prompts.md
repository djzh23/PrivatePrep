> **Attribution / MIT-Lizenz-Hinweis.** Dieser Blueprint extrahiert Prompt- und Scoring-Logik aus [career-ops](https://github.com/santifer/career-ops) (MIT License).
>
> Copyright (c) 2026 Santiago Fernández de Valderrama
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
>
> Quelldateien: `modes/pdf.md`, `batch/batch-prompt.md` Step 4, `modes/_writing.md`, `modes/_shared.md`, `modes/heuristics/recruiter-side.md`, `modes/latex.md`, `verify-cv-facts.mjs`, `jd-skill-gap.mjs`, `AGENTS.md` (Source-of-Truth Boundary).

# 02 — CV-Tailoring-Prompts (JD + cv.md → angepasster Lebenslauf)

career-ops schreibt **nie** in `cv.md`. Die Quelle bleibt unangetastet. Tailoring erzeugt eine *Ableitung*: JSON-Payload → HTML (`build-cv-html.mjs`) → PDF (`generate-pdf.mjs`), oder den LaTeX-Zwilling (`modes/latex.md`).

---

## 1. System-Kontext, der vor jedem Tailoring gilt

Aus `modes/_shared.md` § Sources of Truth (wörtlich):

```text
The files below are the **ONLY** sources for user-facing content (CV, cover letters, form answers, recruiter outreach).

| File | Path | When |
|------|------|------|
| cv.md | `cv.md` (project root) | ALWAYS |
| article-digest.md | `article-digest.md` (if exists) | ALWAYS (detailed proof points) |
| profile.yml | `config/profile.yml` | ALWAYS (candidate identity and targets) |
| _profile.md | `modes/_profile.md` | ALWAYS (user archetypes, narrative, negotiation) |
| writing-samples/ | `writing-samples/` | When generating candidate-facing text — check `_profile.md` for cached `## Writing Style` first; only scan files if absent |
| voice-dna.md | `voice-dna.md` (project root, if exists) | When generating candidate-facing text. Anti-AI-slop guardrail + voice. See Voice DNA precedence below. |
| interview-prep | `interview-prep/story-bank.md`, `interview-prep/{company}-{role}.md` | When generating ATS form answers / interview content — … quantified claims are NOT automatically cv.md-equivalent |
| _custom.md | `modes/_custom.md` (if exists) | ALWAYS (user house rules …). Procedural rules only — never a content source for claims |

**RULE: NEVER hardcode metrics from proof points.** Read them from cv.md + article-digest.md at evaluation time.
**RULE: For article/project metrics, article-digest.md takes precedence over cv.md.**
**RULE: NEVER claim the user authored a project, repo, library, tool, framework, or open-source artefact unless explicitly attributed to them in cv.md or article-digest.md.** Tool-of-trade conflation (user uses X → user built X) is the most common fabrication pattern and is forbidden.
**RULE: Keywords get reformulated, never fabricated.** Reorder, reframe, emphasise — but never invent. If a claim isn't backed by an in-scope file, ask the user. If no answer, omit. Silence on a topic beats manufactured detail.
```

`AGENTS.md` verdichtet dasselbe zum Design-Satz:

```text
**Rule from the original design:** *"Keywords get reformulated, never fabricated."* Reorder, reframe, emphasise — but never invent. If a claim isn't backed by an in-scope file, ask the user; if they don't add it, the output goes without it. Silence on a topic is fine; manufactured detail is not.
```

NEVER-Liste (Auszug, `_shared.md`):

```text
### NEVER

1. Invent experience or metrics
2. Modify cv.md or portfolio files
…
6. Generate a PDF without reading the JD first
7. Use corporate-speak
```

---

## 2. Der eigentliche Tailoring-Prompt (`modes/pdf.md`)

Es gibt **keinen** separaten System-/User-Chat-Prompt wie bei OpenAI-eval. Der Agent *ist* das Modell; `pdf.md` ist die vollständige Instruktion. In einem SaaS müsst ihr genau diese Steps als System-Prompt serialisieren.

### 2.1 Pipeline (wörtliche Step-Liste)

```text
1. Read `cv.md` as the source of truth
2. Ask the user for the JD if it is not in context (text or URL)
3. Extract 15-20 keywords from the JD
4. Run the zero-LLM skill-gap check before drafting anything: write the JD to a scratch file (e.g. `jds/{slug}.md`) if it isn't already one, then `node jd-skill-gap.mjs jds/{slug}.md --summary`. This classifies the JD's explicit requirements against `cv.md` into three buckets — never surface `result.gap` items as if the candidate has them:
   - `existing` — already a named skill in cv.md's Skills section, safe to lead with
   - `supportedByResume` — not a named skill yet, but cv.md's prose already demonstrates it; legitimate candidates for the Skills section in the user's own words (Step 13's competency grid draws from here first)
   - `gap` — cv.md has no trace of it at all. **Tell the user explicitly which skills are gaps before generating the CV.** Never paper over a gap by inventing a claim, and never silently drop it from the conversation — the user decides whether to proceed, address it in the cover letter/interview, or skip the role
5. Use `language.output` for the CV language. The JD language and `language.modes_dir` supply market vocabulary and evaluation context, but never override the configured output language.
6. Detect company location → paper format:
   - US/Canada → `letter`
   - Rest of the world → `a4`
7. Detect role archetype → adapt framing
8. Before tailoring, optionally compare the new JD with the latest tailored CV or JD. … Run `npm run jd:similarity -- {new-jd.txt} {previous-jd-or-cv.txt}` …
9. Build an internal recruiter-side risk map from the JD using `modes/heuristics/recruiter-side.md`
10. Rewrite Professional Summary by injecting JD keywords + exit narrative bridge ("Built and sold a business. Now applying systems thinking to [JD domain].")
11. Select top 3-4 most relevant projects for the job. If `cv.md` carries an Awards / Honors section, populate `awards[]` … never invent an award to fill it
12. Reorder experience bullets by JD relevance and by the risk map: strongest matching evidence first
13. Build competency grid from JD requirements (6-8 keyword phrases), prioritizing `existing` and `supportedByResume` skills from Step 4 — never a `gap` skill
14. Inject keywords naturally into existing achievements (NEVER invent)
15. Apply the six-second clarity gate from `modes/heuristics/recruiter-side.md`: top third must make target role, strongest fit, and proof obvious
16. Read `name` from `config/profile.yml` → normalize to kebab-case lowercase …
17. Build the render payload … compact structured JSON, **not** full HTML markup …
18. Run `node build-cv-html.mjs …`
19. Run the fact gate against the generated HTML: `node verify-cv-facts.mjs {html-path}`
    - This is a hard gate before PDF rendering.
    - If it fails, stop and fix the generated HTML by removing invented metrics or adding verified evidence to `cv.md`, `article-digest.md`, or `config/cv-facts.json`.
20. Hiring-manager audit — off by default, opt-in only. (`modes/pdf/hm-audit.md`)
21. Execute: `node generate-pdf.mjs {html-path} {pdf-path} --format={letter|a4} --report={report number}`
22. Report: PDF path, number of pages, keyword coverage %, and any skill gaps from Step 4 still unaddressed
```

**LOW-CONFIDENCE-Gate für Step 4 (wörtlich, wenn der Skill-Check nichts klassifiziert):**

```text
> ⚠️ **Skill-gap check inconclusive:** [Render in {language.output}: state that the automated skill-gap check returned no classified skills for this JD and so cannot be read as "no gaps"; name which of the three shapes occurred from the reason code (requirements section never found, or found but no candidates extracted, or the JD file was empty); … otherwise say that you will read the JD directly to identify required skills before drafting. Keep the CLI's own English diagnostic out of the user-facing message.]
```

Reason codes: `no-requirements-section` | `no-skill-candidates` | `empty-jd`.

**Modell-Hinweis:** Step 4 und Step 19 sind **zero-LLM**. Die gehören in PrivatePrep als deterministische Services *vor* und *nach* dem LLM-Call, nicht als Prompt-Hoffnung.

### 2.2 Batch-Worker-Variante (self-contained, `batch/batch-prompt.md`)

Wird nur ausgeführt wenn `score >= auto_pdf_score_threshold` (Default 3.0). Wörtlich:

```text
If score is greater than or equal to the threshold:

1. Read `cv.md`, `article-digest.md`, and `templates/cv-template.html`.
2. Extract 15-20 JD keywords.
3. Use `language.output` for CV prose.
4. Choose paper format: US/Canada -> `letter`; otherwise `a4`.
5. Adapt framing to the detected archetype.
6. Rewrite the Professional Summary with real evidence and relevant keywords.
7. Select the most relevant projects and proof points.
8. Reorder experience bullets by relevance.
9. Build a 6-8 item competency grid.
10. Inject keywords ethically into existing achievements; never invent skills or metrics.
11. Write HTML to `output/cv-candidate-{company-slug}.html`.
12. Run: node generate-pdf.mjs …

ATS rules:

- Single column, no sidebars.
- Standard section headers.
- No critical information in images, SVGs, headers, or footers.
- UTF-8 selectable text.
- Keywords distributed naturally across summary, experience, skills, and projects.
```

Unterschied zum Agent-Pfad: Batch **überspringt** `jd-skill-gap.mjs`, Similarity-Reuse, Recruiter-Risk-Map, Fact-Gate-Wortlaut in der Step-Liste (das Script `generate-pdf.mjs` kann den Gate intern trotzdem fahren), und HM-Audit. Für SaaS: Agent-Pfad ist die Qualitätslinie, Batch ist die billige Linie.

---

## 3. Welche CV-Teile werden umgeschrieben, welche bleiben?

| Teil | Aktion | Prompt-Anker |
|------|--------|--------------|
| **Master `cv.md`** | **unberührt** | NEVER modify cv.md |
| Header (Name, Kontakt, Links) | unverändert aus `profile.yml` | JSON `candidate.*` |
| Foto | nur wenn `candidate.photo` gesetzt (DACH-opt-in) | US/UK: weglassen |
| **Professional Summary** | **wird neu geschrieben** | Step 10: JD-Keywords + Exit-Narrative-Bridge |
| **Core Competencies (6–8 Tags)** | **neu gebaut** aus JD, nur `existing` + `supportedByResume` | Step 13 |
| **Work Experience: Employer, Titel, Daten, Ort** | **bleiben** | JSON `experience[].company/role/dates/location` |
| **Work Experience: Bullets** | **reorder + keyword-inject**, Inhalt muss in cv.md existieren | Step 12 + 14 |
| **Projects** | **Selektion** Top 3–4 relevanteste; Texte umformulieren, nicht erfinden | Step 11 |
| Awards | nur wenn in cv.md; sonst Section droppen | "never invent an award" |
| Education / Certifications | bleiben; optional Reorder | Schema `education[]` / `certifications[]` |
| Skills-Liste | umsortieren / JD-Vokabular auf *vorhandene* Skills; keine Gap-Skills | Step 4 + 13 |
| Section-Reihenfolge | fix "6-second recruiter scan" | Header → Summary → Competencies → Experience → Projects → Education → Skills |

Exit-Narrative-Bridge im Default-Prompt (wörtlich, Upstream-Founder-Annahme):

```text
Rewrite Professional Summary by injecting JD keywords + exit narrative bridge ("Built and sold a business. Now applying systems thinking to [JD domain].")
```

**PrivatePrep muss diesen Satz aus `_profile.md` lesen**, nicht hardcoden. In diesem Fork steht dort z. B. die Headline „.NET-Entwickler mit selbst gebauter, deployter KI-Bewerbungshilfe" — das ist die Bridge.

Six-second gate (`modes/heuristics/recruiter-side.md`, wörtlich):

```text
## Six-Second Clarity Gate

For every CV/PDF and cover letter, the top third must make the target fit
impossible to miss:

- target role/archetype
- strongest matching stack or domain
- one production or business outcome
- location/remote fit only when appropriate for that document
- portfolio/case-study link when available and relevant

If a recruiter must infer fit from scattered bullets, rewrite the summary and
first experience bullets.
```

Business-Value-Bullets (wörtlich):

```text
Prefer:

`Action + system/scope + tool/approach + outcome + proof`

Good patterns:

- `Resolved [problem] in [system], improving [business/system effect].`
- `Built [capability] with [tools], enabling [user/team outcome].`
- `Migrated [old] to [new], reducing [risk/cost/latency/debt].`
- `Improved [metric] from [before] to [after] by [technical action].`

Avoid weak starts when stronger ownership is true: "helped", "assisted",
"responsible for", "worked on", "participated in".
```

---

## 4. Keyword-Injection, ohne dass es "AI-generated" klingt

### 4.1 Die erlaubte Reformulierung (`modes/pdf.md`, wörtlich)

```text
## Keyword injection strategy (ethical, truth-based)

Examples of legitimate reformulation:
- JD says "RAG pipelines" and CV says "LLM workflows with retrieval" → change to "RAG pipeline design and LLM orchestration workflows"
- JD says "MLOps" and CV says "observability, evals, error handling" → change to "MLOps and observability: evals, error handling, cost monitoring"
- JD says "stakeholder management" and CV says "collaborated with team" → change to "stakeholder management across engineering, operations, and business"

**NEVER add skills that the candidate does not have. Only reword real experience using the exact JD vocabulary.**
```

ATS-Verteilung (wörtlich):

```text
- Distributed JD keywords: Summary (top 5), first bullet of each role, Skills section
- No hidden text, keyword stuffing, or white-font tricks. Optimize for parseability plus human review.
```

Cover-Letter-Schwesterregel (gilt analog, `modes/cover.md` Step 4 — nützlich als CV-Prinzip):

```text
**Application rules (enforced during drafting):**
- Mirror their vocabulary, not their structure
- Content stays from cv.md — only vocabulary shifts
- Fit naturally or don't use — if a keyword can't be woven in, flag it post-generation
- Use each keyword once — never repeat for density
```

Recruiter-Heuristik:

```text
Optimize for parseability and human review, not "ATS hacks":
- exact JD keywords only in truthful context
- no hidden text
- no keyword stuffing
- no white-font tricks
```

### 4.2 Anti-AI-Slop auf dem CV (Tier 1 only)

`modes/_writing.md` trennt zwei Tiers. **CV/ATS bekommt nur Tier 1** (Banned List, keine Em-Dashes, keine Negative Parallelisms). Tier 2 (Contractions, "And/But"-Opener, Hedging) ist **verboten** auf CV-Bullets — das würde genau nach AI-Cover-Letter klingen.

```text
**Two-tier scope (this is what keeps CVs accurate):**

- **Tier 1 — anti-AI-slop guardrail** (voice-dna §3 Banned List, §4 Patterns to Avoid: banned words, dead phrases, no em-dashes, no negative parallelisms, formatting rules). These are HARD RULES. They apply to **all** generated text, including CV bullets and the Professional Summary.
- **Tier 2 — conversational voice** (voice-dna §1-2: contractions, And/But sentence openers, hedging like "I think"/"maybe", parenthetical asides, direct "I"/"you"). Apply **only** to conversational candidate-facing prose: cover letters, LinkedIn outreach, follow-up emails. **Do NOT apply Tier 2 to CV/ATS text** (PDF bullets, Professional Summary) — those keep the formal, keyword-dense register in the ATS Rules below.

**Accuracy always wins over style.** Facts from `cv.md` and `article-digest.md` are never overridden by voice-dna. Never drop, soften, or hedge a real metric to improve rhythm. Never invent detail to sound more human. Voice-dna shapes wording; it never changes content.
```

Cliché-Fallback, falls kein `voice-dna.md` (`_writing.md`, wörtlich):

```text
- "passionate about" / "results-oriented" / "proven track record"
- "leveraged" (use "used" or name the tool)
- "spearheaded" (use "led" or "ran")
- "facilitated" (use "ran" or "set up")
- "synergies" / "robust" / "seamless" / "cutting-edge" / "innovative"
- "in today's fast-paced world"
- "demonstrated ability to" / "best practices" (name the practice)
```

`voice-dna.md` §3A ist die schärfere Liste (delve, tapestry, leverage, holistic, seamless, robust, …). **Ein einziges Trefferwort = Fail.**

Satzbau-Regeln für ATS-Text:

```text
### Vary sentence structure
- Don't start every bullet with the same verb
- Mix sentence lengths (short. Then longer with context. Short again.)
- Don't always use "X, Y, and Z" — sometimes two items, sometimes four

### Prefer specifics over abstractions
- "Cut p95 latency from 2.1s to 380ms" beats "improved performance"
- "Postgres + pgvector for retrieval over 12k docs" beats "designed scalable RAG architecture"
- Name tools, projects, and customers when allowed
```

**Was in der Praxis "nicht AI-generated" macht:** (1) JD-Wort *einmal* an der Stelle, wo die echte Erfahrung sitzt; (2) Metrik unverändert aus cv.md; (3) Verben rotieren; (4) keine Em-Dashes; (5) Summary 3–4 Zeilen, nicht ein Essay.

### 4.3 Bold ist Attention, kein Claim

```text
Emphasis is not a substitute for evidence — bold reorders attention, it does not add claims. The no-fabrication rule applies to bolded text exactly as it does to the rest of the bullet, and bolding every other phrase emphasises nothing.
```

Payload-Konvention: `**120 ms**` im JSON, der Builder macht `<strong>`.

---

## 5. Halluzinationsschutz (geschichtet, nicht ein Prompt)

career-ops verlässt sich **nicht** darauf, dass das Modell "nicht erfindet". Es gibt vier Schichten:

### Schicht 1 — Prompt-Verbot (notwendig, nicht hinreichend)

Siehe Sources of Truth + NEVER invent. Reicht allein nicht.

### Schicht 2 — Zero-LLM Skill-Gap *vor* dem Draft (`jd-skill-gap.mjs`)

Klassifikation gegen cv.md. `gap`-Skills dürfen nicht in Competencies oder als Können auftauchen. User muss Gaps **vor** der Generierung sehen.

**DE-Markt-Falle:** Der Extractor mag kapitalisierte Tokens. Deutsche JDs ("kenntnisse in csharp, sql, git") liefern oft `no-skill-candidates`. Der Prompt zwingt dann das Modell, die JD selbst zu lesen — und genau dann steigen Halluzinationen. PrivatePrep sollte einen DE-fähigen Skill-Extractor bauen (kein Capitalization-Filter).

### Schicht 3 — Fact-Gate *nach* dem HTML (`verify-cv-facts.mjs`)

Hard Gate vor PDF. Prüft metric-like Claims plus explizit behauptete Employer, Titles, Tools gegen `cv.md`, `article-digest.md`, optional `config/cv-facts.json`.

Verdicts: `pass` | `warn` | `block`. `block` stoppt das PDF.

Design-Lektionen aus dem Script-Header (warum Prompt allein scheitert):

- Modifier-Fenster zwischen Zahl und Nomen musste auf **4 Wörter** (`MODIFIER_WINDOW = 4`), sonst matchte "~5 live Cloud Run deployments" nicht gegen "~5 Cloud Run deployments" — und ein *geänderter* Wert mit 3 Modifiern passierte unsichtbar (#2279).
- Magnitude-Suffix (`50k`) muss Teil der Zahl sein, sonst wird "50k users" zu "50 users" und 1000× Inflation geht durch.
- Headcount-Nomen (`staff`, `personnel`, `people`, …) wurden nachgezogen, weil Ops/Healthcare-CVs genau dort aufblähen.
- Tool-of-trade-Prosa-Wörter (`built`, `using`, `with`, `production`) sind Stopwords, damit "built with Docker" nicht als "built Docker" geparst wird.

Allowlist `config/cv-facts.json`: nur für *verifizierte* Ausnahmen, die absichtlich nicht in cv.md stehen. Kein Hintertür-Erfinden.

### Schicht 4 — Story-Bank ist **kein** CV-Source für Zahlen

`AGENTS.md`: Zahlen aus `interview-prep/story-bank.md` brauchen Provenance (`source: cv.md` | `user-stated YYYY-MM-DD` | `derived-unverified` | `user-cannot-confirm`). Unbestätigte Zahlen nicht in CV-Bullets übernehmen. Details: Datei 03.

### Schicht 5 (opt-in) — HM-Audit (`modes/pdf/hm-audit.md`)

Separater Subagent, nie dasselbe Modell, das die Bullets geschrieben hat:

```text
1. **The reviewer is external.** A separate subagent — never the agent that wrote the bullets. An agent reviewing its own tailoring grades its own work and drifts toward summarising what it wrote instead of auditing it.
2. **The reviewer is research-grounded.**
```

Off by default, weil Kosten (Subagent + WebSearch). Für SaaS: nur Paid-Tier oder expliziter Button.

Der Audit **darf keine neuen Fakten empfehlen**, die außerhalb von cv.md / article-digest / profile liegen. Er bewertet *Auswahl und Framing*, nicht Wahrheit — Wahrheit ist Schicht 3.

---

## 6. JSON-Payload (das eigentliche "User-Prompt-Ergebnis")

Das Modell emittiert **kein** HTML. Es füllt dieses Schema (`modes/pdf.md`):

```json
{
  "lang": "en",
  "page_format": "a4",
  "candidate": {
    "name": "…",
    "phone": "…",
    "email": "…",
    "linkedin": { "url": "…", "display": "…" },
    "github": { "url": "…", "display": "…" },
    "portfolio": { "url": "…", "display": "…" },
    "location": "…",
    "photo": "",
    "photo_style": "rounded"
  },
  "sections": {
    "summary": "Professional Summary",
    "competencies": "Core Competencies",
    "experience": "Work Experience",
    "projects": "Projects",
    "education": "Education",
    "certifications": "Certifications",
    "awards": "Awards & Honors",
    "skills": "Skills"
  },
  "summary": "Personalized summary with JD keywords injected (honest vs cv.md).",
  "competencies": ["…", "…"],
  "experience": [
    {
      "company": "Company Name",
      "role": "Job Title",
      "location": "Remote",
      "dates": "June 2022 - Present",
      "bullets": ["Achievement bullet with JD keywords injected", "Another quantified-impact bullet"]
    }
  ],
  "projects": [
    { "name": "Project Name", "url": "https://github.com/...", "badge": "Open Source", "tech": "Python, FastAPI", "description": "What it does." }
  ],
  "education": [{ "title": "B.S. Computer Science", "org": "University Name", "year": "2022" }],
  "certifications": [{ "title": "…", "org": "…", "year": "…" }],
  "awards": [],
  "skills": [
    { "category": "Languages", "items": "C#, TypeScript" },
    { "category": "Frameworks", "items": ["ASP.NET Core", "React"] }
  ]
}
```

Leere optionale Arrays droppen die ganze Section inklusive Header. Experience weglassen nur bei Kandidaten ohne Berufshistorie — "never drop it to hide a gap."

Für DE: `lang: "de"`, `page_format: "a4"`, Section-Titles lokalisiert (`"Berufserfahrung"` etc.). Foto in DACH oft erwartet — `candidate.photo` aus dem Profil.

---

## 7. Voice / Writing — was *nicht* auf den CV darf

`_writing.md` Calibration gilt **nicht** für interne Reports, **nicht** in voller Conversational-Form für CVs:

```text
**When to apply:** Before generating any text the user will send or publish — cover letters, LinkedIn outreach, application form answers, follow-up emails, executive summaries, profile blurbs. Does NOT apply to internal evaluation reports (A–F blocks, scores, analysis).
```

CV bleibt keyword-dicht und formal. Cover Letter darf Voice-DNA Tier 2.

---

## 8. Canva-Pfad (optional, teuer, leicht zu brechen)

Wenn `cv.canva_resume_design_id` gesetzt ist. Zusätzliche harte Regel (wörtlich):

```text
**IMPORTANT — Character budget rule:** Each replacement text MUST be approximately the same length as the original text it replaces (within ±15% character count). If tailored content is longer, condense it. The Canva design has fixed-size text boxes — longer text causes overlapping with adjacent elements.
```

Dieselbe Content-Generation wie HTML (Steps 1–11), plus Layout-Reflow. Für PrivatePrep Web-SaaS: **nicht** nachbauen, bis HTML/PDF steht. Character-Budget ist ein Prompt-Muster, das nur bei Fixed-Layout-Templates nötig ist.

---

## 9. Modell-spezifische Hinweise

| Komponente | Claude Sonnet/Opus (Agent) | Llama 70B / Economy | Deterministisch (kein LLM) |
|------------|----------------------------|---------------------|----------------------------|
| Keyword-Extraktion 15–20 | zuverlässig | oft Generic ("teamwork", "communication") | regex/skill-gap besser als schwaches LLM |
| Reformulation ohne Erfindung | meist ok + Fact-Gate fängt Reste | Fact-Gate **block**t häufig; ohne Gate unbrauchbar | — |
| JSON-Payload-Schema | hält sich | bricht an optionalen Keys / nested objects | JSON-Schema-validieren, retry |
| Voice-DNA Banned List | folgt, wenn im Prompt | ignoriert lange Listen | Post-Filter auf Banned Tokens |
| HM-Audit-Subagent | designed for Claude-class | nicht starten | — |
| `jd-skill-gap` / `verify-cv-facts` | immer davor/danach | **umso wichtiger** | Kern eures Backends |

Headless-Eval komprimiert laut `lib/context-budget.mjs` bewusst **Sources of Truth und Writing-Rules als P2 weg**, wenn das Context-Window eng ist. Für Tailoring dürft ihr das **nicht** tun — sonst halluziniert das Modell genau dann, wenn der Prompt "Platz sparen" musste. Scoring-Pfad ≠ Generation-Pfad.

---

## 10. Empfohlene SaaS-Zerlegung (Prompt vs. Code)

1. **Code:** Skill-Gap, Fact-Gate, Template-Render, ATS-Normalisierung (Em-Dash → ASCII macht `generate-pdf.mjs` automatisch).
2. **LLM-Call 1 (klein):** 15–20 Keywords + Competency-Grid-Kandidaten, constrained auf `existing ∪ supportedByResume`.
3. **LLM-Call 2:** Summary + Bullet-Reorder/Rewrite, Input = nur die cv.md-Abschnitte, die ihr umschreiben wollt, plus Keyword-Liste plus Exit-Narrative aus dem Profil. Nicht die ganze oferta.md.
4. **Nie** den Evaluation-Prompt und den Tailoring-Prompt in einem Call mischen — Evaluation darf Gaps *beschreiben*; Tailoring darf Gaps nicht *schließen*.
