import type { Page } from '@playwright/test'
import reference from './portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './platformSettings'

export const screenIds = { account: 'account', transaction: 'payment', dispute: 'case' }
const now = '2026-11-09T10:00:00Z'
const future = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()
const actor = {
  id: 'customer',
  displayName: 'Alex Customer',
  roles: ['customer'],
  permissions: [] as string[]
}
export const screenTransaction = {
  id: 'payment',
  accountId: 'account',
  accountNumber: '1234564821',
  accountType: 'Cheque',
  customerId: actor.id,
  customerName: actor.displayName,
  email: 'alex@example.test',
  reference: 'BANK-reference',
  transactionAt: now,
  description: 'Card Purchase · City Fuel',
  merchantName: 'City Fuel',
  amount: 249.99,
  currency: 'ZAR',
  type: 'CardPurchase',
  typeLabel: 'Card Purchase',
  direction: 'Debit',
  status: 'Posted',
  disputeEligible: true,
  hasActiveDispute: false,
  disputeId: null
}
const account = {
  id: 'account',
  userId: actor.id,
  accountType: 'Cheque',
  type: 'Cheque',
  countryCode: 'ZA',
  currency: 'ZAR',
  number: '1234564821',
  accountNumber: '1234564821',
  maskedAccountNumber: '••••4821',
  status: 'Active',
  registeredAt: now,
  createdAt: now,
  ledgerBalance: 1000,
  availableBalance: 1000,
  reservedBalance: 0,
  version: 1
}
const savings = {
  ...account,
  id: 'savings',
  type: 'Savings',
  accountType: 'Savings',
  number: '1234567103'
}
const dispute = {
  id: 'case',
  reference: 'DP-2026-123456',
  transactionId: 'payment',
  transactionDescription: screenTransaction.description,
  transactionType: 'CardPurchase',
  transactionTypeLabel: 'Card Purchase',
  merchantName: 'City Fuel',
  amount: 249.99,
  currency: 'ZAR',
  category: 'Duplicate',
  reason: 'I was charged more than once',
  description: 'The same payment appears twice on my statement.',
  status: 'Submitted',
  priority: 2,
  version: 1,
  createdAt: now,
  resolution: null
}
const staff = {
  id: 'specialist',
  displayName: 'Sam Specialist',
  firstName: 'Sam',
  lastName: 'Specialist',
  email: 'sam@example.test',
  role: 'card_specialist',
  status: 'Active',
  availability: 'Available',
  capacity: 5,
  activeCases: 1
}
const operations = {
  id: 'case',
  reference: dispute.reference,
  status: 'Submitted',
  queueType: 'Card',
  severity: 'Medium',
  priority: 2,
  version: 1,
  assignedTo: 'customer',
  assignedName: 'Alex Administrator',
  createdAt: now,
  assignedAt: now,
  responseDueAt: future,
  dueAt: future,
  firstResponseAt: null,
  slaClosedAt: null,
  escalationLevel: 0,
  escalatedAt: null,
  actions: {
    isClosed: false,
    canStartConversation: true,
    canManage: true,
    canAssign: true,
    canEscalate: true,
    canAddNote: true,
    canWithdraw: true,
    canTakeOwnership: true,
    allowedTransitions: ['UnderReview', 'AwaitingCustomer', 'Resolved'],
    claim: { allowed: true, state: 'ready', label: 'Take This Case', reason: '' },
    assignees: [{ ...staff, eligible: true, unavailableReason: '' }]
  }
}
const detail = {
  dispute,
  operations,
  transactionReference: screenTransaction.reference,
  merchantName: 'City Fuel',
  transactionAt: now,
  customerName: actor.displayName,
  maskedEmail: 'a•••@example.test',
  accountNumber: '1234564821',
  history: [
    {
      newStatus: 'Submitted',
      changedAt: now,
      actorName: 'Alex Customer',
      comment: 'Dispute submitted.'
    }
  ],
  notes: []
}
const profile = {
  ...actor,
  firstName: 'Alex',
  middleName: null,
  lastName: 'Customer',
  email: 'alex@example.test',
  phone: '+27821234567',
  registeredAt: now,
  address: null,
  residenceCountry: 'ZA',
  identityIssuerCountry: 'ZA',
  identityDocumentType: 'Passport',
  maskedIdentityNumber: '••••4821',
  dateOfBirth: '1995-01-15',
  status: 'Active'
}
const policy = {
  policyPublicId: 'policy',
  versionPublicId: 'publication',
  version: 1,
  concurrencyToken: 1,
  priority: 1,
  effectiveFromUtc: now,
  effectiveToUtc: null,
  createdAtUtc: now,
  createdBy: 'customer',
  createdByName: 'Alex Administrator',
  changeReason: 'Initial configuration',
  isDeleted: false,
  deletedAtUtc: null,
  deletedByName: null,
  deletionReason: null,
  provenance: 'Published',
  state: 'Effective',
  enabled: true,
  maximumAmount: 250,
  maximumAgeHours: 48,
  duplicateWindowMinutes: 10,
  dailyLimit: 600,
  highValueThreshold: 5000,
  extraCheckAfterDays: 30,
  debitOrderWindowDays: 40,
  debitOrderAppLimit: 250
}
const resolution = {
  selected: policy,
  evaluationTimeUtc: now,
  nextChangeAtUtc: null,
  selectionReason: 'Current Publication'
}
const pageOf = (items: unknown[]) => ({ items, total: items.length, page: 1, pageSize: 20 })
export const disputeInputFixture = {
  reasonMaximumLength: 120,
  descriptionMinimumLength: 10,
  descriptionMaximumLength: 2000,
  commentMaximumLength: 1000,
  noteMinimumLength: 1,
  noteMaximumLength: 4000,
  statusMaximumLength: 32,
  idempotencyKeyMinimumLength: 8,
  idempotencyKeyMaximumLength: 128,
  categories: [
    {
      code: 'Unrecognized',
      label: 'I don’t recognise this transaction',
      reason: 'I did not make this transaction'
    },
    {
      code: 'Duplicate',
      label: 'I was charged more than once',
      reason: 'I was charged more than once'
    },
    {
      code: 'IncorrectAmount',
      label: 'The amount looks incorrect',
      reason: 'The amount is incorrect'
    },
    {
      code: 'GoodsOrServices',
      label: 'I did not receive the goods or service',
      reason: 'I did not receive the goods or service'
    },
    {
      code: 'DebitOrder',
      label: 'I cancelled this debit order',
      reason: 'I cancelled this debit order'
    },
    { code: 'Other', label: 'Something else', reason: 'Other' }
  ].map((row, sortOrder) => ({
    ...row,
    sortOrder,
    transactionType: row.code === 'DebitOrder' ? 'DebitOrder' : null,
    transactionTypes:
      row.code === 'DebitOrder'
        ? ['DebitOrder']
        : ['CardPurchase', 'Card', 'CreditCardPurchase', 'DebitOrder']
  }))
}

export async function mockScreens(
  page: Page,
  mode: 'customer' | 'staff' | 'anonymous' = 'customer'
) {
  const unknown = new Set<string>()
  const requests: { path: string; body: Record<string, unknown> }[] = []
  let evidenceFiles = [
    {
      id: 'evidence',
      category: 'PhotoOrScreenshot',
      fileName: 'Payment receipt.png',
      contentType: 'image/png',
      sizeBytes: 2048,
      uploadedBy: 'Alex Customer',
      createdAt: now,
      canView: true,
      canManageAccess: mode === 'staff',
      canRemove: mode === 'staff',
      canRemoveOwn: mode === 'customer'
    }
  ]
  const user =
    mode === 'staff'
      ? {
          ...actor,
          displayName: 'Alex Administrator',
          roles: ['customer', 'super_admin'],
          permissions: [
            'disputes.read',
            'disputes.manage',
            'transactions.assist',
            'operations.manage',
            'users.manage',
            'audit.read'
          ]
        }
      : actor
  const settings = platformSettings()
  const messages = [
    {
      id: 'message-1',
      sequence: 1,
      authorId: 'specialist',
      authorName: staff.displayName,
      authorRole: 'card_specialist',
      body: 'Your message is with our specialist.\nWe will review your payment.',
      createdAt: now
    }
  ]
  const thread = {
    id: 'open-thread',
    customerId: 'customer',
    customerName: actor.displayName,
    disputeId: null,
    relatedDisputeId: 'case',
    reference: dispute.reference,
    subject: 'A question about my payment',
    transactionId: 'payment',
    amount: 249.99,
    currency: 'ZAR',
    accountNumber: '••••4821',
    createdAt: now,
    lastMessageAt: now,
    status: 'Open',
    version: 1,
    unreadCount: 1,
    lastReadSequence: 0,
    concernLabel: 'Payment Question',
    canReply: true,
    canClose: true,
    canAssign: mode === 'staff',
    canEscalate: mode === 'staff'
  }
  await page.routeWebSocket('**/api/v1/realtime**', (socket) => socket.close())
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname
    const data = request.method() === 'POST' ? (request.postDataJSON() ?? {}) : {}
    if (request.method() === 'POST') requests.push({ path, body: data })
    const reply = (json: unknown, headers?: Record<string, string>) =>
      route.fulfill({ json, headers })
    if (path === '/api/v1/portal/reference') return reply(reference)
    if (path === '/api/v1/auth/me')
      return mode === 'anonymous'
        ? route.fulfill({ status: 401, json: { title: 'Sign In Required' } })
        : reply(user)
    if (path === '/api/v1/auth/csrf') return reply({ requestToken: 'isolated-browser-fixture' })
    if (path === '/api/v1/platform/status') return reply({ enabled: true })
    if (path === '/api/v1/platform/settings') return reply(settings)
    if (path === '/api/v1/dispute-input') return reply(disputeInputFixture)
    if (path === '/api/v1/transactions/types')
      return reply([
        { typeKey: 'CardPurchase', label: 'Card Purchase' },
        { typeKey: 'DebitOrder', label: 'Debit Order' }
      ])
    if (path.endsWith('/dispute-check'))
      return reply({
        allowed: true,
        canStart: true,
        requiresVerification: false,
        assistedReview: false,
        policyVersion: 1,
        ageDays: 0,
        message: '',
        activeDisputeId: null,
        blockReason: null,
        hasPendingFinancialOperation: false,
        hasPostedRefund: false
      })
    if (path === '/api/v1/transactions/payment') return reply(screenTransaction)
    if (path === '/api/v1/transactions') return reply(pageOf([screenTransaction]))
    if (path === '/api/v1/accounts') return reply([account, savings])
    if (path === '/api/v1/accounts/account') return reply(account)
    if (path.endsWith('/connection'))
      return reply({ connected: false, canRefresh: false, lastSyncedAt: null })
    if (path === '/api/v1/platform/accounts') return reply([account, savings])
    if (path.endsWith('/transfer-targets')) return reply([savings])
    if (path.endsWith('/payments/quote'))
      return reply({
        id: 'quote',
        kind: 'Transfer',
        accountId: 'account',
        recipientAccountId: 'savings',
        amount: 10,
        amountExact: '10.00',
        fee: 0,
        feeExact: '0.00',
        totalDebitExact: '10.00',
        currency: 'ZAR',
        expiresAt: future
      })
    if (path.endsWith('/platform/payments')) return reply({ status: 'Processing' })
    if (
      path === '/api/v1/platform/payments' ||
      path.endsWith('/platform/accounts/account/transactions')
    )
      return reply([])
    if (path === '/api/v1/disputes/case') return reply(detail)
    if (path === '/api/v1/disputes' && request.method() === 'POST')
      return reply({ id: 'case', reference: dispute.reference })
    if (path === '/api/v1/disputes') return reply(pageOf([dispute]))
    if (path === '/api/v1/operations/cases/case') return reply(operations)
    if (path === '/api/v1/operations/me')
      return reply({ ...staff, id: user.id, role: mode === 'staff' ? 'super_admin' : 'customer' })
    if (path === '/api/v1/operations/users')
      return reply([
        staff,
        { ...staff, id: user.id, role: 'super_admin', displayName: user.displayName }
      ])
    if (path === '/api/v1/admin/disputes/case') return reply(detail)
    if (path === '/api/v1/admin/disputes') return reply(pageOf([{ ...detail, dispute }]))
    if (path === '/api/v1/admin/transactions/payment')
      return reply({
        transaction: screenTransaction,
        customer: profile,
        fullAccountNumber: account.number,
        accountStatus: 'Active'
      })
    if (path === '/api/v1/admin/transactions') return reply(pageOf([screenTransaction]))
    if (path.endsWith('/evidence')) return reply(evidenceFiles)
    if (path.endsWith('/evidence/evidence/remove')) {
      evidenceFiles = []
      return route.fulfill({ status: 204 })
    }
    if (path.endsWith('/evidence/evidence/access'))
      return reply({ grants: [], recipients: [staff] })
    if (path === '/api/v1/admin/dashboard')
      return reply({ submitted: 6, underReview: 4, awaitingCustomer: 2, resolved: 11 })
    if (path === '/api/v1/operations/staff')
      return reply([
        staff,
        {
          ...staff,
          id: user.id,
          displayName: user.displayName,
          role: 'super_admin',
          activeCases: 3
        }
      ])
    if (path === '/api/v1/finance/disputes/case')
      return reply({
        refund: null,
        decision: null,
        ruleCode: null,
        ruleVersion: null,
        canApproveRefund: mode === 'staff',
        canRecall: false,
        canReinstate: false
      })
    if (path.endsWith('/activity'))
      return reply([
        {
          id: 'activity',
          action: 'DisputeSubmitted',
          actor: 'Alex Customer',
          occurredAt: now,
          detail: 'Dispute submitted.'
        }
      ])
    if (path === '/api/v1/finance/overview')
      return reply({
        currency: 'ZAR',
        currentBalance: 2000,
        availableBalance: 2000,
        accountCount: 2,
        balanceKnown: true,
        accounts: [account, savings].map((a) => ({
          accountId: a.id,
          currency: a.currency,
          currentBalance: 1000,
          availableBalance: 1000,
          reservedAmount: 0,
          balanceKnown: true
        }))
      })
    if (path === '/api/v1/profile') return reply(profile)
    if (path.endsWith('/channels')) return reply({ channels: ['email', 'sms'] })
    if (path === '/api/v1/notification-settings')
      return reply({ channel: 'inapp', pushAvailable: false, pushSubscribed: false })
    if (path === '/api/v1/notifications')
      return reply(
        pageOf([
          {
            id: 'notification',
            type: 'DisputeUpdated',
            title: 'Dispute Updated',
            body: 'Your case is with a specialist.',
            createdAt: now,
            isRead: false,
            disputeId: 'case'
          }
        ])
      )
    if (path === '/api/v1/support/last-opened') return reply({ threadId: null })
    if (path === '/api/v1/support/concerns')
      return reply([
        {
          publicId: 'general',
          code: 'General',
          label: 'Something Else',
          contextKind: 'None',
          requiresCase: false,
          collectDate: false
        },
        {
          publicId: 'transaction',
          code: 'TransactionDispute',
          label: 'A Transaction',
          contextKind: 'Transaction',
          requiresCase: false,
          collectDate: true
        }
      ])
    if (path === '/api/v1/support' && request.method() === 'POST') {
      messages.push({
        id: 'sent-' + messages.length,
        sequence: messages.length + 1,
        authorId: user.id,
        authorName: user.displayName,
        authorRole: mode === 'staff' ? 'super_admin' : 'customer',
        body: data.body,
        createdAt: now
      })
      return reply({ id: 'open-thread' })
    }
    if (path === '/api/v1/support')
      return reply(
        [
          thread,
          { ...thread, id: 'closed-thread', status: 'Closed', canReply: false, canClose: false }
        ],
        { 'X-Total-Count': '2', 'X-Page': '1', 'X-Page-Size': '20' }
      )
    if (path.endsWith('/close')) {
      thread.status = 'Closed'
      thread.canReply = false
      thread.canClose = false
      return reply(thread)
    }
    if (path.endsWith('/messages') && request.method() === 'POST') {
      messages.push({
        id: 'sent',
        sequence: 2,
        authorId: user.id,
        authorName: user.displayName,
        authorRole: mode === 'staff' ? 'super_admin' : 'customer',
        body: data.body,
        createdAt: now
      })
      return reply({ id: 'sent' })
    }
    if (path.endsWith('/messages')) return reply(messages, { 'X-Has-More': 'false' })
    if (path === '/api/v1/support/open-thread') return reply(thread)
    if (path === '/api/v1/support/closed-thread')
      return reply({
        ...thread,
        id: 'closed-thread',
        status: 'Closed',
        canReply: false,
        canClose: false
      })
    if (path.endsWith('/read') || path.endsWith('/opened')) return route.fulfill({ status: 204 })
    if (path.includes('/admin/policies/'))
      return reply({ ...pageOf([policy]), concurrencyToken: 1, effective: resolution })
    if (path === '/api/v1/lookups/case-statuses')
      return reply({
        revision: 1,
        items: Object.entries(reference.statuses).map(([code, row], sortOrder) => ({
          code,
          sortOrder,
          displayName: row.customerLabel,
          ...row,
          workflowTargets: []
        }))
      })
    if (path === '/api/v1/platform/settings/history')
      return reply({
        ...pageOf([{ ...policy, settings }]),
        concurrencyToken: 1,
        effective: resolution
      })
    if (path === '/api/v1/platform/country-catalog')
      return reply([
        {
          code: 'ZA',
          name: 'South Africa',
          dialCode: '+27',
          locale: 'en-ZA',
          currency: 'ZAR',
          currencyName: 'South African Rand',
          currencySymbol: 'R',
          decimals: 2
        }
      ])
    if (path === '/api/v1/platform/reports')
      return reply({
        journals: 1,
        balances: [{ currency: 'ZAR', balance: 2000, accounts: 2 }],
        payments: []
      })
    if (path === '/api/v1/platform/reconciliation')
      return reply({ unbalancedJournals: 0, positionDifferences: [] })
    if (path === '/api/v1/admin/monitoring/overview')
      return reply({
        persisted: {
          from: now,
          to: now,
          requestCount: 1,
          failureCount: 0,
          clientErrorCount: 0,
          averageDurationMs: 15,
          lastPersistedAt: now,
          endpoints: [
            {
              method: 'GET',
              route: '/api/v1/transactions/{id}',
              statusCode: 200,
              requestCount: 1,
              failureCount: 0,
              clientErrorCount: 0,
              averageDurationMs: 15,
              minimumDurationMs: 15,
              maximumDurationMs: 15
            }
          ]
        },
        collection: { bufferedSeries: 0, droppedMeasurements: 0 }
      })
    if (path === '/api/v1/admin/audit-logs')
      return reply(
        pageOf([
          {
            id: 'audit',
            actorName: 'Alex Administrator',
            action: 'PolicyVersionPublished',
            entityType: 'Policy',
            occurredAt: now,
            safeMetadata: '{}',
            outcome: 'Success'
          }
        ])
      )
    if (
      path === '/api/v1/auth/devices' ||
      path.includes('remembered') ||
      path.endsWith('/security/settings')
    )
      return reply({
        canRememberDevice: true,
        requiresTwoStep: true,
        rememberedDeviceDays: 30,
        devices: [],
        canChangeVerification: true,
        loginOtpEnabled: true
      })
    if (request.method() === 'POST') return route.fulfill({ status: 204 })
    unknown.add(path)
    return reply([])
  })
  return { unknown, thread, messages, requests }
}
