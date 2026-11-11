SELECT
    u."publicId" AS UserId,
    f."flowHash" AS FlowHash,
    f."passwordStamp" AS PasswordStamp,
    f."agentHash" AS AgentHash,
    f."protectedDestination" AS ProtectedDestination,
    f."verifyRegisteredContact" AS VerifyRegisteredContact,
    f."expiresAt" AS ExpiresAt,
    f."resendAvailableAt" AS ResendAvailableAt,
    f."resendCount" AS ResendCount,
    f.revision,
    u."passwordHash" AS PasswordHash,
    u."accountStatus" AS Status,
    u."loginOtpEnabled" AS LoginOtpEnabled,
    u."loginIdentifier" AS LoginIdentifier,
    i.channel,
    i.state,
    i."codeHash" AS CodeHash,
    i.attempts,
    i."expiresAt" AS CodeExpiresAt
FROM "identity"."loginVerificationFlows" f
JOIN "identity"."identityChallenges" i ON i.id = f."challengeId"
AND i."userId" = f."userId"
AND i.purpose = 'Login'
JOIN "identity"."users" u ON u.id = f."userId"
WHERE f."publicId" = @Id
