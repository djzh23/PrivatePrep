-- Inbox: job postings collected by the browser extension ("job vacuum") or entered manually,
-- pending review and analysis by the user. Requires app_users (001).
-- This is the one deliberate exception to "no full text storage": the analyze endpoints
-- themselves still never persist a posting or a report (spec 001). Text here lives until the
-- user deletes the entry or runs it through analyze.

CREATE TABLE IF NOT EXISTS inbox_jobs (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    clerk_user_id TEXT NOT NULL
        REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    title VARCHAR(500) NOT NULL,
    company VARCHAR(300) NOT NULL,
    location VARCHAR(300) NULL,
    source_url VARCHAR(2000) NOT NULL,
    source_kind SMALLINT NOT NULL DEFAULT 0,
    raw_text VARCHAR(50000) NOT NULL,
    status SMALLINT NOT NULL DEFAULT 0,
    extracted_at TIMESTAMPTZ NOT NULL,
    analyzed_at TIMESTAMPTZ NULL,
    analysis_report_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT pk_inbox_jobs PRIMARY KEY (id)
);

-- Upsert key: the extension re-POSTs the same posting as the user revisits/re-scans a page.
CREATE UNIQUE INDEX IF NOT EXISTS uq_inbox_jobs_user_source_url
    ON inbox_jobs (clerk_user_id, source_url);

-- Inbox list filtered by status (e.g. "New" only).
CREATE INDEX IF NOT EXISTS idx_inbox_jobs_user_status
    ON inbox_jobs (clerk_user_id, status);

-- Inbox list sorted newest first.
CREATE INDEX IF NOT EXISTS idx_inbox_jobs_user_extracted_at
    ON inbox_jobs (clerk_user_id, extracted_at DESC);

COMMENT ON TABLE inbox_jobs IS 'Job postings collected by the browser extension or entered manually, held as raw text until the user deletes or analyzes them.';
COMMENT ON COLUMN inbox_jobs.source_kind IS '0=Generic, 1=LinkedIn, 2=StepStone, 3=Manual (PrivatePrep.Models.InboxSourceKind).';
COMMENT ON COLUMN inbox_jobs.status IS '0=New, 1=Analyzed, 2=Archived (PrivatePrep.Models.InboxJobStatus).';
COMMENT ON COLUMN inbox_jobs.analysis_report_id IS 'Optional correlation id for a future analysis-report table. No such table exists yet, so no foreign key.';
