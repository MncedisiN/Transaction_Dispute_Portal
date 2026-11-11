FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
JOIN "identity"."customerProfiles" p ON "p"."userId" = "d"."customerId"
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId"
