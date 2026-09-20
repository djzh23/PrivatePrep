# Technical debt (V1)

Items noticed during the V1 cut. Do not expand these in the V1 loop.

- Stripe webhook idempotency uses `IMemoryCache` (48h). A process restart can replay an event. Move to Postgres before serious live traffic.
- `GET /api/agent/usage` still lives on `AgentController` because there is no `TrackingController`.
- Groq prompt caching (`cache_control`) is not wired; system prompt is resent every analyze call.
- `CareerProfile.Story` is JSON-only. There is no dedicated Story endpoint yet; clients send it via the existing profile payload.
- App code no longer persists CV raw text (hash + length only). Migration `016_cv_store_hash_drop_raw_text.sql` runs on Render boot via PrivatePrepMigrationRunner. Follow with `VACUUM FULL` on `career_profiles` if old TOAST leftovers remain.
- IndexedDB encryption for the browser CV cache is a later sprint, not V1-blocking.

## Noticed during the Phase 4 live-deploy check (2026-09-17), not part of this cleanup session

- Frontend UI is not compact enough for a fast-use SaaS tool on mobile: boxes are small and hard to read, spacing between sections is too generous. Needs a mobile-first pass in the SmartAssist-react repo.
- Groq changed its free-tier model lineup; the currently configured model is not reliably reachable anymore. Need a replacement model (or provider) to keep testing the existing prompt, retry-loop, and scoring logic without rewriting them.
- Stripe pricing/plan setup needs review before launch (price IDs, plan tiers).
