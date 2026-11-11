using Dapper;
using DisputePortal.Api.Service.Disputes;
using Xunit;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Customer_update_events_preserve_internal_note_and_transition_comment_privacy()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate the unrecognised payment."
            },
            "privacy-events-" + Guid.NewGuid(),
            ct);
        var detail = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        const string privateReview = "Internal investigation reference: confidential merchant intelligence.";
        await _repository.TransitionDispute(
            Agent,
            created.Id,
            new(detail.Dispute.Version, "UnderReview", privateReview),
            "private-transition",
            ct);
        await using var c = await _connections.OpenConnection(ct);
        var statusBody = await c.ExecuteScalarAsync<string>(
            """
            SELECT "notifications"."body" AS "body"
            FROM "notifications"."notifications" AS "notifications"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "notifications"."userId") = @Customer
            AND(
                SELECT "related2"."publicId"
                FROM "disputes"."disputes" AS "related2"
                WHERE "related2"."id" = "notifications"."disputeId") = @Id
            AND "notifications"."notificationType" = 'DisputeStatusChanged'
            """,
            new { Customer, Id = created.Id });
        Assert.DoesNotContain(privateReview, statusBody);
        var before = await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM "notifications"."events" e
            JOIN "identity"."users" u
            ON "u"."id" = "e"."userId"
            WHERE "u"."publicId" = @Customer
            """,
            new { Customer });
        await _repository.AddNote(Agent, created.Id, new NoteCommand("Sensitive internal reviewer notes.", "Internal"), "internal-note", ct);
        Assert.Equal(
            before,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."events" e
            JOIN "identity"."users" u
            ON "u"."id" = "e"."userId"
            WHERE "u"."publicId" = @Customer
            """,
                new { Customer }));
        await _repository.AddNote(
            Agent,
            created.Id,
            new NoteCommand("Please upload your purchase receipt.", "Customer"),
            "customer-note",
            ct);
        Assert.Equal(
            before + 1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."events" e
            JOIN "identity"."users" u
            ON "u"."id" = "e"."userId"
            WHERE "u"."publicId" = @Customer
            """,
                new { Customer }));
        var visible = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        Assert.Contains(visible.Notes, note => note.Content == "Please upload your purchase receipt.");
        Assert.DoesNotContain(visible.Notes, note => note.Content == "Sensitive internal reviewer notes.");
    }
}
