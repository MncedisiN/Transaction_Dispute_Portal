UPDATE "transactions"."transactions"
SET
    "transactionStatus" = @Status,
    "disputeEligible" = @Eligible,
    "debitOrderScheme" = @DebitOrderScheme,
    "mandateStatus" = @MandateStatus
WHERE "transactions"."publicId" = @Id
AND(
    SELECT
        "related2"."publicId"
    FROM "accounts"."accounts" AS "related2"
    WHERE "related2"."id" = "transactions"."accountId") = @Account
