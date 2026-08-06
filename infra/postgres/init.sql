CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS app_users (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(), email TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL,
  role TEXT NOT NULL CHECK (role IN ('administrator', 'operator')), created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS audit_events (
  id BIGSERIAL PRIMARY KEY, actor_id UUID REFERENCES app_users(id), action TEXT NOT NULL, entity_type TEXT NOT NULL,
  entity_id UUID, created_at TIMESTAMPTZ NOT NULL DEFAULT now(), metadata JSONB NOT NULL DEFAULT '{}'::jsonb
);
CREATE INDEX IF NOT EXISTS audit_events_created_at_idx ON audit_events (created_at DESC);

CREATE TABLE IF NOT EXISTS scanners (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(), name TEXT NOT NULL UNIQUE, model TEXT NOT NULL DEFAULT 'PET/CT',
  serial_number TEXT, manufacture_year SMALLINT CHECK (manufacture_year BETWEEN 1900 AND 2100),
  is_active BOOLEAN NOT NULL DEFAULT true, created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS scanner_profiles (
  scanner_id UUID NOT NULL REFERENCES scanners(id) ON DELETE CASCADE,
  patient_category TEXT NOT NULL CHECK (patient_category IN ('S', 'M', 'F')),
  preparation_minutes SMALLINT NOT NULL CHECK (preparation_minutes BETWEEN 0 AND 180),
  scan_minutes SMALLINT NOT NULL CHECK (scan_minutes BETWEEN 1 AND 300), PRIMARY KEY (scanner_id, patient_category)
);

CREATE TABLE IF NOT EXISTS isotope_settings (
  isotope_code TEXT PRIMARY KEY CHECK (isotope_code IN ('F-18', 'Ga-68')),
  half_life_minutes NUMERIC(8,2) NOT NULL CHECK (half_life_minutes > 0),
  dose_coefficient_mbq_per_kg NUMERIC(8,3) NOT NULL CHECK (dose_coefficient_mbq_per_kg > 0),
  default_source_activity_mbq NUMERIC(12,2) NOT NULL CHECK (default_source_activity_mbq >= 0), updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS protocols (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(), isotope_code TEXT NOT NULL REFERENCES isotope_settings(isotope_code), name TEXT NOT NULL,
  duration_minutes SMALLINT NOT NULL CHECK (duration_minutes BETWEEN 1 AND 300),
  uptake_minutes SMALLINT NOT NULL DEFAULT 0 CHECK (uptake_minutes BETWEEN 0 AND 360),
  maximum_uptake_minutes SMALLINT CHECK (maximum_uptake_minutes BETWEEN 0 AND 360), is_active BOOLEAN NOT NULL DEFAULT true,
  UNIQUE (isotope_code, name)
);
CREATE INDEX IF NOT EXISTS protocols_isotope_active_idx ON protocols (isotope_code) WHERE is_active = true;

CREATE TABLE IF NOT EXISTS shifts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(), shift_date DATE NOT NULL,
  isotope_code TEXT NOT NULL REFERENCES isotope_settings(isotope_code), source_activity_mbq NUMERIC(12,2) NOT NULL CHECK (source_activity_mbq >= 0),
  source_measured_at TIMESTAMPTZ NOT NULL, half_life_minutes NUMERIC(8,2) NOT NULL CHECK (half_life_minutes > 0),
  dose_coefficient_mbq_per_kg NUMERIC(8,3) NOT NULL CHECK (dose_coefficient_mbq_per_kg > 0),
  created_by UUID NOT NULL REFERENCES app_users(id), created_at TIMESTAMPTZ NOT NULL DEFAULT now(), UNIQUE (shift_date, isotope_code)
);
CREATE TABLE IF NOT EXISTS appointments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(), shift_id UUID NOT NULL REFERENCES shifts(id) ON DELETE CASCADE,
  scanner_id UUID NOT NULL REFERENCES scanners(id), patient_number_ciphertext TEXT NOT NULL, weight_ciphertext TEXT NOT NULL,
  isotope_code TEXT NOT NULL REFERENCES isotope_settings(isotope_code), protocol_name TEXT NOT NULL, injection_at TIMESTAMPTZ NOT NULL,
  scan_start_at TIMESTAMPTZ NOT NULL, duration_minutes SMALLINT NOT NULL CHECK (duration_minutes BETWEEN 1 AND 300),
  uptake_minutes SMALLINT NOT NULL CHECK (uptake_minutes BETWEEN 0 AND 360), patient_category TEXT NOT NULL CHECK (patient_category IN ('S', 'M', 'F')),
  confirmed_at TIMESTAMPTZ, confirmed_by UUID REFERENCES app_users(id), created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS shifts_date_idx ON shifts (shift_date DESC);
CREATE INDEX IF NOT EXISTS appointments_shift_time_idx ON appointments (shift_id, injection_at);
CREATE INDEX IF NOT EXISTS appointments_scanner_scan_time_idx ON appointments (scanner_id, scan_start_at);

INSERT INTO isotope_settings (isotope_code, half_life_minutes, dose_coefficient_mbq_per_kg, default_source_activity_mbq) VALUES
  ('F-18', 109.77, 3.7, 37000), ('Ga-68', 68, 2.2, 10000)
ON CONFLICT (isotope_code) DO NOTHING;
INSERT INTO protocols (isotope_code, name, duration_minutes, uptake_minutes, maximum_uptake_minutes) VALUES
  ('F-18', 'Онко-ПЭТ/КТ (FDG)', 90, 60, 90), ('Ga-68', 'FAPI', 30, 30, NULL),
  ('Ga-68', 'PSMA', 30, 30, NULL), ('Ga-68', 'DOTA TATE', 40, 40, NULL)
ON CONFLICT (isotope_code, name) DO NOTHING;
INSERT INTO scanners (name, model) VALUES ('GE Discovery IQ', 'PET/CT'), ('Siemens Horizon', 'PET/CT'), ('PET/CT 3', 'PET/CT') ON CONFLICT (name) DO NOTHING;
INSERT INTO scanner_profiles (scanner_id, patient_category, preparation_minutes, scan_minutes)
SELECT s.id, profile.category, profile.preparation, profile.scan FROM scanners s CROSS JOIN LATERAL (
  SELECT 'S'::TEXT category, CASE WHEN s.name='GE Discovery IQ' THEN 15 WHEN s.name='Siemens Horizon' THEN 15 ELSE 20 END::SMALLINT preparation, CASE WHEN s.name='GE Discovery IQ' THEN 20 WHEN s.name='Siemens Horizon' THEN 17 ELSE 20 END::SMALLINT scan
  UNION ALL SELECT 'M', CASE WHEN s.name='GE Discovery IQ' THEN 20 WHEN s.name='Siemens Horizon' THEN 18 ELSE 20 END, CASE WHEN s.name='GE Discovery IQ' THEN 25 WHEN s.name='Siemens Horizon' THEN 22 ELSE 20 END
  UNION ALL SELECT 'F', CASE WHEN s.name='GE Discovery IQ' THEN 30 WHEN s.name='Siemens Horizon' THEN 23 ELSE 20 END, CASE WHEN s.name='GE Discovery IQ' THEN 35 WHEN s.name='Siemens Horizon' THEN 25 ELSE 20 END
) profile ON CONFLICT (scanner_id, patient_category) DO NOTHING;
