SELECT
    "support_concerns"."code" AS "code",
    "support_concerns"."label" AS "label",
    "support_concerns"."requiresCase" AS RequiresCase,
    "support_concerns"."contextKind" AS ContextKind,
    "support_concerns"."collectDate" AS CollectDate
FROM "support"."concerns" AS "support_concerns"
WHERE "support_concerns"."code" = @Code
AND "support_concerns"."enabled" = {{TrueValue}}
