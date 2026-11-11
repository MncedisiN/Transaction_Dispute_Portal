-- The existing dual-provider FX component keeps its original schema history.
CREATE OR ALTER TRIGGER configuration.protectExchangeRateIdentity
ON configuration."exchangeRates"
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted currentValue
        JOIN deleted previousValue
            ON previousValue.id=currentValue.id
        WHERE previousValue."publicId"<>currentValue."publicId"
    )
        THROW 51000, 'Record identity is immutable', 1;
END;
