IF SCHEMA_ID('configuration') IS NULL
    EXEC('CREATE SCHEMA configuration');

IF OBJECT_ID('configuration.exchangeRates') IS NULL
BEGIN
CREATE TABLE configuration."exchangeRates" (
    "id" bigint IDENTITY(1,1) PRIMARY KEY,
    "publicId" uniqueidentifier NOT NULL UNIQUE,
    "sourceCurrency" char(3) NOT NULL,
    "targetCurrency" char(3) NOT NULL,
    "rate" decimal(28,12) NOT NULL CHECK("rate">0),
    "source" varchar(100) NOT NULL,
    "sourceKind" varchar(16) NOT NULL,
    "publishedAt" datetimeoffset NULL,
    "publicationPrecision" varchar(20) NOT NULL,
    "retrievedAt" datetimeoffset NOT NULL,
    "effectiveAt" datetimeoffset NOT NULL,
    "expiresAt" datetimeoffset NOT NULL,
    "approvalStatus" varchar(24) NOT NULL CHECK("approvalStatus" IN ('Approved','PendingApproval')),
    "changedBy" uniqueidentifier NULL,
    "approvedBy" uniqueidentifier NULL,
    "reason" varchar(500) NOT NULL,
    "version" int NOT NULL,
    "previousRateId" bigint NULL REFERENCES configuration."exchangeRates"("id"),
    CHECK("expiresAt">"effectiveAt"),
    UNIQUE("sourceCurrency","targetCurrency","version")
);

CREATE INDEX ix_exchange_rates_active ON configuration."exchangeRates" (
    "sourceCurrency",
    "targetCurrency",
    "approvalStatus",
    "effectiveAt",
    "expiresAt"
);

END;
