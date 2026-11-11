WHERE p.code = 'Default'
AND v."version" > 0
AND(@Search IS NULL
    OR {{SearchPredicate}} ESCAPE '~'
    OR {{RelatedSearchPredicate}} ESCAPE '~')
