SELECT
    profile."displayName"
FROM "identity"."customerProfiles" profile
JOIN "identity".users actor ON actor.id = profile."userId"
WHERE actor."publicId" = @Actor
