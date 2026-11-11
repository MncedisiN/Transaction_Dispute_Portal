using Dapper;
using System.Data.Common;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Catalogue_contains_all_stable_codes_and_constrains_case_history_without_rewriting_it()
    {
        var store = new CaseStatusRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider));
        var value = await store.GetWorkflowStatuses(default);
        Assert.Equal(
            new[] { "Submitted", "UnderReview", "AwaitingCustomer", "Resolved", "Declined", "Withdrawn" },
            value.Items.Select(x => x.Code));
        Assert.Equal(6, value.Revision);
        Assert.Contains("UnderReview", value.Items.Single(x => x.Code == "Resolved").WorkflowTargets);
        Assert.True(value.Items.Single(x => x.Code == "Withdrawn").Closed);
        await new DatabaseInitializer(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read()).InitializeDatabase(default);
        Assert.Equal(6, (await store.GetWorkflowStatuses(default)).Items.Length);
        await using var c = await _connections.OpenConnection(default);
        var links = (await c.QueryAsync<NativeRelationship>(RelationshipCatalogSql)).Where(x => x.ParentTable == "disputes.caseStatuses").ToArray();
        Assert.Equal(3, links.Length);
        Assert.All(
            links,
            link =>
        {
            Assert.Equal("code", link.ParentColumn);
            Assert.Equal(_provider == DatabaseProvider.SqlServer ? "nvarchar" : "text", link.ChildType);
        });
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync("""UPDATE disputes."caseStatuses" SET code='Unknown' WHERE code='Submitted'"""));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync("""DELETE FROM disputes."caseStatuses" WHERE code='Submitted'"""));
    }

    [Fact]
    public async Task Description_changes_are_authorised_versioned_audited_and_enqueued_atomically()
    {
        var store = new CaseStatusRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider));
        var before = await store.GetWorkflowStatuses(default);
        var row = before.Items.Single(x => x.Code == "UnderReview");
        var request = new UpdateCaseStatusRequest
        {
            ExpectedRevision = before.Revision,
            DisplayName = "Specialist Review",
            CustomerLabel = "Under Specialist Review",
            NextStep = "Your specialist is reviewing the available evidence.",
            SortOrder = row.SortOrder,
            Reason = "Clarify the customer-facing specialist review description."
        };
        await Assert.ThrowsAsync<PortalForbiddenException>(() => store.UpdateWorkflowStatus(Agent, row.Code, request, default));
        await PromotePolicyAdministrator();
        await store.UpdateWorkflowStatus(Agent, row.Code, request, default);
        var after = await store.GetWorkflowStatuses(default);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(request.DisplayName, after.Items.Single(x => x.Code == row.Code).DisplayName);
        await Assert.ThrowsAsync<PortalConflictException>(() => store.UpdateWorkflowStatus(Agent, row.Code, request, default));
        await using var c = await _connections.OpenConnection(default);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.\"auditEvents\" WHERE action='CaseStatusDescriptionsChanged'"));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM service_outbox WHERE event_type='dispute.case-statuses.changed'"));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM disputes.disputes"));
        Assert.Equal(2, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM transactions.transactions"));
    }
    private string RelationshipCatalogSql => _provider == DatabaseProvider.SqlServer ? """
        SELECT child_schema.name + '.' + child_table.name AS ChildTable,
            child_column.name AS ChildColumn,
            TYPE_NAME(child_column.user_type_id) AS ChildType,
            parent_schema.name + '.' + parent_table.name AS ParentTable,
            parent_column.name AS ParentColumn,
            TYPE_NAME(parent_column.user_type_id) AS ParentType
        FROM sys.foreign_key_columns relation
        JOIN sys.tables child_table ON child_table.object_id = relation.parent_object_id
        JOIN sys.schemas child_schema ON child_schema.schema_id = child_table.schema_id
        JOIN sys.columns child_column
            ON child_column.object_id = child_table.object_id
            AND child_column.column_id = relation.parent_column_id
        JOIN sys.tables parent_table ON parent_table.object_id = relation.referenced_object_id
        JOIN sys.schemas parent_schema ON parent_schema.schema_id = parent_table.schema_id
        JOIN sys.columns parent_column
            ON parent_column.object_id = parent_table.object_id
            AND parent_column.column_id = relation.referenced_column_id
        """ : """
        SELECT child_schema.nspname || '.' || child_table.relname AS ChildTable,
            child_column.attname AS ChildColumn,
            child_type.typname AS ChildType,
            parent_schema.nspname || '.' || parent_table.relname AS ParentTable,
            parent_column.attname AS ParentColumn,
            parent_type.typname AS ParentType
        FROM pg_catalog.pg_constraint relation
        JOIN pg_catalog.pg_class child_table ON child_table.oid = relation.conrelid
        JOIN pg_catalog.pg_namespace child_schema ON child_schema.oid = child_table.relnamespace
        JOIN pg_catalog.pg_class parent_table ON parent_table.oid = relation.confrelid
        JOIN pg_catalog.pg_namespace parent_schema ON parent_schema.oid = parent_table.relnamespace
        CROSS JOIN LATERAL unnest(relation.conkey, relation.confkey) part(child_number, parent_number)
        JOIN pg_catalog.pg_attribute child_column
            ON child_column.attrelid = child_table.oid
            AND child_column.attnum = part.child_number
        JOIN pg_catalog.pg_type child_type ON child_type.oid = child_column.atttypid
        JOIN pg_catalog.pg_attribute parent_column
            ON parent_column.attrelid = parent_table.oid
            AND parent_column.attnum = part.parent_number
        JOIN pg_catalog.pg_type parent_type ON parent_type.oid = parent_column.atttypid
        WHERE relation.contype = 'f'
        """;


    private class NativeRelationship
    {
        public string ChildTable { get; set; } = null!;
        public string ChildColumn { get; set; } = null!;
        public string ChildType { get; set; } = null!;
        public string ParentTable { get; set; } = null!;
        public string ParentColumn { get; set; } = null!;
        public string ParentType { get; set; } = null!;
        public string Signature => $"{ChildTable}.{ChildColumn}>{ParentTable}.{ParentColumn}";
    }
}
