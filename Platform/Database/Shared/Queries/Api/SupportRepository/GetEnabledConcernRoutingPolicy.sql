SELECT
    "support_concerns"."id" AS "id",
    "support_concerns"."code" AS "code",
    "support_concerns"."label" AS "label",
    "support_concerns"."queueType" AS QueueType,
    "support_concerns"."priority" AS "priority",
    "support_concerns"."requiresCase" AS RequiresCase
FROM "support"."concerns" AS "support_concerns"
WHERE "support_concerns"."code" = @Code
AND "support_concerns"."enabled" = {{TrueValue}}
