SELECT
    COUNT(*)
FROM "disputes"."disputes" active
JOIN "disputes"."disputes" current_case ON "current_case"."transactionId" = "active"."transactionId"
WHERE "current_case"."publicId" = @Id
AND "active"."publicId" <> @Id
AND "active"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer')
