import { reactive } from 'vue'
import { api } from './api'

export interface StatusReference {
  label: string
  customerLabel: string
  tone: string
  stage: number
  nextStep: string
  ownerLabel: string
  steps: string[]
  closed: boolean
  sortOrder?: number
}
export interface RoleReference {
  rank?: number
  label: string
  description: string
  assignable: boolean
  specialist: boolean
}
export interface QueueReference {
  label: string
  role: string
  steps: string[]
}
export interface FieldLimits {
  minimum: number
  maximum: number | null
}
export interface PortalReference {
  customerSearchEnabled: boolean
  customerAccountOpeningEnabled: boolean
  version?: number
  clientOptions: Record<string, number>
  enquiryFlow: {
    initialStep: string
    relatedCaseInitialStep: string
    reviewStep: string
    steps: {
      code: string
      kind: string
      phase: string
      prompt: string
      completionField?: string | null
      concernFlag?: string | null
    }[]
  }
  evidence: {
    categories: { value: string; label: string }[]
    maximumFiles: number
    maximumFileBytes: number
    maximumOriginalBytes: number
    maximumCaseBytes: number
    allowedExtensions: string[]
    uploadStatuses: string[]
  }
  statuses: Record<string, StatusReference>
  roles: Record<string, RoleReference>
  queues: Record<string, QueueReference>
  priorities: Record<string, string>
  severities: string[]
  transactionStatuses: Record<string, string>
  transactionTypes: Record<string, string>
  paymentStatuses: Record<string, string>
  availability: string[]
  staffStatuses: string[]
  supportBody: FieldLimits
  supportSubject: FieldLimits
  additionalDetails: FieldLimits
  financialReason: FieldLimits
  staffCapacity: FieldLimits
  evidenceReason: FieldLimits
  evidenceDurations: Record<string, string>
  evidenceDefaultHours: number
  assignmentReason: FieldLimits
  escalationReason: FieldLimits
  policyReason: FieldLimits
  password: FieldLimits
}
export const referenceState = reactive({ ready: false })
export const statusReferences = reactive<Record<string, StatusReference>>({})
export const userRoles = reactive<Record<string, RoleReference>>({})
export const caseTeams = reactive<Record<string, QueueReference>>({})
export const priorityLabels = reactive<Record<string, string>>({})
export const reference = reactive<Partial<PortalReference>>({})
let pending: Promise<void> | null = null

function replace<T>(target: Record<string, T>, source: Record<string, T>) {
  for (const key of Object.keys(target)) delete target[key]
  Object.assign(target, source)
}
export function applyPortalReference(value: PortalReference) {
  if (
    !value?.clientOptions ||
    !value?.enquiryFlow?.steps?.length ||
    !value?.statuses ||
    !value.roles ||
    !value.queues ||
    !value.priorities ||
    !value.assignmentReason ||
    !value.escalationReason ||
    !value.policyReason ||
    !value.password ||
    !value.supportBody ||
    !value.supportSubject ||
    !value.financialReason ||
    !value.staffCapacity ||
    !value.evidenceReason ||
    !value.evidenceDurations ||
    !value.severities?.length ||
    !value.evidence?.categories?.length ||
    !Number.isSafeInteger(value.evidence.maximumFiles) ||
    value.evidence.maximumFiles < 1 ||
    !(value.evidence.maximumFileBytes > 0) ||
    !(value.evidence.maximumOriginalBytes >= value.evidence.maximumFileBytes) ||
    !value.evidence.allowedExtensions?.length ||
    !Array.isArray(value.evidence.uploadStatuses)
  )
    throw new Error('Portal reference settings are unavailable.')
  replace(statusReferences, value.statuses)
  replace(userRoles, value.roles)
  replace(caseTeams, value.queues)
  replace(priorityLabels, value.priorities)
  Object.assign(reference, value)
  referenceState.ready = true
}
export async function loadPortalReference(refresh = false): Promise<void> {
  if (referenceState.ready && !refresh) return
  if (!pending)
    pending = api
      .get<PortalReference>('/api/v1/portal/reference')
      .then(applyPortalReference)
      .finally(() => {
        pending = null
      })
  await pending
}
export function isClosedStatus(status?: string | null): boolean {
  return !status || statusReferences[status]?.closed !== false
}
