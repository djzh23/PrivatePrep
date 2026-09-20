# Release-Checkliste

Vor jedem Push, der Live erreichen kann. Live deployt aktuell von `v1-focus`
(Render Backend, Vercel Frontend). GitHub Actions Deploy-Jobs feuern nur auf
`main`.

Offene Go-Live-Blocker stehen in
[PRE-LAUNCH-CHECKLIST.md](./PRE-LAUNCH-CHECKLIST.md).

## Vor dem Push

- [ ] Zielbranch ist der, den Zouhair genannt hat (`v1-focus` für Live; nie
      `main`, außer ausdrücklich gewünscht)
- [ ] Impressum: Straße und PLZ sind Platzhalter, außer Zouhair hat sie selbst
      gefüllt und mit `docs: fill in address for beta launch` committed
- [ ] Datenschutz beschreibt den echten Ablauf (nur Groq, CV-Hash plus Länge
      auf dem Server, Report im Browser, AVVs nicht als unterschrieben
      behaupten)
- [ ] Keine Claims wie "100% anonym" oder "DSGVO-vollständig"
- [ ] `BETA_MODE` / `VITE_BETA_MODE` bleiben aus, außer Zouhair will das
      harte Gate an
- [ ] Kein erfundenes V1-Produkt (kein Interview-Trainer, kein
      Sprachtraining, kein Verlauf / Speichern / Export)
- [ ] Backend-Tests und Frontend Lint/Build lokal grün, wenn Code geändert
      wurde

## Nach Deploy auf v1-focus (Render / Vercel)

- [ ] Render hat anstehende SQL-Migrationen angewendet (aktuell
      `016_cv_store_hash_drop_raw_text.sql` beim Boot)
- [ ] Smoke: Landing, Impressum, Datenschutz, Analyse, Profil, Preise
- [ ] Banner "geschlossene Beta" auf öffentlichen Seiten sichtbar
- [ ] Stripe bleibt Test-Modus, bis Zouhair umstellt
- [ ] `VITE_PIRSCH_CODE` nur setzen, wenn Analytics laufen soll

## Nicht Teil eines Routine-Releases

- Push auf `main` ohne ausdrückliche Anweisung
- History-Rewrite, um Secrets zu verstecken (Keys rotieren statt filter-branch)
- Straßenadresse erfinden
- DNS oder Staging-Environment ohne ausdrückliche Anweisung anlegen
- AVVs als unterschrieben behaupten
