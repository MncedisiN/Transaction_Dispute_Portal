import { nextTick, onUnmounted, ref, watch, type Ref } from 'vue'
import { api, ApiError } from '../api'
import { caseTeams, userRoles } from '../portalReference'
import type { StaffUser, SupportThread } from '../operations'

export function useConversationActions(
  metadata: Ref<SupportThread | null>,
  permitted: Ref<boolean>,
  error: Ref<string>,
  actionDialog: Readonly<Ref<HTMLDialogElement | null>>,
  changed: (accessLost: boolean) => void
) {
  const menuOpen = ref(false)
  const action = ref<'close' | 'escalate' | 'assign' | null>(null)
  const actionReason = ref('')
  const actionBusy = ref(false)
  const assignmentTarget = ref('')
  const assignmentChoices = ref<StaffUser[]>([])
  let generation = 0
  let disposed = false

  function cancelAction() {
    generation++
    actionDialog.value?.close()
    action.value = null
    actionBusy.value = false
  }

  watch(() => metadata.value?.id, cancelAction, { flush: 'sync' })
  onUnmounted(() => {
    disposed = true
    cancelAction()
  })

  async function startAction(value: 'close' | 'escalate' | 'assign') {
    const current = ++generation
    menuOpen.value = false
    action.value = value
    actionReason.value = ''
    await nextTick()
    if (disposed || current !== generation) return
    actionDialog.value?.showModal()
    if (value !== 'assign' || !metadata.value?.canAssign) return
    try {
      const staff = await api.get<StaffUser[]>('/api/v1/operations/staff')
      if (disposed || current !== generation) return
      const queueRole = caseTeams[metadata.value?.queueType || 'General']?.role
      assignmentChoices.value = staff.filter((person) => {
        const role = userRoles[person.role]
        return (
          person.status === 'Active' &&
          role?.assignable &&
          (!role.specialist || person.role === queueRole)
        )
      })
      assignmentTarget.value = metadata.value?.assignedTo || ''
    } catch {
      if (disposed || current !== generation) return
      error.value = 'Staff choices are unavailable. Please try again.'
      cancelAction()
    }
  }

  async function completeAction() {
    if (!action.value || !metadata.value || actionBusy.value) return
    const kind = action.value
    const thread = metadata.value
    const current = generation
    actionBusy.value = true
    error.value = ''
    try {
      const updated = await api.post<SupportThread>('/api/v1/support/' + thread.id + '/' + kind, {
        expectedVersion: thread.version,
        reason: actionReason.value.trim() || undefined,
        ...(kind === 'assign' ? { userId: assignmentTarget.value || null } : {})
      })
      if (disposed || current !== generation || metadata.value?.id !== thread.id) return
      metadata.value = updated
      permitted.value = updated.canReply === true
      cancelAction()
      changed(kind === 'escalate' && updated.canReply !== true)
    } catch (cause) {
      if (!disposed && current === generation)
        error.value =
          cause instanceof ApiError ? cause.message : 'The conversation could not be updated.'
    } finally {
      if (!disposed && current === generation) actionBusy.value = false
    }
  }

  return {
    menuOpen,
    actionDialog,
    action,
    actionReason,
    actionBusy,
    assignmentTarget,
    assignmentChoices,
    startAction,
    completeAction,
    cancelAction
  }
}
