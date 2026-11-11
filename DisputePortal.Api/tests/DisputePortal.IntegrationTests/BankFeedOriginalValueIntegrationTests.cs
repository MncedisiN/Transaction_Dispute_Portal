using DisputePortal.Api.Models.Banking;
using System.Data.Common;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Bank_feed_original_payment_fields_are_immutable_before_any_dispute_while_financial_status_can_advance()
    {
        var ct = CancellationToken.None;
        const string externalAccount = "immutable-original-bank-account";
        await _repository.LinkOwnedAccount(Customer, Account, "immutable-original-bank", externalAccount, ct);
        var binding = (await _repository.GetOwnedBinding(Customer, Account, ct))!;
        var item = new BankTransaction
        {
            ExternalId = "immutable-original-payment",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Description = "CityFuel purchase",
            MerchantName = "CityFuel",
            Amount = 249.99m,
            Currency = "ZAR",
            Type = "CardPurchase",
            Direction = "Debit",
            Status = "Pending",
            DisputeEligible = true
        };
        var pending = new BankTransactionPage
        {
            ExternalAccountId = externalAccount,
            Items = [item],
            NextCursor = "original-cursor",
            HasMore = false
        };
        await _repository.ApplyPage(Customer, binding, pending, ct);
        binding = (await _repository.GetOwnedBinding(Customer, Account, ct))!;
        await using var connection = await _connections.OpenConnection(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(
            """
            SELECT payment."publicId"
            FROM "transactions"."bankLinks" link
            JOIN "transactions"."transactions" payment ON payment.id = link."transactionId"
            JOIN "accounts"."accounts" account ON account.id = link."accountId"
            WHERE account."publicId" = @Account
            """,
            new { Account });
        var original = (await _repository.GetTransaction(Customer, id, ct))!;
        Assert.Equal("Pending", original.Status);
        Assert.Null(original.DisputeId);
        var changes = new[]
        {
            new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
            {
                Amount = 250m
            },
            new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
            {
                Currency = "USD"
            },
            new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
            {
                OccurredAt = item.OccurredAt.AddDays(-1)
            },
            new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
            {
                Description = "Changed original description"
            },
            new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
            {
                MerchantName = "Another merchant"
            },
            new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
            {
                Type = "DebitOrder"
            },
            new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
            {
                Direction = "Credit"
            }
        };
        foreach (var changed in changes)
        {
            var page = new global::DisputePortal.Api.Models.Banking.BankTransactionPage(pending)
            {
                Items = [changed],
                NextCursor = "must-not-advance"
            };
            if (changed.Currency != item.Currency)
            {
                await Assert.ThrowsAsync<PortalValidationException>(() => _repository.ApplyPage(Customer, binding, page, ct));
            }
            else
            {
                await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ApplyPage(Customer, binding, page, ct));
            }

            Assert.Equivalent(original, (await _repository.GetTransaction(Customer, id, ct))!, strict: true);
            Assert.Equivalent(binding, (await _repository.GetOwnedBinding(Customer, Account, ct))!, strict: true);
        }

        var rejected = await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET amount = amount + 1
            WHERE "publicId" = @Id
            """,
            new { Id = id }));
        if (rejected is PostgresException postgres)
        {
            Assert.Equal("23514", postgres.SqlState);
        }
        else
        {
            Assert.Equal(51058, Assert.IsType<SqlException>(rejected).Number);
        }

        Assert.Contains("Original bank transaction values are immutable", rejected.Message);
        foreach (var changedDescription in new[]
        {
            item.Description.ToUpperInvariant(),
            item.Description + " "
        }

        )
        {
            var textChange = await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
                """
                UPDATE transactions.transactions SET description=@Description WHERE "publicId"=@Id
                """,
                new { Id = id, Description = changedDescription }));
            Assert.Contains("Original bank transaction values are immutable", textChange.Message);
        }

        if (_provider == DisputePortal.Api.Repository.Data.DatabaseProvider.SqlServer)
        {
            await Assert.ThrowsAnyAsync<DbException>(() => connection.ExecuteAsync(
                """
                UPDATE transactions.transactions SET "transactionAt"=SWITCHOFFSET("transactionAt",'+01:00')
                WHERE "publicId"=@Id
                """,
                new { Id = id }));
        }

        Assert.Equivalent(original, (await _repository.GetTransaction(Customer, id, ct))!, strict: true);
        Assert.Equivalent(binding, (await _repository.GetOwnedBinding(Customer, Account, ct))!, strict: true);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."disputes"
            """));
        await _repository.ApplyPage(
            Customer,
            binding,
            new global::DisputePortal.Api.Models.Banking.BankTransactionPage(pending)
            {
                Items = [new global::DisputePortal.Api.Models.Banking.BankTransaction(item) { Status = "Posted" }],
                NextCursor = "posted-cursor"
            },
            ct);
        var posted = (await _repository.GetTransaction(Customer, id, ct))!;
        Assert.Equal("Posted", posted.Status);
        Assert.True(posted.DisputeEligible);
        original.Status = "Posted";
        original.DisputeEligible = true;
        Assert.Equivalent(original, posted, strict: true);
        var advanced = (await _repository.GetOwnedBinding(Customer, Account, ct))!;
        Assert.Equal("posted-cursor", advanced.Cursor);
        Assert.Equal(binding.Revision + 1, advanced.Revision);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."bankLinks" link
            JOIN "accounts"."accounts" account ON account.id = link."accountId"
            WHERE account."publicId" = @Account
            """,
                new { Account }));
    }
}
