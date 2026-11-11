-- Preserve all existing financial records, including historical cross-customer request GUID reuse.
CREATE INDEX payments_request_identity ON payments."payments"  (
    "requestId"
);
