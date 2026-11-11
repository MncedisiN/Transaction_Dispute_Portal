export interface PolicyVersion {
  policyPublicId: string
  versionPublicId: string
  version: number
  concurrencyToken: number
  priority: number
  effectiveFromUtc: string
  effectiveToUtc: string | null
  createdAtUtc: string | null
  createdBy: string | null
  createdByName: string | null
  changeReason: string
  isDeleted: boolean
  deletedAtUtc: string | null
  deletedBy?: string | null
  deletedByName: string | null
  deletionReason: string | null
  provenance: string
  state: string
  enabled?: boolean
  maximumAmount?: number
  duplicateWindowMinutes?: number
  maximumAgeHours?: number
  dailyLimit?: number
  highValueThreshold?: number
  extraCheckAfterDays?: number
  debitOrderWindowDays?: number
  debitOrderAppLimit?: number
}
export interface PolicyResolution {
  selected: PolicyVersion | null
  evaluationTimeUtc: string
  nextChangeAtUtc: string | null
  selectionReason: string
  atAffectedBoundary?: PolicyResolution | null
}
export interface PolicyHistory {
  items: PolicyVersion[]
  total: number
  page: number
  pageSize: number
  concurrencyToken: number
  effective: PolicyResolution
}
export interface PolicyPreview {
  before: PolicyResolution
  after: PolicyResolution
  proposed: PolicyVersion
  atActivation: PolicyResolution
  consequence: string
}
export function policyBoundaryDelay(resolution: PolicyResolution, now = Date.now()): number | null {
  if (!resolution.nextChangeAtUtc) return null
  const remaining =
    Date.parse(resolution.nextChangeAtUtc) - Date.parse(resolution.evaluationTimeUtc)
  if (!Number.isFinite(remaining)) return null
  // Use the server's relative boundary, not the device's wall clock.
  void now
  return Math.min(2_147_000_000, Math.max(50, remaining + 50))
}
