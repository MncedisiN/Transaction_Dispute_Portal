import { reference } from '../portalReference'
import type { SupportConcern, SupportCaseOption } from '../operations'
import type { Transaction } from '../types'
import { isClosedStatus } from './casePresentation'

export interface EnquiryDraft {
  requestId: string
  concernCode: string
  relatedCase: SupportCaseOption | null
  transaction: Transaction | null
  description: string
  additionalDetails: string
  occurredOn: string
  additionalAnswered: boolean
  dateAnswered: boolean
  noTransaction: boolean
}
export function enquiryBody(draft: EnquiryDraft) {
  return (
    draft.description.trim() +
    (draft.additionalDetails.trim() ? '\n\n' + draft.additionalDetails.trim() : '')
  )
}
export function contextKind(concern: SupportConcern) {
  return concern.contextKind ?? (concern.requiresCase ? 'Case' : 'None')
}
export function enquiryProblem(draft: EnquiryDraft, concern: SupportConcern | undefined) {
  if (!reference.supportBody || !reference.supportSubject)
    return 'Enquiry settings are unavailable. Please try again.'
  if (!concern) return 'Choose a concern to continue.'
  if ((concern.requiresCase || contextKind(concern) === 'Case') && !draft.relatedCase)
    return 'Select the related dispute.'
  if (contextKind(concern) === 'Transaction' && !draft.transaction && !draft.relatedCase)
    return 'Select the related transaction.'
  if (draft.description.trim().length < reference.supportBody.minimum)
    return 'Tell us what you need help with.'
  if (enquiryBody(draft).length > (reference.supportBody.maximum ?? Infinity))
    return `Keep your complete enquiry within ${reference.supportBody.maximum?.toLocaleString()} characters.`
  return ''
}
export function enquirySubmission(draft: EnquiryDraft, concern: SupportConcern) {
  const problem = enquiryProblem(draft, concern)
  if (problem) throw new Error(problem)
  const isClosed = !!draft.relatedCase && isClosedStatus(draft.relatedCase.status)
  const subject = [
    concern.label,
    draft.relatedCase?.reference ?? draft.transaction?.merchantName ?? ''
  ]
    .filter(Boolean)
    .join(' · ')
    .slice(0, reference.supportSubject?.maximum ?? undefined)
  return {
    threadId: null,
    disputeId: draft.relatedCase && !isClosed ? draft.relatedCase.id : null,
    relatedDisputeId: isClosed ? draft.relatedCase!.id : null,
    transactionId: draft.relatedCase ? null : (draft.transaction?.id ?? null),
    subject,
    body: enquiryBody(draft),
    clientMessageId: draft.requestId,
    concernCode: concern.code,
    startNewConversation: isClosed,
    enquiry: {
      description: draft.description.trim(),
      additionalDetails: draft.additionalDetails.trim() || null,
      occurredOn: concern.collectDate ? draft.occurredOn || null : null
    }
  }
}
