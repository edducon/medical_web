ALTER TABLE shifts ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'closed'));
ALTER TABLE shifts ADD COLUMN IF NOT EXISTS closed_at TIMESTAMPTZ;
ALTER TABLE shifts ADD COLUMN IF NOT EXISTS closed_by UUID REFERENCES app_users(id);
CREATE INDEX IF NOT EXISTS shifts_status_date_idx ON shifts (status, shift_date DESC);
