CREATE TABLE IF NOT EXISTS telemetry_samples (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    recorded_at TIMESTAMPTZ NOT NULL,
    tank_level DOUBLE PRECISION NOT NULL CHECK (tank_level >= 0 AND tank_level <= 100),
    pump_running BOOLEAN NOT NULL,
    revision BIGINT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_telemetry_samples_recorded_at
    ON telemetry_samples (recorded_at DESC);

CREATE TABLE IF NOT EXISTS security_events (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at TIMESTAMPTZ NOT NULL,
    category TEXT NOT NULL,
    message TEXT NOT NULL,
    actor TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_security_events_occurred_at
    ON security_events (occurred_at DESC);
