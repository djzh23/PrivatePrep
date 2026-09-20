-- Drop raw CV text. Keep a SHA-256 hex fingerprint and the character count.
-- CHAR(64) pads in Postgres; VARCHAR(64) avoids false hash mismatches.
-- DROP COLUMN removes the column. TOAST leftovers need VACUUM FULL on the
-- production database after this migration, not inside this transaction.

ALTER TABLE career_profiles
    ADD COLUMN IF NOT EXISTS cv_content_hash VARCHAR(64),
    ADD COLUMN IF NOT EXISTS cv_content_length INTEGER;

UPDATE career_profiles
SET profile_json = profile_json - 'cvRawText'
WHERE jsonb_typeof(profile_json) = 'object'
  AND profile_json ? 'cvRawText';

ALTER TABLE career_profiles
    DROP COLUMN IF EXISTS cv_raw_text;

COMMENT ON COLUMN career_profiles.cv_content_hash IS 'SHA-256 hex of last uploaded CV text. Raw CV text is not stored.';
COMMENT ON COLUMN career_profiles.cv_content_length IS 'Character count of last uploaded CV text.';
