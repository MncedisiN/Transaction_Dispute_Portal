using DisputePortal.Api.Repository.Interfaces;
using DisputePortal.Api.Models.Banking;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.IntegrationTests;

internal sealed class RepositoryTestFixture : IAuthenticationRepository, ITransactionRepository, IBankFeedStore, IBankingProvisioningRepository, IDisputeRepository, IOperationsRepository, IDisputeRiskRepository, INotificationRepository, INotificationDeliveryRepository, IAuditRepository, IUserAdministrationRepository, IEvidenceRepository, ISupportRepository, ISupportReadRepository, IProfileRepository, IContactVerificationRepository, IIdentityVerificationRepository, ISecurityActivityRepository, IAccountManagementRepository
{
    private readonly AuthenticationRepository authenticationRepository;
    private readonly LegacyBankingFixture bankingRepository;
    private readonly DisputeRepository disputeRepository;
    private readonly DisputeOperationsRepository disputeOperationsRepository;
    private readonly DisputeRiskRepository disputeRiskRepository;
    private readonly NotificationRepository notificationRepository;
    private readonly AuditRepository auditRepository;
    private readonly UserAdministrationRepository userAdministrationRepository;
    private readonly EvidenceRepository evidenceRepository;
    private readonly SupportRepository supportRepository;
    private readonly ProfileRepository profileRepository;
    private readonly ContactVerificationRepository contactVerificationRepository;
    private readonly IdentityVerificationRepository identityVerificationRepository;
    private readonly SecurityActivityRepository securityActivityRepository;
    private readonly AccountManagementRepository accountManagementRepository;

    public RepositoryTestFixture(
        IDatabaseConnectionFactory connections,
        IDatabaseDialect dialect,
        CaseServicePolicy servicePolicy,
        ISecretProtector secretProtector,
        IOtpHasher otpHasher,
        SessionPolicy? sessionPolicy = null)
    {
        var profile = new ProfileRepository(
            connections,
            dialect,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            secretProtector,
            otpHasher);
        var identity = new IdentityVerificationRepository(
            connections,
            dialect,
            secretProtector,
            otpHasher,
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        authenticationRepository = new AuthenticationRepository(connections, dialect, sessionPolicy);
        bankingRepository = new LegacyBankingFixture(connections, dialect, secretProtector, otpHasher);
        disputeOperationsRepository = new DisputeOperationsRepository(connections, dialect, servicePolicy);
        disputeRiskRepository = new DisputeRiskRepository(
            connections,
            dialect,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            otpHasher,DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        disputeRepository = new DisputeRepository(
            connections,
            dialect,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            servicePolicy,
            disputeOperationsRepository,
            disputeRiskRepository,
            new PortalSettingsRepository(connections, dialect));
        notificationRepository = new NotificationRepository(connections, dialect, secretProtector, profile, identity, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        auditRepository = new AuditRepository(connections, dialect);
        userAdministrationRepository = new UserAdministrationRepository(connections, dialect);
        evidenceRepository = new EvidenceRepository(connections, dialect, secretProtector, new EvidencePolicyRepository(connections), DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        supportRepository = new SupportRepository(connections, dialect, TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"));
        profileRepository = profile;
        contactVerificationRepository = new ContactVerificationRepository(
            connections,
            dialect,
            secretProtector,
            otpHasher,
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        identityVerificationRepository = identity;
        securityActivityRepository = new SecurityActivityRepository(connections, dialect, sessionPolicy);
        accountManagementRepository = new AccountManagementRepository(
            connections,
            dialect,
            secretProtector,
            otpHasher,
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
    }

    public Task<AccountNumberDto> RevealAccountNumber(Guid user, Guid account, CancellationToken ct) =>
        accountManagementRepository.RevealAccountNumber(user, account, ct);

    public Task<DeletionChallengeDto> ReserveDeletionChallenge(Guid user, string destination, string code, CancellationToken ct) =>
        accountManagementRepository.ReserveDeletionChallenge(user, destination, code, ct);

    public Task SetDeletionChallengeReady(Guid user, Guid challenge, bool ready, CancellationToken ct) =>
        accountManagementRepository.SetDeletionChallengeReady(user, challenge, ready, ct);

    public Task<Guid> ConfirmDeletionRequest(Guid user, ConfirmDeletionRequest request, CancellationToken ct) =>
        accountManagementRepository.ConfirmDeletionRequest(user, request, ct);

    public Task<IReadOnlyList<AccountDeletionDto>> GetDeletionRequests(Guid actor, bool administration, CancellationToken ct) =>
        accountManagementRepository.GetDeletionRequests(actor, administration, ct);

    public Task CancelDeletionRequest(Guid user, Guid request, CancellationToken ct) => accountManagementRepository.CancelDeletionRequest(
        user,
        request,
        ct);

    public Task ReviewDeletionRequest(Guid actor, Guid request, ReviewDeletionRequest decision, CancellationToken ct) =>
        accountManagementRepository.ReviewDeletionRequest(actor, request, decision, ct);

    public Task<BankFeedBinding?> GetOwnedBinding(Guid customerId, Guid accountId, CancellationToken ct) =>
        bankingRepository.GetOwnedBinding(customerId, accountId, ct);

    public Task LinkOwnedAccount(Guid customerId, Guid accountId, string provider, string externalAccountId, CancellationToken ct) =>
        bankingRepository.LinkOwnedAccount(customerId, accountId, provider, externalAccountId, ct);

    public Task<int> ApplyPage(Guid customerId, BankFeedBinding expected, BankTransactionPage page, CancellationToken ct) =>
        bankingRepository.ApplyPage(customerId, expected, page, ct);

    public Task<IReadOnlyList<AccountDto>> GetAccounts(Guid customerId, CancellationToken cancellationToken) =>
        bankingRepository.GetAccounts(customerId, cancellationToken);

    public Task<AccountDto?> GetAccountByPublicId(Guid customerPublicId, Guid accountPublicId, CancellationToken cancellationToken) =>
        bankingRepository.GetAccountByPublicId(customerPublicId, accountPublicId, cancellationToken);

    public Task<PageResult<TransactionDto>> GetTransactions(Guid customerId, TransactionFilter filter, CancellationToken cancellationToken) =>
        bankingRepository.GetTransactions(customerId, filter, cancellationToken);

    public Task<TransactionDto?> GetTransaction(Guid customerId, Guid transactionId, CancellationToken cancellationToken) =>
        bankingRepository.GetTransaction(customerId, transactionId, cancellationToken);

    public Task<BankingProvisioningStatusDto> GetProvisioningStatus(Guid customer, CancellationToken ct) =>
        bankingRepository.GetProvisioningStatus(customer, ct);

    public Task<bool> EnsureFirstLoginAccounts(Guid customer, CancellationToken ct) => bankingRepository.EnsureFirstLoginAccounts(
        customer,
        ct);

    public Task<IReadOnlyList<BankingRegenerationTarget>> GetRegenerationTargets(Guid actor, CancellationToken ct) =>
        bankingRepository.GetRegenerationTargets(actor, ct);

    public Task<BankingRegenerationResult> RegenerateTransactions(Guid actor, RegenerateBankingRequest request, CancellationToken ct) =>
        bankingRepository.RegenerateTransactions(actor, request, ct);

    public Task<ProvisionedAccountCreatedDto> OpenAccount(
        Guid customer,
        Guid requestId,
        string accountType,
        int count,
        CancellationToken ct) => bankingRepository.OpenAccount(customer, requestId, accountType, count, ct);

    public Task<BankingBatchDto> QueueTransactions(Guid customer, Guid account, Guid requestId, int count, CancellationToken ct) =>
        bankingRepository.QueueTransactions(customer, account, requestId, count, ct);

    public Task<bool> ProcessNextTransactionBatch(CancellationToken ct) => bankingRepository.ProcessNextTransactionBatch(ct);

    public Task<IReadOnlyList<TransactionTypeDto>> GetTransactionTypes(CancellationToken ct) => bankingRepository.GetTransactionTypes(ct);

    public Task<ContactChallengeDto> ReserveContactChallenge(
        Guid user,
        string field,
        string destination,
        string channel,
        string code,
        CancellationToken ct) => contactVerificationRepository.ReserveContactChallenge(user, field, destination, channel, code, ct);

    public Task SetContactChallengeDelivery(Guid user, Guid challenge, bool delivered, CancellationToken ct) =>
        contactVerificationRepository.SetContactChallengeDelivery(user, challenge, delivered, ct);

    public Task CompleteContactChange(Guid user, Guid challenge, string code, CancellationToken ct) =>
        contactVerificationRepository.CompleteContactChange(user, challenge, code, ct);

    public Task<PasswordState?> GetPasswordState(Guid user, CancellationToken ct) => authenticationRepository.GetPasswordState(user, ct);

    public Task<LoginRecord?> FindLogin(string normalizedIdentifier, CancellationToken cancellationToken) =>
        authenticationRepository.FindLogin(normalizedIdentifier, cancellationToken);

    public Task<Guid> CreateSession(Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        authenticationRepository.CreateSession(userId, expiresAt, cancellationToken);

    public Task<bool> IsSessionActive(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        authenticationRepository.IsSessionActive(sessionId, now, cancellationToken);

    public Task RevokeSession(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        authenticationRepository.RevokeSession(sessionId, now, cancellationToken);

    public Task<CurrentUserDto?> GetCurrentUser(Guid userId, CancellationToken cancellationToken) =>
        authenticationRepository.GetCurrentUser(userId, cancellationToken);

    public Task<CreateDisputeResult> CreateDispute(
        Guid customerId,
        CreateDisputeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken) => disputeRepository.CreateDispute(customerId, command, idempotencyKey, cancellationToken);

    public Task<PageResult<DisputeSummaryDto>> GetCustomerDisputes(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        string? status = null,
        string? search = null,
        DisputePortal.Api.Models.Internal.HistoryFilter? filter = null) => disputeRepository.GetCustomerDisputes(
        customerId,
        page,
        pageSize,
        cancellationToken,
        accountId,
        status,
        search,
        filter);

    public Task<DisputeDetailDto?> GetCustomerDispute(Guid customerId, Guid disputeId, CancellationToken cancellationToken) =>
        disputeRepository.GetCustomerDispute(customerId, disputeId, cancellationToken);

    public Task<DashboardDto> GetDashboard(CancellationToken cancellationToken, Guid? actorId = null) => disputeRepository.GetDashboard(
        cancellationToken,
        actorId);

    public Task<PageResult<AdminQueueItemDto>> GetAdminQueue(
        AdminQueueFilter filter,
        CancellationToken cancellationToken,
        Guid? actorId = null) => disputeRepository.GetAdminQueue(filter, cancellationToken, actorId);

    public Task<AdminDisputeDto?> GetAdminDispute(Guid disputeId, CancellationToken cancellationToken, Guid? actorId = null) =>
        disputeRepository.GetAdminDispute(disputeId, cancellationToken, actorId);

    public Task<int> TransitionDispute(
        Guid actorId,
        Guid disputeId,
        TransitionCommand command,
        string correlationId,
        CancellationToken cancellationToken) => disputeRepository.TransitionDispute(
        actorId,
        disputeId,
        command,
        correlationId,
        cancellationToken);

    public Task<int> WithdrawCustomerDispute(
        Guid customerId,
        Guid disputeId,
        int expectedVersion,
        string correlationId,
        CancellationToken cancellationToken) => disputeRepository.WithdrawCustomerDispute(
        customerId,
        disputeId,
        expectedVersion,
        correlationId,
        cancellationToken);

    public Task<CreateDisputeResult> ReinstateCustomerDispute(
        Guid customerId,
        Guid disputeId,
        ReinstateDisputeRequest request,
        string correlationId,
        CancellationToken cancellationToken) => disputeRepository.ReinstateCustomerDispute(
        customerId,
        disputeId,
        request,
        correlationId,
        cancellationToken);

    public Task AddNote(Guid actorId, Guid disputeId, NoteCommand command, string correlationId, CancellationToken cancellationToken) =>
        disputeRepository.AddNote(actorId, disputeId, command, correlationId, cancellationToken);

    public Task UpdateCaseDeadlines(Guid actor, Guid id, UpdateCaseDeadlinesRequest request, CancellationToken ct) =>
        disputeOperationsRepository.UpdateCaseDeadlines(actor, id, request, ct);

    public Task<DisputeRiskPolicy> GetRiskPolicy(CancellationToken ct) => disputeRiskRepository.GetRiskPolicy(ct);

    public Task<PolicyDefaultsDto> GetPolicyDefaults(CancellationToken ct) => disputeRiskRepository.GetPolicyDefaults(ct);

    public Task<PolicyConfigurationDto> ResetPolicies(Guid actor, ResetDisputePoliciesRequest request, CancellationToken ct) =>
        disputeRiskRepository.ResetPolicies(actor, request, ct);

    public Task UpdateRiskPolicy(Guid actor, RiskPolicyUpdateRequest request, CancellationToken ct) =>
        disputeRiskRepository.UpdateRiskPolicy(actor, request, ct);

    public Task<DisputeIntakeDto> AssessDispute(Guid user, Guid transaction, CancellationToken ct) => disputeRiskRepository.AssessDispute(
        user,
        transaction,
        ct);

    public Task<IdentityChallengeDto> ReserveDisputeVerification(
        Guid user,
        Guid session,
        Guid transaction,
        string channel,
        string destination,
        string code,
        CancellationToken ct) => disputeRiskRepository.ReserveDisputeVerification(
        user,
        session,
        transaction,
        channel,
        destination,
        code,
        ct);

    public Task ReadyDisputeVerification(Guid user, Guid challenge, CancellationToken ct) =>
        disputeRiskRepository.ReadyDisputeVerification(user, challenge, ct);

    public Task CompleteDisputeVerification(Guid user, Guid session, Guid transaction, Guid challenge, string code, CancellationToken ct) =>
        disputeRiskRepository.CompleteDisputeVerification(user, session, transaction, challenge, code, ct);

    public Task Escalate(Guid actor, Guid dispute, EscalationRequest request, CancellationToken ct) =>
        disputeOperationsRepository.Escalate(actor, dispute, request, ct);

    public Task SetAvailability(Guid actor, string availability, CancellationToken ct) => disputeOperationsRepository.SetAvailability(
        actor,
        availability,
        ct);

    public Task<IReadOnlyList<CaseOperationsDto>> GetCases(Guid actor, CancellationToken ct) => disputeOperationsRepository.GetCases(
        actor,
        ct);

    public Task<CaseOperationsDto> GetCase(Guid actor, Guid id, CancellationToken ct) =>
        disputeOperationsRepository.GetCase(actor, id, ct);

    public Task ClaimCase(Guid actor, Guid id, ClaimCaseRequest request, CancellationToken ct) => disputeOperationsRepository.ClaimCase(
        actor,
        id,
        request,
        ct);

    public Task Assign(Guid actor, Guid id, AssignmentRequest request, CancellationToken ct) => disputeOperationsRepository.Assign(
        actor,
        id,
        request,
        ct);

    public Task Triage(Guid actor, Guid id, TriageRequest request, CancellationToken ct) => disputeOperationsRepository.Triage(
        actor,
        id,
        request,
        ct);

    public Task AssignWaitingCases(CancellationToken ct) => disputeOperationsRepository.AssignWaitingCases(ct);

    public Task<PageResult<NotificationDto>> GetNotifications(Guid customerId, int page, int pageSize, CancellationToken cancellationToken) =>
        notificationRepository.GetNotifications(customerId, page, pageSize, cancellationToken);

    public Task<bool> MarkNotificationRead(Guid customerId, Guid notificationId, CancellationToken cancellationToken) =>
        notificationRepository.MarkNotificationRead(customerId, notificationId, cancellationToken);

    public Task<string> GetNotificationPreference(Guid user, CancellationToken ct) => notificationRepository.GetNotificationPreference(
        user,
        ct);

    public Task SaveNotificationPreference(Guid user, string channel, CancellationToken ct) =>
        notificationRepository.SaveNotificationPreference(user, channel, ct);

    public Task SavePushSubscription(Guid user, Guid session, PushSubscriptionRequest subscription, CancellationToken ct) =>
        notificationRepository.SavePushSubscription(user, session, subscription, ct);

    public Task RemovePushSubscriptions(Guid user, Guid session, CancellationToken ct) => notificationRepository.RemovePushSubscriptions(
        user,
        session,
        ct);

    public Task DeletePushSubscription(Guid id, CancellationToken ct) => notificationRepository.DeletePushSubscription(id, ct);

    public Task<IReadOnlyList<StoredPushSubscription>> GetPushSubscriptions(Guid user, CancellationToken ct, Guid? session = null) =>
        notificationRepository.GetPushSubscriptions(user, ct, session);

    public Task<IReadOnlyList<NotificationDelivery>> ClaimNotifications(CancellationToken ct) =>
        notificationRepository.ClaimNotifications(ct);

    public Task<NotificationTarget> GetNotificationTarget(Guid user, string channel, CancellationToken ct) =>
        notificationRepository.GetNotificationTarget(user, channel, ct);

    public Task CompleteNotification(Guid id, string outcome, string? error, CancellationToken ct) =>
        notificationRepository.CompleteNotification(id, outcome, error, ct);

    public Task<PageResult<AuditRecordDto>> GetAuditLogs(int page, int pageSize, string? action, CancellationToken cancellationToken) =>
        auditRepository.GetAuditLogs(page, pageSize, action, cancellationToken);

    public Task RemoveEvidence(Guid actor, Guid disputeId, Guid evidenceId, RemoveEvidenceRequest request, CancellationToken ct) =>
        evidenceRepository.RemoveEvidence(actor, disputeId, evidenceId, request, ct);

    public Task EnsureEvidenceUploadAllowed(Guid actor, Guid disputeId, CancellationToken ct) =>
        evidenceRepository.EnsureEvidenceUploadAllowed(actor, disputeId, ct);

    public Task<IReadOnlyList<EvidenceDto>> GetEvidence(Guid actor, Guid disputeId, CancellationToken ct) =>
        evidenceRepository.GetEvidence(actor, disputeId, ct);

    public Task<Guid> AddEvidence(
        Guid actor,
        Guid disputeId,
        string fileName,
        byte[] content,
        string scanStatus,
        string category,
        CancellationToken ct,
        EvidenceOriginal? original = null) => evidenceRepository.AddEvidence(
        actor,
        disputeId,
        fileName,
        content,
        scanStatus,
        category,
        ct,
        original);

    public Task<EvidenceDownload> DownloadEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct) =>
        evidenceRepository.DownloadEvidence(actor, disputeId, evidenceId, ct);

    public Task<EvidenceDownload> DownloadOriginalEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct) =>
        evidenceRepository.DownloadOriginalEvidence(actor, disputeId, evidenceId, ct);

    public Task<EvidenceDownload> PreviewEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct) =>
        evidenceRepository.PreviewEvidence(actor, disputeId, evidenceId, ct);

    public Task<EvidenceAccessDto> GetEvidenceAccess(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct) =>
        evidenceRepository.GetEvidenceAccess(actor, disputeId, evidenceId, ct);

    public Task GrantEvidenceAccess(Guid actor, Guid disputeId, Guid evidenceId, GrantEvidenceAccessRequest request, CancellationToken ct) =>
        evidenceRepository.GrantEvidenceAccess(actor, disputeId, evidenceId, request, ct);

    public Task RevokeEvidenceAccess(
        Guid actor,
        Guid disputeId,
        Guid evidenceId,
        Guid grantId,
        RevokeEvidenceAccessRequest request,
        CancellationToken ct) => evidenceRepository.RevokeEvidenceAccess(actor, disputeId, evidenceId, grantId, request, ct);

    public Task<VerifiedIdentity?> FindIdentity(string contact, CancellationToken ct) => identityVerificationRepository.FindIdentity(
        contact,
        ct);

    public Task<bool> RegistrationContactExists(string contact, CancellationToken ct) =>
        identityVerificationRepository.RegistrationContactExists(contact, ct);

    public Task<IdentityChallengeDto> CreateIdentityChallenge(
        Guid? user,
        string purpose,
        string channel,
        string destination,
        string payload,
        string code,
        CancellationToken ct) => identityVerificationRepository.CreateIdentityChallenge(
        user,
        purpose,
        channel,
        destination,
        payload,
        code,
        ct);

    public Task MarkIdentityChallengeReady(Guid challenge, CancellationToken ct) =>
        identityVerificationRepository.MarkIdentityChallengeReady(challenge, ct);

    public Task<Guid> RegisterWithoutContactVerification(DisputePortal.Api.Models.Internal.RegistrationPayload registration, CancellationToken ct) =>
        identityVerificationRepository.RegisterWithoutContactVerification(registration, ct);

    public Task<Guid> CompleteRegistration(Guid challenge, string code, CancellationToken ct) =>
        identityVerificationRepository.CompleteRegistration(challenge, code, ct);

    public Task<Guid> CompleteLogin(Guid challenge, string code, CancellationToken ct) => identityVerificationRepository.CompleteLogin(
        challenge,
        code,
        ct);

    public Task CompletePasswordReset(
        Guid challenge,
        string code,
        string passwordHash,
        CancellationToken ct,
        Func<string, bool>? matchesCurrentPassword = null) => identityVerificationRepository.CompletePasswordReset(
        challenge,
        code,
        passwordHash,
        ct,
        matchesCurrentPassword);

    public Task<IReadOnlyList<StaffUserDto>> GetStaff(Guid actor, bool administration, CancellationToken ct) =>
        userAdministrationRepository.GetStaff(actor, administration, ct);

    public Task<Guid> CreateUser(Guid actor, SaveUserRequest request, string passwordHash, CancellationToken ct) =>
        userAdministrationRepository.CreateUser(actor, request, passwordHash, ct);

    public Task UpdateUser(Guid actor, Guid id, UpdateUserRequest request, CancellationToken ct) =>
        userAdministrationRepository.UpdateUser(actor, id, request, ct);

    public Task<ProfileDto> GetProfile(Guid user, CancellationToken ct) => profileRepository.GetProfile(user, ct);

    public Task<string> RevealIdentityNumber(Guid user, CancellationToken ct) => profileRepository.RevealIdentityNumber(user, ct);

    public Task UpdateProfileDetails(Guid user, ProfileDetailsRequest request, CancellationToken ct) =>
        profileRepository.UpdateProfileDetails(user, request, ct);

    public Task SetIdentity(Guid user, string identityNumber, int birthCentury, CancellationToken ct) => profileRepository.SetIdentity(
        user,
        identityNumber,
        birthCentury,
        ct);

    public Task<SignInDetailDto?> GetSignInDetail(Guid user, Guid eventId, CancellationToken ct) =>
        securityActivityRepository.GetSignInDetail(user, eventId, ct);

    public Task<IReadOnlyList<SecurityActivityDto>> GetRecentSignIns(Guid user, CancellationToken ct) =>
        securityActivityRepository.GetRecentSignIns(user, ct);

    public Task RecordSecurityEvent(
        Guid? user,
        string eventType,
        string outcome,
        string ip,
        string userAgent,
        Guid? session,
        CancellationToken ct) => securityActivityRepository.RecordSecurityEvent(user, eventType, outcome, ip, userAgent, session, ct);

    public Task<IReadOnlyList<SecurityActivityDto>> GetSecurityActivity(Guid? user, int page, int pageSize, CancellationToken ct) =>
        securityActivityRepository.GetSecurityActivity(user, page, pageSize, ct);

    public Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessions(Guid user, CancellationToken ct) =>
        securityActivityRepository.GetActiveSessions(user, ct);

    public Task RevokeOwnedSession(Guid user, Guid session, CancellationToken ct) => securityActivityRepository.RevokeOwnedSession(
        user,
        session,
        ct);

    public Task<PageResult<SupportCaseOptionDto>> GetRelatedCases(
        Guid actor,
        int page,
        int pageSize,
        string? reference,
        CancellationToken ct) => supportRepository.GetRelatedCases(actor, page, pageSize, reference, ct);

    Task<PageResult<SupportThreadDto>> ISupportRepository.GetThreads(
        Guid actor,
        CancellationToken ct,
        Guid? disputeId,
        string? search,
        Guid? transactionId,
        DisputePortal.Api.Models.Internal.HistoryFilter? filter,
        Guid? relatedDisputeId,
        int page,
        int pageSize,
        bool assignedOnly,
        bool ownOnly) => supportRepository.GetThreads(
        actor,
        ct,
        disputeId,
        search,
        transactionId,
        filter,
        relatedDisputeId,
        page,
        pageSize,
        assignedOnly,
        ownOnly);

    public async Task<IReadOnlyList<SupportThreadDto>> GetThreads(
        Guid actor,
        CancellationToken ct,
        Guid? disputeId = null,
        string? search = null,
        Guid? transactionId = null,
        DisputePortal.Api.Models.Internal.HistoryFilter? filter = null,
        Guid? relatedDisputeId = null) => (await supportRepository.GetThreads(
        actor,
        ct,
        disputeId,
        search,
        transactionId,
        filter,
        relatedDisputeId)).Items;

    public Task<IReadOnlyList<SupportMessageDto>> GetMessages(
        Guid actor,
        Guid thread,
        CancellationToken ct,
        long? afterSequence = null,
        long? beforeSequence = null) => supportRepository.GetMessages(actor, thread, ct, afterSequence, beforeSequence);

    public Task<SupportMessagePageDto> GetMessagePage(Guid actor, Guid thread, CancellationToken ct, long? afterSequence = null, long? beforeSequence = null) =>
        supportRepository.GetMessagePage(actor, thread, ct, afterSequence, beforeSequence);

    public Task<IReadOnlyList<SupportConcernDto>> GetConcerns(CancellationToken ct) => supportRepository.GetConcerns(ct);

    public Task<SupportThreadDto> CloseConversation(Guid actor, Guid thread, CloseSupportRequest request, CancellationToken ct) =>
        supportRepository.CloseConversation(actor, thread, request, ct);

    public Task<SupportThreadDto> AssignConversation(Guid actor, Guid thread, AssignmentRequest request, CancellationToken ct) =>
        supportRepository.AssignConversation(actor, thread, request, ct);

    public Task<SupportThreadDto> EscalateConversation(Guid actor, Guid thread, EscalationRequest request, CancellationToken ct) =>
        supportRepository.EscalateConversation(actor, thread, request, ct);

    public Task AssignWaitingSupportConversations(CancellationToken ct) => supportRepository.AssignWaitingSupportConversations(ct);

    public Task<Guid> SendMessage(Guid actor, SupportSendRequest request, CancellationToken ct) => supportRepository.SendMessage(
        actor,
        request,
        ct);

    public Task<DisputePortal.Api.Models.Responses.SupportThreadDto?> GetSupportThread(Guid actor, Guid thread, CancellationToken ct) =>
        supportRepository.GetSupportThread(actor, thread, ct);

    public Task<Guid?> GetLastSupportThread(Guid actor, CancellationToken ct) => supportRepository.GetLastSupportThread(actor, ct);

    public Task MarkSupportRead(Guid actor, Guid thread, Guid message, CancellationToken ct) => supportRepository.MarkSupportRead(
        actor,
        thread,
        message,
        ct);
}
