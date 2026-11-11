SELECT
    request."publicId" AS Id,
    owner."publicId" AS UserId,
    request.reference,
    request.status,
    request.version,
    profile."displayName" AS CustomerName,
    profile.email,
    request."protectedReason" AS Reason,
    request."protectedDecision" AS Decision,
    request."createdAt" AS CreatedAt,
    request."reviewedAt" AS ReviewedAt
FROM "identity"."accountDeletionRequests" request
JOIN "identity"."users" owner ON owner.id = request."userId"
JOIN "identity"."customerProfiles" profile ON profile."userId" = request."userId"
WHERE (@Administration = {{TrueValue}}
    OR owner."publicId" = @Actor)
ORDER BY request."createdAt" DESC
OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY
