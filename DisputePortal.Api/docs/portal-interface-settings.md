# Portal configuration and policy storage
 
## Database ownership

| Information | Authoritative storage |
| --- | --- |
| Routing and verification thresholds | API: `configuration.riskPolicies` |
| Automatic refund limits and publications | API: `configuration.refundPolicies` |
| Dispute reasons and applicable transaction types | API: `configuration.disputeCategories` and `configuration.disputeCategoryTransactionTypes` |
| Workflow statuses and transition metadata | API: `disputes.caseStatuses` |
| Enabled evidence categories | API: `configuration.evidenceCategories` |
| Allowed evidence extensions and MIME types | API: `configuration.evidenceFormats` |
| Evidence quotas and access limits | API: `configuration.evidencePolicies` |
| Guided enquiry flow, business descriptions and client resource settings | API: `configuration.portalSettings`, code `Interface` |
| Validation of editable reference settings | API: `configuration.portalSettings`, code `Validation` |
| Country, currency and financial-service configuration | Configuration service: `configuration.settings` |

Connections, secrets and deployment controls remain protected hosting configuration. Public reference data never includes them. Binary signature validation and authorization remain code capabilities; enabling an extension never bypasses content inspection.

## One policy table per family

Migration `068_consolidated_policy_tables` consolidates each API family into one table. Its version-zero family row retains the identity, initial definition and revision counters. Published rows have positive versions, effective dates and the existing publication IDs. Their `policyId` references the family row in the same table. Initial definitions are excluded from effective policy resolution and publication-history counts.

There are no current `riskPolicyPresets`, `refundPolicyPresets`, `riskPolicyVersions` or `refundPolicyVersions` tables. SQL migrations populate initial values. Reset reads the initial definition and publishes a new revision through the normal publication path. It never overwrites an old publication. Existing publication IDs, financial-policy references, values and audit attribution are preserved.

Configuration-service migration `005_consolidated_settings` applies the same pattern to its one `configuration.settings` table, retaining published JSON snapshots. The typed financial configuration contract remains unchanged. Service migrations are discovered from packaged SQL resources and compared with database history; adding a migration does not require a C# version list.

API migration `070_published_policy_reference_integrity` and configuration-service migration `006_published_settings_reference_integrity` enforce the meaning of the self-references. A publication must belong to a version-zero family; a predecessor must be a publication in that same family. Cases and financial commands must reference published policy revisions. Historical soft-deleted publications remain valid references.

## Request flow

1. Vue loads `GET /api/v1/portal/reference` before mounting pages.
2. The API combines current reference settings with authoritative status, transaction-type, reason and evidence tables.
3. Vue renders normal interface copy directly and reads descriptive options such as `transactionPageSize` through `clientOption(key)`.
4. The existing guided enquiry controls render the database-configured flow, prompts and choices. Configuration never supplies executable Vue or JavaScript.
5. Verification screens display the actual `expiresAt` from the API, together with server-provided resend availability and trusted-device duration. No fixed five-minute claim is shown.

The reference response contains no generic UI text catalogue. Historical bodies containing old interface text remain retained as history; a new current revision omits that text.

## Publishing and evidence

Existing secured publication endpoints retain authorization, CSRF protection, optimistic concurrency, request replay protection, effective-date resolution and audit/outbox writes. Published values are immutable. Soft deletion records the actor, time and reason; copy/restore publishes a new revision. API contracts can still call a publication a "version" without requiring a separate version table.

`GET /api/v1/admin/portal-settings` and `POST /api/v1/admin/portal-settings` remain restricted to `super_admin`. Publications carry `{ expectedVersion, settings, reason }`; stale versions conflict. Validation bounds and supported enquiry configuration are read from the database `Validation` row.

Migration `069_database_evidence_and_configurable_content` normalizes evidence settings and adds a non-null evidence-category foreign key without replacing files. The upload resource filter reads one policy snapshot before multipart binding and configures native ASP.NET Core request/form limits. The repository applies category, content and quota checks from that policy. Existing stored evidence stays readable if a format is later disabled for new uploads.

Evidence payload persistence uses `ApiRuntime:EvidencePayloadWriteTimeoutSeconds` from hosting configuration (currently 90 seconds). This is an operational I/O timeout, separate from database-owned upload quotas and financial policies. The same configured budget applies to both supported database providers.

## Validation and deployment

Frontend unit tests and Vitest dependencies are removed. `pnpm test` runs Playwright workflows; type checking, lint and production build remain separate commands. API unit tests are concentrated in twelve business classes covering refunds, routing, deadlines, closure, policy resolution and dispute workflows. Integration tests use real SQL Server/PostgreSQL databases and verify durable financial operations and events.

Use the existing migration command before starting the new runtime. Keep a verified backup and compare original transaction, evidence and policy-publication values before and after cutover. Previously applied migration scripts retain their historical names and checksums; they are not rewritten.
