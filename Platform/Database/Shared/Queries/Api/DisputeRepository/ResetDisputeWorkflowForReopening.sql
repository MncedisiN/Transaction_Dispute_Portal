UPDATE "disputes"."disputes"
SET
    "workflowCycle" = "workflowCycle" + 1,
    "assignedToId" = NULL,
    "assignedAt" = NULL,
    "autoAssignmentPaused" = {{FalseValue}},
    "firstResponseAt" = NULL,
    "responseDueAt" = @ResponseDue,
    "dueAt" = @Due
WHERE "publicId" = @Id
