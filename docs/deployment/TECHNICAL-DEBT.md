# Technical debt (V1)

Items noticed during the V1 cut. Do not expand these in the V1 loop.

- Stripe webhook idempotency uses `IMemoryCache` (48h). A process restart can replay an event. Move to Postgres before serious live traffic.
- `GET /api/agent/usage` still lives on `AgentController` because there is no `TrackingController`.
- Groq prompt caching (`cache_control`) is not wired; system prompt is resent every analyze call.
- `CareerProfile.Story` is JSON-only. There is no dedicated Story endpoint yet; clients send it via the existing profile payload.
- Full CV text is in `career_profiles.cv_raw_text`; `GetProfile` still omits that column and analyze loads it separately.
