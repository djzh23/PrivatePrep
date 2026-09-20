# Pre-Launch Checklist

Updated 20 September 2026 after the privacy/UI work (Phases 1 to 6). Unchecked items are still blocking a honest go-live.

## Datenschutz
- [ ] AVVs/DPAs accepted and filed for Groq, Clerk, Stripe, Render, Vercel, Supabase, and Pirsch if analytics is on
- [ ] No Google Gemini DPA needed while V1 uses only Groq
- [ ] Datenschutzerklärung reviewed by a lawyer OR finished on an e-recht24.de base (current text is a draft of the real data flow)
- [ ] Impressum fully filled (name, address, email; currently TODO placeholders)
- [ ] Confirm migration `016_cv_store_hash_drop_raw_text.sql` ran on Render boot (hash columns present, `cv_raw_text` gone). No local pg_dump was available before this deploy.
- [ ] Postgres `VACUUM FULL` on `career_profiles` after that migration if the production DB still had old CV text
- [ ] `VITE_PIRSCH_CODE` set only when Pirsch should run

## UI
- [ ] Analyse page tested on mobile (375px), tablet (768px), desktop (1280px+) by Zouhair
- [ ] Loading state visible while Groq runs (time-based steps, no SSE)
- [ ] Copy button on bullet rewrites works in a real browser
- [ ] Dark app shell is the only theme; no extra dark: variants to maintain
- [ ] Nav is Analyse / Profil / Preise only (no Verlauf, Speichern, Export, Bookmark)

## Rechtlich (legacy list, still open)
- [ ] Impressum ausgefüllt und live
- [ ] Datenschutzerklärung von e-recht24.de generiert und live, oder Kanzlei-Review
- [ ] DPAs / AVVs bei Groq, Clerk, Stripe, Render, Vercel, Supabase akzeptiert
- [ ] Kein Google-Gemini-DPA nötig, solange V1 nur Groq nutzt
- [ ] Cookie-Banner: Pirsch ist cookielos und nur aktiv, wenn VITE_PIRSCH_CODE gesetzt ist. Banner nur nötig, wenn zusätzliche Tracker mit Cookies dazukommen.

## Technisch
- [ ] Render von Free auf Starter ($7/Monat) hochgestuft ODER auf Hetzner CX22 migriert
- [ ] Datenbank-Backups aktiv (Supabase daily)
- [ ] Sentry oder ähnliches für Error-Tracking eingerichtet
- [ ] Stripe im Live-Modus (nicht Test-Mode)
- [ ] Groq API-Key mit Rate-Limit-Budget (nicht über 30 req/min)
- [ ] Code no longer persists CV raw text; 016 runs on the next Render start and drops `cv_raw_text`

## Content
- [ ] Landing Page final Deutsch reviewed
- [ ] Demo-Report technisch geprüft (statisch, keine LLM-Calls)
- [ ] Ein guter Beispiel-CV (fiktiv) für Demo
- [ ] Eine gute Beispiel-JD (fiktiv) für Demo

## Test-Bewerber-Runde
- [ ] 5 echte Bewerber (Freundeskreis) haben das getestet
- [ ] Feedback dokumentiert
- [ ] Kritische Bugs gefixt

## Communication (checked in Phase 6)
- [x] No "100% anonym" / "vollständig sicher" claims on landing, upload, Datenschutz, Preise-FAQ
- [x] Same story everywhere: Groq USA once, hash+length on server, report in this browser, PII filter is a safety net
- [x] Gemini is not listed as a V1 processor
