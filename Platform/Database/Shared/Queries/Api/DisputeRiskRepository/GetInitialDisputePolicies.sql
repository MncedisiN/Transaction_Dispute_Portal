SELECT
    "initialVersion" AS Version,
    "highValueThreshold" AS HighValueThreshold,
    "extraCheckAfterDays" AS ExtraCheckAfterDays,
    "debitOrderWindowDays" AS DebitOrderWindowDays,
    "debitOrderAppLimit" AS DebitOrderAppLimit
FROM configuration."riskPolicies"
WHERE "version" = 0 AND "code" = 'Default';

SELECT
    "initialVersion" AS Version,
    enabled AS Enabled,
    "maximumAmount" AS MaximumAmount,
    "maximumAgeHours" AS MaximumAgeHours,
    "dailyLimit" AS DailyLimit,
    "duplicateWindowMinutes" AS DuplicateWindowMinutes
FROM configuration."refundPolicies"
WHERE "version" = 0 AND "code" = 'Default';
