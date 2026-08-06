ALTER TABLE appointments ADD COLUMN IF NOT EXISTS patient_number_search_tokens TEXT[] NOT NULL DEFAULT '{}';
CREATE INDEX IF NOT EXISTS appointments_patient_number_search_tokens_idx ON appointments USING GIN (patient_number_search_tokens);
