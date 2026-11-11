export interface ActionActor {
  userId?: string | null
  firstName: string | null
  middleName: string | null
  lastName: string | null
  fullName: string | null
  accountName: string | null
  roles: string[]
  kind: 'User' | 'System' | 'Anonymous'
  provenance?: 'Recorded' | 'LegacyProfile' | 'ErasedProfile' | null
  loginIdentifier?: string | null
  label: string
}
import type { CaseOperations } from './operations'

export interface SessionUser {
  id: string
  displayName: string
  maskedEmail?: string
  roles: string[]
  permissions: string[]
}

export interface Account {
  id: string
  registeredAt: string
  accountType: 'Cheque' | 'Savings' | 'Credit'
  currency: string
  maskedAccountNumber: string
  accountNumber?: string | null
  status: string
}

export interface Transaction {
  accountType?: string | null
  id: string
  accountId: string
  accountNumber: string
  reference: string | null
  typeLabel?: string | null
  transactionAt: string
  description: string
  merchantName: string
  amount: number
  currency: string
  type: string
  direction: 'Debit' | 'Credit'
  status: string
  disputeEligible: boolean
  disputeStatus?: string | null
  hasActiveDispute: boolean
  disputeId?: string | null
}

export interface DisputeIntake {
  allowed: boolean
  requiresVerification: boolean
  assistedReview: boolean
  ageDays: number
  policyVersion: number
  message: string
  canStart: boolean
  activeDisputeId: string | null
  blockReason: string | null
  hasPendingFinancialOperation: boolean
  hasPostedRefund: boolean
}

export interface PageResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface DisputeSummary {
  transactionType?: string | null
  transactionTypeLabel?: string | null
  id: string
  reference: string
  transactionId: string
  transactionDescription: string
  amount: number
  currency: string
  category: string
  status: string
  priority: number
  version: number
  createdAt: string
  resolution?: string | null
}

export interface StatusHistory {
  actorIdentity?: ActionActor | null
  previousStatus?: string | null
  newStatus: string
  actorName?: string | null
  changedAt: string
  comment?: string | null
}

export interface DisputeNote {
  actorIdentity?: ActionActor | null
  authorName: string
  content: string
  visibility: 'Customer' | 'Internal'
  createdAt: string
}

export interface DisputeDetail {
  dispute: DisputeSummary
  resolvedBy?: ActionActor | null
  transactionReference: string | null
  merchantName: string
  transactionAt: string
  history: StatusHistory[]
  notes: DisputeNote[]
}

export interface AdminQueueItem {
  operations?: CaseOperations
  dispute: DisputeSummary
  customerName: string
  maskedEmail: string
  accountNumber: string
  transactionReference: string
  merchantName: string
  transactionAt: string
}

export interface AdminDispute extends DisputeDetail {
  operations?: CaseOperations
  customerName: string
  maskedEmail: string
  accountNumber: string
}

export interface PortalNotification {
  url?: string | null
  id: string
  disputeId?: string | null
  type: string
  title: string
  body: string
  isRead: boolean
  createdAt: string
}

export interface DashboardMetrics {
  submitted: number
  underReview: number
  awaitingCustomer: number
  resolvedToday: number
  highPriority: number
}

export interface ApiProblem {
  code?: string
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}

export interface DisputeInputPolicy {
  reasonMaximumLength: number
  descriptionMinimumLength: number
  descriptionMaximumLength: number
  commentMaximumLength: number
  noteMinimumLength: number
  noteMaximumLength: number
  statusMaximumLength: number
  idempotencyKeyMinimumLength: number
  idempotencyKeyMaximumLength: number
  categories: {
    code: string
    label: string
    reason: string
    transactionType: string | null
    transactionTypes: string[]
    sortOrder: number
  }[]
}
