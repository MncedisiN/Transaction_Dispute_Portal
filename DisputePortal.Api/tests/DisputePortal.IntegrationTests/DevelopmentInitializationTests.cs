using Dapper;
using DisputePortal.Api.Models;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Configured_development_users_are_created_once_without_resetting_existing_access_or_money()
    {
        using var config = SeedConfiguration();
        var hasher = new PasswordHasher<PortalPasswordUser>();
        var initializer = new DevelopmentUserInitializer(_connections, DatabaseDialect.GetDatabaseDialect(_provider), hasher);
        await using var connection = await _connections.OpenConnection(default);
        const string totalsSql = """
            SELECT COUNT(*) AS Transactions,
            SUM("transactions"."amount") AS Total
            FROM "transactions"."transactions" AS "transactions"
            """;
        var before = await connection.QuerySingleAsync<(int Transactions, decimal Total)>(totalsSql);
        var results = await Task.WhenAll(
            initializer.InitializeConfiguredDevelopmentUsers(new SeedEnvironment(), config, default),
            initializer.InitializeConfiguredDevelopmentUsers(new SeedEnvironment(), config, default));
        Assert.Equal(1, results.Sum());
        var user = await connection.QuerySingleAsync<(Guid Id, string Hash)>(
            """
            SELECT "users"."publicId" AS "id",
            "users"."passwordHash" AS "password_hash"
            FROM "identity"."users" AS "users"
            WHERE "users"."normalizedLoginIdentifier" = @Email
            """,
            new { Email = "CONFIGURED@EXAMPLE.INVALID" });
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(new PortalPasswordUser(user.Id), user.Hash, config["DevelopmentSeed:Users:0:Password"]!));
        config["DevelopmentSeed:Users:0:Password"] = "Aa1!" + Guid.NewGuid().ToString("N");
        config["DevelopmentSeed:Users:0:Roles:0"] = "super_admin";
        Assert.Equal(0, await initializer.InitializeConfiguredDevelopmentUsers(new SeedEnvironment(), config, default));
        Assert.Equal(
            user.Hash,
            await connection.ExecuteScalarAsync<string>(
                """
            SELECT "users"."passwordHash" AS "password_hash"
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @Id
            """,
                new { user.Id }));
        Assert.Equal(
            "customer",
            await connection.ExecuteScalarAsync<string>(
                """
            SELECT "r"."code" AS "role_key"
            FROM "identity"."userRoles" ur
            JOIN "identity"."roles" r
            ON "r"."id" = "ur"."roleId"
            JOIN "identity"."users" u
            ON "u"."id" = "ur"."userId"
            WHERE "u"."publicId" = @Id
            """,
                new { user.Id }));
        Assert.Equal(before, await connection.QuerySingleAsync<(int Transactions, decimal Total)>(totalsSql));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_accounts"."userId") = @Id
            """,
                new { user.Id }));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'DevelopmentUserCreated'
            AND "admin_audit_logs"."entityPublicId" = @Id
            """,
                new { user.Id }));
    }

    [Fact]
    public async Task Invalid_configured_development_role_rolls_back_the_entire_bootstrap()
    {
        using var config = SeedConfiguration();
        config["DevelopmentSeed:Users:1:Email"] = "invalid-role@example.invalid";
        config["DevelopmentSeed:Users:1:FirstName"] = "Configured";
        config["DevelopmentSeed:Users:1:LastName"] = "Invalid";
        config["DevelopmentSeed:Users:1:Password"] = "Aa1!" + Guid.NewGuid().ToString("N");
        config["DevelopmentSeed:Users:1:Roles:0"] = "missing_role";
        var initializer = new DevelopmentUserInitializer(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new PasswordHasher<PortalPasswordUser>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeConfiguredDevelopmentUsers(
            new SeedEnvironment(),
            config,
            default));
        await using var connection = await _connections.OpenConnection(default);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "identity"."users" AS "users"
            WHERE "users"."normalizedLoginIdentifier" IN('CONFIGURED@EXAMPLE.INVALID',
                'INVALID-ROLE@EXAMPLE.INVALID')
            """));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'DevelopmentUserCreated'
            """));
    }

    private static ConfigurationRoot SeedConfiguration() =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DevelopmentSeed:Enabled"] = "true",
        ["DevelopmentSeed:Users:0:Email"] = "configured@example.invalid",
        ["DevelopmentSeed:Users:0:FirstName"] = "Configured",
        ["DevelopmentSeed:Users:0:LastName"] = "Customer",
        ["DevelopmentSeed:Users:0:Password"] = "Aa1!" + Guid.NewGuid().ToString("N"),
        ["DevelopmentSeed:Users:0:Roles:0"] = "customer"
    }).Build();

    private class SeedEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "DisputePortal.Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
