-- Persisted results of the analyze pipeline (PrivatePrep.Services.Agent.AnalyzeReport).
-- This is the report-half exception to spec 001's original "nothing is stored" principle
-- (docs/specs/001-analyse-v2.md, 2026-09-24 addendum). The CV raw text itself is still never
-- stored, only its hash (cv_hash below).
-- Requires app_users (001) and inbox_jobs (017).

CREATE TABLE IF NOT EXISTS analysis_reports (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    clerk_user_id TEXT NOT NULL
        REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    inbox_job_id UUID NULL
        REFERENCES inbox_jobs (id) ON DELETE SET NULL,
    report_json VARCHAR(100000) NOT NULL,
    cv_hash CHAR(64) NOT NULL,
    jd_hash CHAR(64) NOT NULL,
    cv_length INT NOT NULL,
    jd_length INT NOT NULL,
    llm_model VARCHAR(200) NOT NULL,
    match_score NUMERIC(3, 1) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT pk_analysis_reports PRIMARY KEY (id)
);

-- A job has at most one report. Rows with inbox_job_id = NULL (ad-hoc analyses not tied to an
-- inbox job) are exempt from this by ordinary SQL NULL semantics: Postgres never treats two NULLs
-- as equal, so any number of ad-hoc reports can coexist per user.
CREATE UNIQUE INDEX IF NOT EXISTS uq_analysis_reports_user_inbox_job
    ON analysis_reports (clerk_user_id, inbox_job_id);

-- Per user report listing (GET /api/reports), newest first. Also serves plain per user lookups,
-- since clerk_user_id is the leading column.
CREATE INDEX IF NOT EXISTS idx_analysis_reports_user_created_at
    ON analysis_reports (clerk_user_id, created_at DESC);

-- inbox_jobs.analysis_report_id was reserved in migration 017 with no foreign key because this
-- table did not exist yet. It does now: wire it up.
ALTER TABLE inbox_jobs
    ADD CONSTRAINT fk_inbox_jobs_analysis_report
        FOREIGN KEY (analysis_report_id) REFERENCES analysis_reports (id) ON DELETE SET NULL;

COMMENT ON TABLE analysis_reports IS 'Persisted results of the analyze pipeline (AnalyzeReport), one row per analysis. Optionally linked to the inbox_jobs entry it was run from.';
COMMENT ON COLUMN analysis_reports.report_json IS 'Full serialized AnalyzeReport JSON (PrivatePrep.Services.Agent.AnalyzeReport), camelCase, exactly as the API returns it.';
COMMENT ON COLUMN analysis_reports.cv_hash IS 'SHA-256 hex of the CV text used for this analysis. The CV text itself is never stored.';
COMMENT ON COLUMN analysis_reports.jd_hash IS 'SHA-256 hex of the job description text used for this analysis.';
COMMENT ON COLUMN analysis_reports.match_score IS 'Extracted AnalyzeReport.GlobalScore, duplicated here for fast list rendering without deserializing report_json.';
