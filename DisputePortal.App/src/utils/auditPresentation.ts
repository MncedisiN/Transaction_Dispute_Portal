const titles: Record<string, string> = {
  CaseStatusDescriptionsChanged: 'Workflow Descriptions Updated',
  PolicyVersionPublished: 'Policy Version Published',
  PolicyVersionSupersessionScheduled: 'Policy Supersession Scheduled',
  PolicyVersionCopy: 'Policy Copied As New Version',
  PolicyVersionRestore: 'Policy Restored As New Version',
  PolicyVersionDeleted: 'Policy Version Removed',
  PolicyHistoryMigrated: 'Policy History Migrated',
  SettingsVersionPublished: 'Configuration Version Published',
  SettingsVersionDeleted: 'Configuration Version Removed',
  SettingsHistoryMigrated: 'Configuration History Migrated',
  AccountNumberRevealed: 'Account details viewed',
  AccountDeletionRequested: 'Account deletion requested',
  AccountDeletionUnderReview: 'Account deletion review started',
  AccountDeletionCompleted: 'Account deletion completed',
  AccountDeletionDeclined: 'Account deletion declined',
  AccountDeletionCancelled: 'Account deletion cancelled',
  DemoBankAccountOpened: 'Account opened',
  DemoTransactionsQueued: 'Account activity queued',
  DemoTransactionsGenerated: 'Account activity updated',
  DisputeStatusChanged: 'Case status updated',
  CaseEscalated: 'Senior review requested',
  CaseWorkflowStarted: 'Case review started',
  AvailabilityChanged: 'Work status updated',
  CaseAssigned: 'Case assignment updated',
  CaseTriaged: 'Case priority updated',
  UserCreated: 'User account created',
  UserAccessChanged: 'User access updated',
  EvidenceAttached: 'Evidence added',
  EvidenceDownloadRequested: 'Evidence download requested',
  EvidencePreviewRequested: 'Evidence preview requested',
  EvidenceAccessGranted: 'Document access granted',
  EvidenceAccessRevoked: 'Document access revoked',
  DisputeRulesChanged: 'Dispute policy updated',
  DisputePolicyDefaultsRestored: 'Policy defaults restored',
  ProfileDetailsChanged: 'Profile updated',
  IdentityDetailsRecorded: 'Identity details saved',
  ContactVerified: 'Contact verified',
  PasswordReset: 'Password reset',
  CustomerRegistered: 'Customer registered',
  DisputeVerificationPassed: 'Security check passed',
  DisputeVerificationRejected: 'Security check declined',
  AuthenticatorEnabled: 'Sign-in protection updated'
}
export function auditTitle(action: string): string {
  return (
    (Object.hasOwn(titles, action) ? titles[action]! : undefined) ??
    action.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]/g, ' ')
  )
}
const fields: Record<string, string> = {
  version: 'Version',
  effectiveFromUtc: 'Effective From (UTC)',
  effectiveToUtc: 'Effective To (UTC)',
  operation: 'Operation',
  status: 'Status',
  previousStatus: 'Previous status',
  newStatus: 'New status',
  role: 'Role',
  previousRole: 'Previous role',
  newRole: 'New role',
  severity: 'Severity',
  priority: 'Priority',
  category: 'Document category',
  queueType: 'Team',
  channel: 'Channel',
  contentType: 'File type',
  sizeBytes: 'File size (bytes)',
  fileCount: 'Files',
  isActive: 'Account active',
  capacity: 'Case capacity',
  available: 'Available'
}
export function auditDetails(raw: string): { label: string; value: string }[] {
  // Never render arbitrary metadata: audit payloads can contain identifiers or personal information.
  if (!raw || raw.length > 16000) return []
  try {
    const parsed: unknown = JSON.parse(raw)
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return []
    return Object.entries(parsed).flatMap(([key, value]) => {
      const name = key.charAt(0).toLowerCase() + key.slice(1)
      if (!Object.hasOwn(fields, name) || !['string', 'number', 'boolean'].includes(typeof value))
        return []
      const display = typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value)
      if (display.length > 100 || /[{}<>@]/.test(display)) return []
      return [{ label: fields[name]!, value: display.replace(/([a-z])([A-Z])/g, '$1 $2') }]
    })
  } catch {
    return []
  }
}
