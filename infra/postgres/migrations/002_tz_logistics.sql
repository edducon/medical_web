ALTER TABLE scanners ADD COLUMN IF NOT EXISTS serial_number TEXT;
ALTER TABLE scanners ADD COLUMN IF NOT EXISTS manufacture_year SMALLINT CHECK (manufacture_year BETWEEN 1900 AND 2100);

CREATE TABLE IF NOT EXISTS scanner_profiles (
  scanner_id UUID NOT NULL REFERENCES scanners(id) ON DELETE CASCADE,
  patient_category TEXT NOT NULL CHECK (patient_category IN ('S', 'M', 'F')),
  preparation_minutes SMALLINT NOT NULL CHECK (preparation_minutes BETWEEN 0 AND 180),
  scan_minutes SMALLINT NOT NULL CHECK (scan_minutes BETWEEN 1 AND 300),
  PRIMARY KEY (scanner_id, patient_category)
);

INSERT INTO scanner_profiles (scanner_id, patient_category, preparation_minutes, scan_minutes)
SELECT id, category, preparation, scan
FROM scanners
CROSS JOIN LATERAL (
  SELECT 'S'::TEXT AS category, CASE WHEN name = 'GE Discovery IQ' THEN 15 ELSE 20 END::SMALLINT AS preparation, CASE WHEN name = 'GE Discovery IQ' THEN 20 ELSE 20 END::SMALLINT AS scan
  UNION ALL SELECT 'M', CASE WHEN name = 'GE Discovery IQ' THEN 20 ELSE 20 END, CASE WHEN name = 'GE Discovery IQ' THEN 25 ELSE 20 END
  UNION ALL SELECT 'F', CASE WHEN name = 'GE Discovery IQ' THEN 30 ELSE 20 END, CASE WHEN name = 'GE Discovery IQ' THEN 35 ELSE 20 END
) profile
ON CONFLICT (scanner_id, patient_category) DO NOTHING;

UPDATE scanner_profiles p SET preparation_minutes = v.preparation, scan_minutes = v.scan
FROM scanners s
JOIN (VALUES ('S', 15::SMALLINT, 17::SMALLINT), ('M', 18::SMALLINT, 22::SMALLINT), ('F', 23::SMALLINT, 25::SMALLINT)) AS v(category, preparation, scan) ON TRUE
WHERE p.scanner_id = s.id AND s.name = 'Siemens Horizon' AND p.patient_category = v.category;

ALTER TABLE protocols ADD COLUMN IF NOT EXISTS uptake_minutes SMALLINT NOT NULL DEFAULT 0 CHECK (uptake_minutes BETWEEN 0 AND 360);
ALTER TABLE protocols ADD COLUMN IF NOT EXISTS maximum_uptake_minutes SMALLINT CHECK (maximum_uptake_minutes BETWEEN 0 AND 360);
UPDATE protocols SET uptake_minutes = 60, maximum_uptake_minutes = 90 WHERE isotope_code = 'F-18';
INSERT INTO protocols (isotope_code, name, duration_minutes, uptake_minutes, maximum_uptake_minutes) VALUES
  ('Ga-68', 'FAPI', 30, 30, NULL),
  ('Ga-68', 'PSMA', 30, 30, NULL),
  ('Ga-68', 'DOTA TATE', 40, 40, NULL)
ON CONFLICT (isotope_code, name) DO UPDATE SET uptake_minutes = EXCLUDED.uptake_minutes, maximum_uptake_minutes = EXCLUDED.maximum_uptake_minutes, is_active = true;

ALTER TABLE shifts ADD COLUMN IF NOT EXISTS half_life_minutes NUMERIC(8,2);
ALTER TABLE shifts ADD COLUMN IF NOT EXISTS dose_coefficient_mbq_per_kg NUMERIC(8,3);
UPDATE shifts s SET half_life_minutes = settings.half_life_minutes, dose_coefficient_mbq_per_kg = settings.dose_coefficient_mbq_per_kg FROM isotope_settings settings WHERE settings.isotope_code = s.isotope_code AND (s.half_life_minutes IS NULL OR s.dose_coefficient_mbq_per_kg IS NULL);
ALTER TABLE shifts ALTER COLUMN half_life_minutes SET NOT NULL;
ALTER TABLE shifts ALTER COLUMN dose_coefficient_mbq_per_kg SET NOT NULL;
DO $$ BEGIN
  ALTER TABLE shifts ADD CONSTRAINT shifts_half_life_positive CHECK (half_life_minutes > 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
  ALTER TABLE shifts ADD CONSTRAINT shifts_coefficient_positive CHECK (dose_coefficient_mbq_per_kg > 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

ALTER TABLE appointments ADD COLUMN IF NOT EXISTS scan_start_at TIMESTAMPTZ;
ALTER TABLE appointments ADD COLUMN IF NOT EXISTS uptake_minutes SMALLINT NOT NULL DEFAULT 0 CHECK (uptake_minutes BETWEEN 0 AND 360);
ALTER TABLE appointments ADD COLUMN IF NOT EXISTS patient_category TEXT NOT NULL DEFAULT 'M' CHECK (patient_category IN ('S', 'M', 'F'));
UPDATE appointments SET scan_start_at = injection_at WHERE scan_start_at IS NULL;
ALTER TABLE appointments ALTER COLUMN scan_start_at SET NOT NULL;
CREATE INDEX IF NOT EXISTS appointments_scanner_scan_time_idx ON appointments (scanner_id, scan_start_at);
