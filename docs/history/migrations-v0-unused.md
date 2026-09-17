> Diese Datei dokumentiert, welche SQL-Migrationen unter `PrivatePrep/Migrations/` nur
> Tabellen für V0/V2-Features anlegen (Chat, RAG, Job-Application-Pipeline, CV Studio).
> Diese Migrationen werden aus Rollback-Sicherheit nicht gelöscht, auch wenn der
> zugehörige C#-Code im V1-Refactor entfernt wurde (siehe Commit 470a1c7 und den
> Dead-Code-Audit auf v1-focus, September 2026).

# Migrations-Übersicht v1-focus

Eigener SQL-Migrations-Runner (`PrivatePrepMigrationRunner`), keine EF-Core-Code-Migrations.

## V1-relevant (aktiv genutzte Tabellen)

| Migration | Tabelle(n) | Genutzt von |
|---|---|---|
| 001_initial_app_users.sql | app_users | ClerkAuthService, AppUserContext |
| 004_career_profiles.sql | career_profiles | CareerProfilePostgresService |
| 007_token_usage.sql | token_usage_* | TokenTrackingPostgresService |
| 008_user_usage_plan.sql | user_usage_daily, user_plan | UsagePostgresService |
| 013_app_users_plan_and_stripe.sql | app_users (Plan/Stripe-Spalten) | UsagePostgresService, StripeService |

## Nur V0/V2-Feature-Tabellen (kein aktiver C#-Code mehr)

| Migration | Tabelle(n) | Ursprüngliches Feature |
|---|---|---|
| 002_chat_notes.sql | chat_notes | Chat (V0, entfernt in 470a1c7) |
| 003_job_applications.sql | job_applications | Job-Application-Pipeline (V0) |
| 005_chat_sessions.sql | chat_sessions, chat_transcripts | Chat (V0) |
| 006_learning_memory.sql | learning_memories | Chat-Insights/Learning Memory (V0) |
| 009_cv_pdf_exports.sql | cv_pdf_exports | CV Studio (V0) |
| 010_cv_pdf_exports_target_fields.sql | cv_pdf_exports (Zusatzspalten) | CV Studio (V0) |
| 011_cv_resume_categories.sql | cv_user_categories, cv_resume_category_assignments | CV Studio (V0) |
| 014_usage_records.sql | usage_records | RAG/Chat-Turn-Tracking (V0, `UsageTrackingService`) |
| 015_usage_records_rag_columns.sql | usage_records (RAG-Spalten) | RAG-Tracking (V0) |

`012_performance_indexes.sql` ist gemischt: enthält Indizes sowohl für V1-Tabellen
(career_profiles) als auch für V0/V2-Tabellen (job_applications, cv_user_categories)
sowie für `resumes`/`resume_versions`, die in keinem aktuellen `DbSet<>` mehr auftauchen.

Wenn die V2-Features aus dem `v2-features`-Branch wieder integriert werden, sind die
zugehörigen Tabellen bereits vorhanden, die Migrationen müssen nicht neu geschrieben werden.
