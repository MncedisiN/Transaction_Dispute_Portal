import { api } from './api'
export interface CaseStatus {
  code: string
  displayName: string
  customerLabel: string
  sortOrder: number
  nextStep: string
  closed: boolean
  workflowTargets: string[]
}
export interface CaseStatusCatalogue {
  revision: number
  items: CaseStatus[]
}
export function loadCaseStatuses(signal?: AbortSignal) {
  return api.get<CaseStatusCatalogue>('/api/v1/lookups/case-statuses', signal)
}
