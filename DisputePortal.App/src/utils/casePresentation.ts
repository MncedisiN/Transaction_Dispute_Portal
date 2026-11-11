import { statusReferences, isClosedStatus } from '../portalReference'
export { isClosedStatus } from '../portalReference'
import { formatDate } from './format'
import type { DisputeSummary } from '../types'
import type { SupportThread } from '../operations'
export function caseNextStep(status: string) {
  return statusReferences[status]?.nextStep ?? ''
}
export function caseStage(status: string) {
  return statusReferences[status]?.stage ?? 0
}
export function conversationTitle(thread: SupportThread) {
  return thread.disputeId || thread.transactionId
    ? thread.caseTitle?.trim() || 'Transaction dispute'
    : thread.subject
}
export function caseDate(value: string) {
  return formatDate(value)
}
export function caseLabel(item: DisputeSummary) {
  return item.transactionDescription?.trim() || 'Transaction dispute'
}

export function caseOwnerLabel(status: string, assignedName?: string | null) {
  if (isClosedStatus(status)) return statusReferences[status]?.ownerLabel || 'Case Unavailable'
  return assignedName?.trim() || 'Awaiting Assignment'
}
