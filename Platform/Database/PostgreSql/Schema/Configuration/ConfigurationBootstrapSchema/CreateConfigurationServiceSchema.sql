CREATE SCHEMA IF NOT EXISTS configuration;

CREATE TABLE IF NOT EXISTS configuration."exchangeRates" (
    "id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "publicId" uuid NOT NULL UNIQUE,
    "sourceCurrency" char(3) NOT NULL,
    "targetCurrency" char(3) NOT NULL,
    "rate" decimal(28,12) NOT NULL CHECK("rate">0),
    "source" varchar(100) NOT NULL,
    "sourceKind" varchar(16) NOT NULL,
    "publishedAt" timestamptz NULL,
    "publicationPrecision" varchar(20) NOT NULL,
    "retrievedAt" timestamptz NOT NULL,
    "effectiveAt" timestamptz NOT NULL,
    "expiresAt" timestamptz NOT NULL,
    "approvalStatus" varchar(24) NOT NULL CHECK("approvalStatus" IN ('Approved','PendingApproval')),
    "changedBy" uuid NULL,
    "approvedBy" uuid NULL,
    "reason" varchar(500) NOT NULL,
    "version" int NOT NULL,
    "previousRateId" bigint NULL REFERENCES configuration."exchangeRates"("id"),
    CHECK("expiresAt">"effectiveAt"),
    UNIQUE("sourceCurrency","targetCurrency","version")
);

CREATE INDEX IF NOT EXISTS ix_exchange_rates_active ON configuration."exchangeRates" (
    "sourceCurrency",
    "targetCurrency",
    "approvalStatus",
    "effectiveAt",
    "expiresAt"
);
