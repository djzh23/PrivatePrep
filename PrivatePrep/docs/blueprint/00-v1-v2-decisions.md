# 00 — V1 vs V2 Entscheidungen für PrivatePrep

> **Kontext:** Diese Datei basiert auf den vier Blueprint-Dateien (`01-scoring-rubric.md` bis `04-lessons-and-antipatterns.md`), die aus career-ops (MIT License, Copyright Santiago Fernández de Valderrama) extrahiert wurden, sowie auf drei bis vier Wochen produktiver Eigennutzung von career-ops durch mich.
>
> **Attribution:** Prompt- und Scoring-Muster in PrivatePrep sind inspiriert von career-ops. Siehe `NOTICE.md` im Repo-Root.

---

## Leitprinzip für V1

Der Value von PrivatePrep ist **nicht** ein Score. Der Value ist der Personalisierungs-Loop:

```
Ich beschreibe mich (Profil + Story) + lade CV hoch
        ↓
Ich zeige eine Stelle
        ↓
System sagt: "Bullet 3 aus deinem Digi.bo-Job passt zu Anforderung X.
           Formuliere sie so um, dass 'React Hooks' vorne steht."
        ↓
Ich kopiere die neuen Bullets in mein Word-Dokument.
```

**Alles was diesen Loop nicht direkt bedient, ist V2 oder später.**

Ziel-User für V1: deutsche Junior/Mid-Level Bewerber im Tech-Bereich (Backend, Frontend, Fullstack). Deutsche UI. Deutscher Beispiel-CV. Deutsche Beispiel-Stelle. Keine englische Version in V1.

---

## V1 — bleibt in `main`, geht live

### Backend (ASP.NET Core 9)

| Component | Behalten / Neu | Notizen |
|-----------|----------------|---------|
| `AuthController` (Clerk JWT) | **Behalten** | Anonymous Tier + Free Tier |
| `ProfileController` | **Behalten** | Story-Feld hinzufügen (Textarea, freier Text) |
| `CvUploadController` | **Behalten** | PdfPig Parsing, cv.md immutable speichern |
| `AgentController` (nur `jobanalyzer` mode) | **Behalten, stark vereinfacht** | Nur Full-Eval-Light Prompt, kein Multi-Mode-Routing |
| `PaymentsController` (Stripe) | **Behalten** | Ein Plan: Free (3 Analysen) vs Premium 4,99€/Monat |
| `TrackingController` (Daily Limits) | **Behalten** | 3 Analysen/Tag Free, unbegrenzt Premium |
| **NEU:** `SkillGapService` (zero-LLM) | **Neu bauen** | C#-Port von `jd-skill-gap.mjs` — deterministische Klassifizierung |
| **NEU:** `FactGateService` (zero-LLM) | **Neu bauen** | C#-Port von `verify-cv-facts.mjs` — blockt erfundene Zahlen/Skills |

### Frontend (React)

| Component | Behalten / Neu | Notizen |
|-----------|----------------|---------|
| Landing Page (DE) | **Neu bauen** | Ein Value Prop, ein CTA, Demo mit Beispieldaten |
| Onboarding-Flow | **Neu bauen** | 3 Screens: Profil / Story / CV-Upload |
| Job Input Screen | **Behalten, vereinfacht** | URL + Text-Paste, nichts anderes |
| Report Screen | **Umbauen** | 3 Sektionen: Match-Score, Skill-Gap-Tabelle, Bullet-Rewrites |

### Prompt-Struktur (aus Blueprint 01 + 02)

Ein **einziger** Full-Eval-Light Prompt (kein Triage, kein Generate separat). Enthält:

- Score-Dimensionen: CV-Match, Zielrolle-Alignment, Culture-Signale, Red flags
- Skill-Gap-Referenz (aus deterministischem `SkillGapService`)
- Bullet-Rewrite-Anleitung mit dem Kernsatz: *"Keywords get reformulated, never fabricated"*
- Anti-AI-Slop Tier 1 (Banned Words aus `voice-dna.md`)
- Untrusted-JD-Marker (Prompt-Injection-Schutz)

### Zero-LLM-Gates (Pflicht)

1. **VOR LLM-Call:** `SkillGapService.Classify(cv, jd)` → `existing / supportedByResume / gap` Tabelle. Der LLM sieht diese Tabelle als Input und darf `gap`-Items nie als vorhandene Skills darstellen.
2. **NACH LLM-Call:** `FactGateService.Verify(output, cv, story)` → blockt Output, wenn Zahlen/Metriken/Skills auftauchen, die weder im CV noch in der Story stehen.

### Deutsche Markt-Spezifika (aus Blueprint 04 § 3.10)

- A4 Format (nicht Letter)
- "m/w/d" statt "M/F/X"
- Kununu-Referenzen wo passend statt Levels.fyi
- Anschreiben-Kultur berücksichtigen (auch wenn V1 noch keine Anschreiben erzeugt)
- "Junior" nicht als notwendige Bedingung erzwingen (DE-KMU schreiben oft kein "Junior")

---

## V2 — geht in Branch `v2-features`, nicht live

Alles hier ist **committed und funktionsfähig**, aber nicht auf der Live-Seite verfügbar. Feature-Flag oder auskommentierte Routes.

| Feature | Warum V2 | Blueprint-Referenz |
|---------|----------|--------------------|
| CV Studio (Editor + Snapshot-Versioning) | Zu groß für V1; separate Sprint | 02 § 3.7 |
| PDF-Export (QuestPDF + HTML-Template) | Fact-Gate + Layout-Fine-Tuning ist eigene Sub-Sprint | 02 § 2.1 Step 17-19 |
| Cover Letter Generator mit 4 Angles | Braucht HITL-Form-Gates, eigenes UI-Konzept | 03 (ganz) |
| STAR Story Bank | Trust-Tier-Enum + Provenance-Check = viel Backend | 03 + 04 § 3.5 |
| Interview Prep Mode | Neuer Prompt-Pfad, neues UI | Blueprint 01 Block F |
| Language Learning Mode | Off-Topic für V1 | — |
| Programming Help Mode | Off-Topic, konkurriert mit ChatGPT | — |
| General Career Coach Mode | Zu vage, konkurriert mit ChatGPT | — |
| Block G (Ghost-Job-Detection) | 5-15 Signale, teuer, DE-Junior-Markt selten Betrug | 01 Block G + 04 § 3.2 |
| Company Research Mode (`deep`) | Kostet 3+ WebSearch pro Call | Blueprint 01 § 0.2 |
| Contact Discovery (`contacto`) | Legal- und ToS-Fragen bei LinkedIn | 04 § 3 |
| Portal Scanner (Playwright) | Server-side Playwright = teuer + ToS-Risiko | 03 + 04 § 3 |
| Batch Processing | Braucht Multi-User-Queue, DevOps-Aufwand | 01 § 0.1 |
| Retrieval-Augmented Memory (pgvector) | Ohne wiederkehrende User keinen Wert | — |
| Redis Dual-Storage | Over-Engineering für V1-Traffic | — |
| Azure TTS | Off-Topic | — |
| Triage-Tier (Small Model für Vorfilter) | Nur relevant bei Portal-Scanning | 04 § 3.1 |

---

## NIE — auch nicht in V2

Diese Features sind aus dem Blueprint entweder verworfen oder für kleinen SaaS ruinös:

- **Auto-Submit von Bewerbungen** (Blueprint 04 § 2 „Eingeschränkt: Playwright-Apply") — rechtlich und operativ
- **HM-Audit auf jedem PDF** (Blueprint 04 § 2 „Eingeschränkt") — zweiter LLM-Call pro User = zu teuer
- **Nested Subagents / Deep Research** (Blueprint 01 § 0.2) — kann Millionen Tokens verbrennen
- **Auto-Tuning der Scoring-Regeln** (Blueprint 04 § 1.5) — verstecktes Rescoring baut Vertrauen ab
- **Self-Review durch dasselbe Modell** (Blueprint 04 § 1.12) — fasst zusammen statt zu streichen

---

## Prompt-Checkliste vor jedem V1-Release (aus Blueprint 04 § 5)

Kein Prompt geht live, ohne dass alle 10 Punkte greifen:

1. Source-of-Truth-Liste steht drin (CV + Story + Profil)
2. Tool-of-trade-Conflation namentlich verboten
3. Code-Gate nach dem Call (nicht nur Prompt-Bitte)
4. HITL-Gate nicht durch "just generate" umgehbar
5. Fehlende Checks als `not_evaluated`, nicht als grün
6. Research budgetiert (in V1: kein WebSearch = 0 Budget)
7. JD als untrusted markiert
8. Kein Satz, der in jedem Brief für jede Firma stehen könnte
9. Zahlen im Confirm-Dialog nicht als Ja/Nein-Lead
10. Happy Path kostet ≤ 1 Generation-Call

---

## Konkrete Backend-Änderungen (für Cursor-Session)

### Controllers, die bleiben (7 statt 15)
- AuthController
- ProfileController
- CvUploadController
- AgentController (nur eine Route: `POST /api/analyze`)
- PaymentsController
- TrackingController
- HealthController

### Controllers, die in `v2-features` Branch verschoben werden
- Chat / Sessions Controller
- CvStudio Controllers (Studio, Snapshots, Categories)
- Learning Controller
- Notes Controller
- Speech Controller
- Insights Controller
- Interview Controller
- Languages Controller
- Programming Controller

### Services, die bleiben
- Services/Agent/ (vereinfacht auf einen Pfad)
- Services/Profile/
- Services/Payments/
- Services/Tracking/
- Services/Auth/
- **Neu:** Services/SkillGap/
- **Neu:** Services/FactGate/

### Services, die in `v2-features` verschoben werden
- Services/Chat/
- Services/CvStudio/
- Services/VectorStore/
- Services/Learning/
- Services/Notes/
- Services/Speech/
- Services/Embeddings/
- Services/Background/

### Datenbank
- Redis / Upstash komplett aus V1 entfernen (Dual-Storage → Single Postgres)
- pgvector-Extension bleibt aktiv, aber ungenutzt in V1
- Snapshot-Tabellen bleiben im Schema, aber ohne UI-Zugriff

### Deployment
- Render Free Tier → Render Starter ($7/Monat) oder Hetzner Cloud CX22 (~€4/Monat)
- Groq als LLM (schnell + günstig) — bleibt
- Ein einziger Dockerfile, keine Multi-Service-Compose

---

## Definition of Done für V1

Die V1 ist fertig, wenn folgende Sätze wahr sind:

1. Ein anonymer Besucher kann auf betweenatna.com landen und in unter 15 Sekunden verstehen, was das Tool tut
2. Ein anonymer Besucher kann eine Demo-Analyse mit Beispiel-CV + Beispiel-JD in unter 30 Sekunden komplett durchspielen, ohne Login
3. Ein eingeloggter Free-User kann sein eigenes Profil + Story eingeben, seinen echten CV hochladen, eine Job-URL/JD einfügen und einen vollständigen Report bekommen
4. Der Report enthält immer: 1 Match-Score, 1 Skill-Gap-Tabelle mit existing/supported/gap, 3-5 konkrete Bullet-Rewrite-Vorschläge zum Copy-Paste
5. Der Report enthält nie: eine Skill, die nicht im CV oder in der Story steht; eine Zahl, die nicht dort steht; eine Bewertung ohne Culture-Warnung wenn Culture-Cap greift
6. Der Server antwortet auf den ersten Request nach 5 Minuten Pause in unter 3 Sekunden (kein Kaltstart-Problem)
7. Der Stripe-Checkout funktioniert für Premium 4,99€/Monat
8. Die deutsche UI ist vollständig — kein englischer Text im User-Facing-Flow
9. README.md im Repo hat: Value-Prop, Demo-Screenshot, Architektur-Diagramm, Live-Link, Installation-Steps
10. `NOTICE.md` im Repo attributiert career-ops (MIT)

Alles darüber hinaus ist V1.5 oder V2.

---

## Nächste Schritte

1. Backup-Branch erstellen: `backup/pre-v1-refactor-2026-09-15`
2. Neuer Arbeits-Branch: `v1-focus`
3. Diese Datei in `PrivatePrep/docs/blueprint/00-v1-v2-decisions.md` committen
4. Die vier Blueprint-Dateien (01-04) in `PrivatePrep/docs/blueprint/` committen
5. `NOTICE.md` im Repo-Root erstellen mit career-ops Attribution
6. Cursor-Session öffnen und mit dem Bootstrapping-Prompt starten (folgt separat)
