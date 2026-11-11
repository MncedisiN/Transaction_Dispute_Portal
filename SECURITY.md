# Security scope

The assessment is server-side authorization, native antiforgery, bounded verification challenges, structured sanitized errors, service signature validation, durable outboxes/inboxes, refund idempotency and ledger integrity. Redis/RabbitMQ are optional accelerators/transport choices; disabling them does not disable the normal durable financial workflow.

The default Compose stack publishes only localhost:8080 and uses private container networking. It generates fresh installation keys/passwords and persists them in named volumes. Original development data and installation identities are not shipped. Use operator-managed HTTPS and validated encrypted database/service connections for a public deployment.

private/messaging.json intentionally contains the owner's testing provider keys for this controlled handoff. Docker build contexts exclude the folder; the initializer reads it through a read-only runtime mount. It cannot override database passwords or installation signing/OTP keys.

 

