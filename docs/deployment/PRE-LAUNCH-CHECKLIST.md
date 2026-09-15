# Pre-Launch Checklist

## Rechtlich
- [ ] Impressum ausgefüllt und live
- [ ] Datenschutzerklärung von e-recht24.de generiert und live
- [ ] DPAs / AVVs bei Groq, Clerk, Stripe, Render, Supabase akzeptiert
- [ ] Kein Google-Gemini-DPA nötig, solange V1 nur Groq nutzt — bestätigt
- [ ] Cookie-Banner nicht nötig (keine Analytics in V1) — bestätigt

## Technisch
- [ ] Render von Free auf Starter ($7/Monat) hochgestuft ODER auf Hetzner CX22 migriert
- [ ] Datenbank-Backups aktiv (Supabase daily)
- [ ] Sentry oder ähnliches für Error-Tracking eingerichtet
- [ ] Stripe im Live-Modus (nicht Test-Mode)
- [ ] Groq API-Key mit Rate-Limit-Budget (nicht über 30 req/min)

## Content
- [ ] Landing Page final Deutsch reviewed
- [ ] Demo-Report technisch geprüft (statisch, keine LLM-Calls)
- [ ] Ein guter Beispiel-CV (fiktiv) für Demo
- [ ] Eine gute Beispiel-JD (fiktiv) für Demo

## Test-Bewerber-Runde
- [ ] 5 echte Bewerber (Freundeskreis) haben das getestet
- [ ] Feedback dokumentiert
- [ ] Kritische Bugs gefixt
