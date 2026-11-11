{{SupportCandidateSql}}
AND "u"."publicId" = @Target
AND("r"."code" = @Role
    OR {{Supervisor}})
