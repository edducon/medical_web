CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS app_users (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  role TEXT NOT NULL CHECK (role IN ('administrator', 'operator')),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS audit_events (
  id BIGSERIAL PRIMARY KEY,
  actor_id UUID REFERENCES app_users(id),
  action TEXT NOT NULL,
  entity_type TEXT NOT NULL,
  entity_id UUID,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  metadata JSONB NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS audit_events_created_at_idx ON audit_events (created_at DESC);

CREATE TABLE IF NOT EXISTS scanners (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name TEXT NOT NULL UNIQUE,
  model TEXT NOT NULL DEFAULT 'ПЭТ/КТ',
  is_active BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS shifts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  shift_date DATE NOT NULL,
  isotope_code TEXT NOT NULL CHECK (isotope_code IN ('F-18', 'Ga-68')),
  source_activity_mbq NUMERIC(12, 2) NOT NULL CHECK (source_activity_mbq >= 0),
  source_measured_at TIMESTAMPTZ NOT NULL,
  created_by UUID NOT NULL REFERENCES app_users(id),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE (shift_date, isotope_code)
);

CREATE TABLE IF NOT EXISTS appointments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  shift_id UUID NOT NULL REFERENCES shifts(id) ON DELETE CASCADE,
  scanner_id UUID NOT NULL REFERENCES scanners(id),
  patient_number_ciphertext TEXT NOT NULL,
  weight_ciphertext TEXT NOT NULL,
  isotope_code TEXT NOT NULL CHECK (isotope_code IN ('F-18', 'Ga-68')),
  protocol_name TEXT NOT NULL,
  injection_at TIMESTAMPTZ NOT NULL,
  duration_minutes SMALLINT NOT NULL CHECK (duration_minutes BETWEEN 1 AND 300),
  confirmed_at TIMESTAMPTZ,
  confirmed_by UUID REFERENCES app_users(id),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS shifts_date_idx ON shifts (shift_date DESC);
CREATE INDEX IF NOT EXISTS appointments_shift_time_idx ON appointments (shift_id, injection_at);

CREATE TABLE IF NOT EXISTS isotope_settings (
  isotope_code TEXT PRIMARY KEY CHECK (isotope_code IN ('F-18', 'Ga-68')),
  half_life_minutes NUMERIC(8,2) NOT NULL CHECK (half_life_minutes > 0),
  dose_coefficient_mbq_per_kg NUMERIC(8,3) NOT NULL CHECK (dose_coefficient_mbq_per_kg > 0),
  default_source_activity_mbq NUMERIC(12,2) NOT NULL CHECK (default_source_activity_mbq >= 0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS protocols (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  isotope_code TEXT NOT NULL REFERENCES isotope_settings(isotope_code),
  name TEXT NOT NULL,
  duration_minutes SMALLINT NOT NULL CHECK (duration_minutes BETWEEN 1 AND 300),
  is_active BOOLEAN NOT NULL DEFAULT true,
  UNIQUE (isotope_code, name)
);

CREATE INDEX IF NOT EXISTS protocols_isotope_active_idx ON protocols (isotope_code) WHERE is_active = true;

INSERT INTO isotope_settings (isotope_code, half_life_minutes, dose_coefficient_mbq_per_kg, default_source_activity_mbq) VALUES
  ('F-18', 109.77, 3.7, 37000),
  ('Ga-68', 68, 2.2, 10000)
ON CONFLICT (isotope_code) DO NOTHING;

INSERT INTO protocols (isotope_code, name, duration_minutes) VALUES
  ('F-18', 'Онко-ПЭТ/КТ (FDG)', 90),
  ('Ga-68', 'ПСМА-ПЭТ/КТ', 75),
  ('Ga-68', 'НЭО-ПЭТ/КТ (DOTA TATE)', 80)
ON CONFLICT (isotope_code, name) DO NOTHING;

INSERT INTO scanners (name, model) VALUES
  ('GE Discovery IQ', 'ПЭТ/КТ'),
  ('Siemens Horizon', 'ПЭТ/КТ'),
  ('ПЭТ/КТ 3', 'ПЭТ/КТ')
ON CONFLICT (name) DO NOTHING;
