CREATE TABLE IF NOT EXISTS patients (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  number_ciphertext TEXT NOT NULL,
  number_fingerprint TEXT NOT NULL UNIQUE,
  last_weight_ciphertext TEXT NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS patients_updated_at_idx ON patients (updated_at DESC);
