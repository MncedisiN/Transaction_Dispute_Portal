using Dapper;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Only_cheque_and_savings_can_open_and_duplicate_products_are_rejected()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "products@example.test");
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.OpenAccount(Customer, Guid.NewGuid(), "Credit", 1, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.OpenAccount(Customer, Guid.NewGuid(), "Cheque", 1, ct));
        var savings = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 1, ct);
        var state = await _repository.GetProvisioningStatus(Customer, ct);
        Assert.Equal(CustomerAccountPolicy.MaximumAccounts, state.AccountLimit);
        Assert.Equal(new[] { "Cheque", "Savings" }, state.Accounts.Select(item => item.AccountType).Order().ToArray());
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 1, ct));
    }

    [Fact]
    public async Task Transaction_date_filters_use_the_same_South_African_calendar_day_as_cases()
    {
        var ct = CancellationToken.None;
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var start = new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero);
        var times = new[]
        {
            start.AddSeconds(-1),
            start,
            start.AddDays(1).AddSeconds(-1),
            start.AddDays(1)
        };
        await using var c = await _connections.OpenConnection(ct);
        // Equal capture times must retain insertion order through the internal
        // key; public UUID order is independent of the creation sequence.
        for (var i = 0; i < ids.Length; i++)
            await c.ExecuteAsync(
            """
            INSERT INTO "transactions"."transactions"("publicId",
                "accountId",
                "transactionReference",
                "transactionAt",
                "description",
                "merchantName",
                "amount",
                "currencyCode",
                "transactionTypeId",
                "direction",
                "transactionStatus",
                "disputeEligible",
                "createdAt")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "accounts"."accounts" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Account),
                @Reference,
                @At,
                'Local day boundary',
                'Boundary merchant',
                1,
                'ZAR',
                (
                    SELECT "resolved2"."id"
                    FROM "configuration"."transactionTypes" AS "resolved2"
                    WHERE "resolved2"."code" = 'CardPurchase'),
                'Debit',
                'Posted',
                @Eligible,
                @Captured)
            """,
            new
            {
                Id = ids[i],
                Account,
                Reference = "LOCAL-TIME-BOUNDARY-" + i,
                At = times[i],
                Captured = start.AddDays(10),
                Eligible = true
            });
        var day = new DateOnly(2026, 10, 15);
        var result = await _repository.GetTransactions(
            Customer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 50,
                Search = "LOCAL-TIME-BOUNDARY",
                From = day,
                To = day,
                Direction = null,
                Type = null,
                Sort = "date_asc",
                AccountId = Account
            },
            ct);
        Assert.Equal(2, result.Total);
        Assert.Equal(new[] { ids[1], ids[2] }, result.Items.Select(item => item.Id).ToArray());
        var newest = await _repository.GetTransactions(
            Customer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 50,
                Search = "LOCAL-TIME-BOUNDARY",
                From = day,
                To = day,
                Direction = null,
                Type = null,
                Sort = "date_desc",
                AccountId = Account
            },
            ct);
        Assert.Equal(new[] { ids[2], ids[1] }, newest.Items.Select(item => item.Id).ToArray());
        foreach (var sort in new[]
        {
            "amount_asc",
            "amount_desc"
        }

        )
            Assert.Equal(
            new[] { ids[2], ids[1] },
            (await _repository.GetTransactions(
                Customer,
                new TransactionFilter
                {
                    Page = 1,
                    PageSize = 50,
                    Search = "LOCAL-TIME-BOUNDARY",
                    From = day,
                    To = day,
                    Direction = null,
                    Type = null,
                    Sort = sort,
                    AccountId = Account
                },
                ct)).Items.Select(item => item.Id).ToArray());
        Assert.Empty((await _repository.GetTransactions(
            OtherCustomer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 50,
                Search = "LOCAL-TIME-BOUNDARY",
                From = day,
                To = day,
                Direction = null,
                Type = null,
                Sort = "date_asc",
                AccountId = Account
            },
            ct)).Items);
    }
}
