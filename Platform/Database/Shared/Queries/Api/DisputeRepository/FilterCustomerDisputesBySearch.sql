AND (
    @SearchPattern IS NULL
    OR {{SearchPredicate}}
    OR {{RelatedSearchPredicate}}
    OR {{AdditionalSearchPredicate}}
)
