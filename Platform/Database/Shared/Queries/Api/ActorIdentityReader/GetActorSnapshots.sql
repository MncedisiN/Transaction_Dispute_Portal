SELECT
    recorded_event."publicId" AS EventId,
    recorded_actor."publicId" AS ActorUserId,
    snapshot."firstName" AS FirstName,
    snapshot."middleName" AS MiddleName,
    snapshot."lastName" AS LastName,
    snapshot."accountName" AS AccountName,
    snapshot."loginIdentifier" AS LoginIdentifier,
    snapshot."roleKeys" AS RoleKeys,
    snapshot."actorKind" AS ActorKind,
    snapshot.provenance AS Provenance
FROM audit."actorSnapshots" snapshot
JOIN {{Table}} recorded_event ON recorded_event.id = snapshot."{{ForeignKey}}"
LEFT JOIN "identity".users recorded_actor ON recorded_actor.id = snapshot."actorUserId"
WHERE snapshot."sourceKind" = @Source
AND {{MembershipPredicate}}
