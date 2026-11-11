import { computed } from 'vue'
import { reference } from '../portalReference'

export const evidenceCategories = computed(() => reference.evidence?.categories ?? [])
export const evidenceCategoryLabel = (value: string) =>
  evidenceCategories.value.find((item) => item.value === value)?.label ?? 'Supporting Evidence'
export interface EvidenceFile {
  id: string
  category: string
  fileName: string
  contentType: string
  sizeBytes: number
  originalFileName?: string
  hasOriginal?: boolean
  uploadedBy: string
  createdAt: string
  canView: boolean
  canManageAccess: boolean
  canRemove?: boolean
  canRemoveOwn?: boolean
  deletedAt?: string | null
  deletedById?: string | null
  deletedBy?: string | null
}
