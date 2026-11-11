import type { ActionActor } from './types'
export interface ClaimAction {
  allowed: boolean
  state: 'ready' | 'locked' | 'owned' | 'assigned' | 'closed'
  label: string
  reason: string
}
export interface AssigneeOption {
  id: string
  displayName: string
  role: string
  activeCases: number
  capacity: number
  eligible: boolean
  unavailableReason: string
}
export interface CaseActions {
  isClosed: boolean
  canStartConversation?: boolean
  canManage: boolean
  canAssign: boolean
  canEscalate: boolean
  canAddNote: boolean
  canWithdraw: boolean
  canTakeOwnership: boolean
  allowedTransitions: string[]
  claim: ClaimAction
  assignees: AssigneeOption[]
}
export interface CaseOperations {
  actions?: CaseActions
  escalationLevel: number
  escalatedAt: string | null
  id: string
  reference: string
  status: string
  queueType: 'General' | 'Fraud' | 'Card' | 'DebitOrder' | 'Payments'
  severity: string
  priority: number
  version: number
  assignedTo: string | null
  assignedName: string | null
  createdAt: string
  assignedAt: string | null
  responseDueAt: string | null
  dueAt: string | null
  firstResponseAt: string | null
  slaClosedAt: string | null
}
export interface StaffUser {
  firstName?: string | null
  middleName?: string | null
  lastName?: string | null
  id: string
  displayName: string
  email: string
  role: string
  status: string
  availability: string
  capacity: number
  activeCases: number
}
export interface SupportConcern {
  publicId: string
  code: string
  label: string
  requiresCase: boolean
  contextKind?: 'None' | 'Case' | 'Transaction' | 'OptionalTransaction'
  collectDate?: boolean
}
export interface SupportThread {
  status?: 'Open' | 'Closed'
  version?: number
  concernCode?: string
  concernLabel?: string
  queueType?: string | null
  assignedTo?: string | null
  closedAt?: string | null
  canReply?: boolean
  canClose?: boolean
  canAssign?: boolean
  canEscalate?: boolean
  relatedDisputeId?: string | null
  relatedCaseContext?: string | null
  transactionId?: string | null
  accountNumber?: string | null
  amount?: number | null
  currency?: string | null
  transactionAt?: string | null
  caseTitle?: string | null
  caseCreatedAt?: string | null
  caseStatus?: string | null
  unreadCount: number
  lastReadSequence: number
  lastMessageAt: string
  id: string
  customerId: string
  disputeId: string | null
  reference: string | null
  subject: string
  customerName: string
  createdAt: string
}
export interface EnquirySnapshot {
  concernCode: string
  concernLabel: string
  description: string
  additionalDetails: string | null
  occurredOn: string | null
}
export interface SupportMessage {
  enquiry?: EnquirySnapshot | null
  authorRole?: string
  actorIdentity?: ActionActor | null
  sequence: number
  id: string
  authorId: string
  authorName: string
  body: string
  createdAt: string
}
export interface SupportCaseOption {
  id: string
  reference: string
  status: string
  transactionDescription: string
  merchantName: string
  createdAt: string
}
export interface ResidentialAddress {
  line1: string
  line2: string | null
  suburb: string | null
  city: string
  province: string | null
  postalCode: string | null
  countryCode: string
}
export interface Profile {
  residenceCountry?: string | null
  identityDocumentType?: string | null
  identityIssuerCountry?: string | null
  middleName: string | null
  maskedIdentityNumber: string | null
  dateOfBirth: string | null
  firstName: string | null
  lastName: string | null
  address: ResidentialAddress | null
  id: string
  displayName: string
  email: string
  phone: string | null
  registeredAt: string
}
export { priorityLabels, caseTeams } from './portalReference'
