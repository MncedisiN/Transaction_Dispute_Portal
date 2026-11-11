FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
