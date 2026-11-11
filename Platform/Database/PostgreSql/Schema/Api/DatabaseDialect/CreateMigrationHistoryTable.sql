CREATE TABLE IF NOT EXISTS schema_migrations  (
    version varchar(100) PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
)
