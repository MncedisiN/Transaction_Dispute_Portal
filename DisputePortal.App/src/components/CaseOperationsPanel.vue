<script setup lang="ts">
import DisclosurePanel from './DisclosurePanel.vue'
import { useFeedback } from '../composables/useFeedback'

import { isClosedStatus } from '../utils/casePresentation'
import { targetInput, targetInstant } from '../utils/caseDeadlines'
import { formatDate } from '../utils/format'
import { nextTick, onUnmounted, ref, watch, computed } from 'vue'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { api, ApiError } from '../api'
import type { CaseOperations } from '../operations'
import { priorityLabels, caseTeams } from '../operations'
import CaseTimers from './CaseTimers.vue'
import CaseClaimAction from './CaseClaimAction.vue'
import ChoiceSheet from './ChoiceSheet.vue'
import { userRoles } from '../utils/userAccess'
import { reference } from '../portalReference'
import type { ActionActor } from '../types'
const props = defineProps<{
  caseId: string
  customer?: boolean
  resolvedBy?: ActionActor | null
  status?: string
  assignmentRequest?: number
  assignmentIntent?: 'assign' | 'unassign'
}>()
const emit = defineEmits<{ changed: [] }>()
const item = ref<CaseOperations | null>(null)
const error = ref(''),
  success = ref(''),
  saving = ref(false)
const responseTarget = ref(''),
  resolutionTarget = ref(''),
  targetReason = ref('')
const assignmentDisclosure = ref<InstanceType<typeof DisclosurePanel> | null>(null)
let handledAssignmentRequest = 0
async function openRequestedAssignment() {
  if (
    !item.value ||
    !props.assignmentRequest ||
    handledAssignmentRequest === props.assignmentRequest
  )
    return
  handledAssignmentRequest = props.assignmentRequest
  if (props.assignmentIntent === 'unassign') selected.value = ''
  await nextTick()
  if (assignmentDisclosure.value) {
    assignmentDisclosure.value.open = true
    assignmentDisclosure.value.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
  }
}
watch(
  () => props.assignmentRequest,
  () => {
    void openRequestedAssignment()
  }
)
const selected = ref(''),
  priority = ref(0),
  severity = ref(''),
  queue = ref('')
const currentStatus = computed(() => props.status ?? item.value?.status)
const closed = computed(
  () => item.value?.actions?.isClosed ?? isClosedStatus(currentStatus.value ?? '')
)
const ownerName = computed(() =>
  closed.value
    ? props.resolvedBy?.fullName ||
      props.resolvedBy?.accountName ||
      props.resolvedBy?.label ||
      'Closing actor not recorded'
    : (item.value?.assignedName ?? 'Awaiting Assignment')
)
const assignmentReason = ref(''),
  triageReason = ref(''),
  escalationReason = ref(''),
  claimReason = ref('')
const supervisor = computed(() => !props.customer && item.value?.actions?.canAssign === true)
const canEscalate = computed(() => !props.customer && item.value?.actions?.canEscalate === true)
const canClaim = computed(() => !props.customer && item.value?.actions?.canTakeOwnership === true)
const qualified = computed(() => item.value?.actions?.assignees ?? [])
const eligible = computed(() => qualified.value.filter((person) => person.eligible))
const reasonLimits = computed(() => reference.assignmentReason)
const escalationLimits = computed(() => reference.escalationReason)
const staffOptions = computed(() => {
  const options = [
    { value: '', label: 'Unassigned', hint: 'Return to the specialist queue', disabled: false }
  ]
  if (!item.value) return options
  for (const person of qualified.value) {
    const unavailable = person.unavailableReason
    options.push({
      value: person.id,
      label: person.displayName,
      hint: [
        userRoles[person.role]?.label,
        person.activeCases + '/' + person.capacity + ' cases',
        unavailable || 'Available',
        person.id === item.value.assignedTo ? 'Current owner' : ''
      ]
        .filter(Boolean)
        .join(' · '),
      disabled: !!unavailable
    })
  }
  if (
    item.value.assignedTo &&
    !qualified.value.some((person) => person.id === item.value!.assignedTo)
  )
    options.push({
      value: item.value.assignedTo,
      label: item.value.assignedName ?? 'Current owner',
      hint: 'Choose A Qualified Replacement',
      disabled: true
    })
  return options
})
const canSaveAssignment = computed(
  () =>
    !!item.value &&
    selected.value !== (item.value.assignedTo ?? '') &&
    (!selected.value || eligible.value.some((person) => person.id === selected.value)) &&
    !!reasonLimits.value &&
    assignmentReason.value.trim().length >= reasonLimits.value.minimum
)
let loadRequest: AbortController | undefined,
  loadGeneration = 0,
  disposed = false
async function load(preserveDraft = false) {
  loadRequest?.abort()
  const controller = new AbortController()
  loadRequest = controller
  const generation = ++loadGeneration,
    id = props.caseId
  if (item.value?.id !== id) item.value = null
  try {
    const details = await api.get<CaseOperations>(
      '/api/v1/operations/cases/' + id,
      controller.signal
    )
    if (disposed || generation !== loadGeneration) return
    item.value = details
    if (!preserveDraft) {
      selected.value = details.assignedTo ?? ''
      priority.value = details.priority
      severity.value = details.severity
      queue.value = details.queueType
      responseTarget.value = targetInput(details.responseDueAt)
      resolutionTarget.value = targetInput(details.dueAt)
    }
    error.value = ''
    void openRequestedAssignment()
  } catch (cause) {
    if (!disposed && !controller.signal.aborted)
      error.value =
        cause instanceof ApiError ? cause.message : 'Case management is temporarily unavailable.'
  }
}
async function mutationFailed(cause: unknown, id: string) {
  const message =
    cause instanceof ApiError
      ? Object.values(cause.fields).flat().join(' ') || cause.message
      : 'Changes were not saved. Please try again.'
  if (cause instanceof ApiError && cause.status === 409) {
    await load(true)
    if (disposed || props.caseId !== id) return
    error.value = message + ' Latest case details loaded. Review your choice before trying again.'
  } else error.value = message
}
async function mutate(path: string, payload: object, complete: () => void, confirmation: string) {
  if (saving.value || !item.value || item.value.id !== props.caseId) return
  const id = props.caseId
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    await api.post(path, payload)
    if (disposed || props.caseId !== id) return
    complete()
    emit('changed')
    await load()
    if (!disposed && props.caseId === id) {
      success.value = confirmation
      emit('changed')
    }
  } catch (cause) {
    if (!disposed && props.caseId === id) await mutationFailed(cause, id)
  } finally {
    if (!disposed) saving.value = false
  }
}
async function saveDeadlines() {
  if (!item.value || saving.value) return
  const response = item.value.firstResponseAt
      ? item.value.responseDueAt
      : targetInstant(responseTarget.value),
    due = targetInstant(resolutionTarget.value)
  if (!response || !due) {
    error.value = 'Choose valid response and resolution dates.'
    return
  }
  if (targetReason.value.trim().length < (reasonLimits.value?.minimum ?? Infinity)) {
    error.value = 'Add a brief reason for changing the targets.'
    return
  }
  await mutate(
    '/api/v1/operations/cases/' + props.caseId + '/deadlines',
    {
      expectedVersion: item.value.version,
      responseDueAt: response,
      dueAt: due,
      reason: targetReason.value.trim()
    },
    () => {
      targetReason.value = ''
    },
    'Review targets updated.'
  )
}
async function escalate() {
  if (
    !item.value ||
    escalationReason.value.trim().length < (escalationLimits.value?.minimum ?? Infinity)
  )
    return
  await mutate(
    '/api/v1/notification-settings/cases/' + props.caseId + '/escalate',
    { expectedVersion: item.value.version, reason: escalationReason.value.trim() },
    () => {
      escalationReason.value = ''
    },
    'Senior review requested. Supervisors have been notified.'
  )
}
async function claim() {
  if (!item.value) return
  await mutate(
    '/api/v1/operations/cases/' + props.caseId + '/claim',
    {
      expectedVersion: item.value.version,
      reason: claimReason.value.trim() || 'Self-assigned from the case workflow.'
    },
    () => {
      claimReason.value = ''
    },
    'This case is now assigned to you.'
  )
}
async function save(kind: 'assignment' | 'triage') {
  if (!item.value || (kind === 'assignment' && !canSaveAssignment.value)) return
  const reason = (kind === 'assignment' ? assignmentReason.value : triageReason.value).trim()
  if (!reasonLimits.value || reason.length < reasonLimits.value.minimum) return
  const operation = kind === 'assignment' && !selected.value ? 'unassign' : kind
  await mutate(
    '/api/v1/operations/cases/' + props.caseId + '/' + operation,
    kind === 'assignment'
      ? selected.value
        ? { expectedVersion: item.value.version, userId: selected.value, reason }
        : { expectedVersion: item.value.version, reason }
      : {
          expectedVersion: item.value.version,
          priority: priority.value,
          severity: severity.value,
          queueType: queue.value,
          reason
        },
    () => {
      if (kind === 'assignment') assignmentReason.value = ''
      else triageReason.value = ''
    },
    kind === 'assignment' ? 'Case assignment updated.' : 'Priority and team updated.'
  )
}
useLiveRefresh(() => load(!!item.value), ['disputes', 'administration'])
watch(
  () => props.caseId,
  () => {
    assignmentReason.value = ''
    triageReason.value = ''
    escalationReason.value = ''
    claimReason.value = ''
    targetReason.value = ''
    success.value = ''
    void load()
  }
)
onUnmounted(() => {
  disposed = true
  loadGeneration++
  loadRequest?.abort()
})
useFeedback(error, success, undefined)
</script>
<template>
  <section
    class="surface-panel ops-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <div class="ops-heading w3-bar w3-padding-16">
      <div>
        <p class="eyebrow w3-small w3-wide w3-text-grey">
          {{
            closed
              ? currentStatus === 'Resolved'
                ? 'RESOLVED BY'
                : 'CLOSED BY'
              : customer
                ? 'YOUR REVIEW'
                : 'CASE OWNER'
          }}
        </p>
        <h2 class="w3-sans-serif w3-large">{{ ownerName }}</h2>
      </div>
      <span
        v-if="item"
        class="ops-pill w3-tag w3-light-grey w3-round-large w3-small w3-padding-small"
        :class="'urgency-' + item.priority"
      >
        {{ priorityLabels[item.priority] }} priority
      </span>
    </div>
    <p
      v-if="error"
      role="alert"
      class="form-alert workflow-feedback w3-panel w3-pale-red w3-leftbar w3-border-red"
    >
      {{ error }}
      <button
        class="text-button w3-button w3-text-black w3-round-large"
        type="button"
        :disabled="saving"
        @click="load(true)"
      >
        Refresh Case
      </button>
    </p>
    <p
      v-if="success"
      role="status"
      class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
    >
      {{ success }}
    </p>
    <template v-if="item">
      <DisclosurePanel
        v-if="canClaim && item.assignedTo"
        class="compact-disclosure ownership-disclosure"
      >
        <template #heading>Take Ownership</template>
        <form
          class="case-claim-form w3-bar"
          @submit.prevent="claim"
        >
          <label
            v-if="item.assignedTo"
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          >
            Reason For Taking Over
            <input
              v-model.trim="claimReason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              required
              :minlength="reasonLimits?.minimum"
              :maxlength="reasonLimits?.maximum ?? undefined"
              placeholder="Explain the handover"
            />
          </label>
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="
              saving ||
              (!!item.assignedTo && claimReason.length < (reasonLimits?.minimum ?? Infinity))
            "
          >
            {{ saving ? 'Assigning…' : 'Take This Case' }}
          </button>
        </form>
      </DisclosurePanel>
      <CaseClaimAction
        v-if="!customer && !closed && !item.assignedTo"
        :item="item"
        @claimed="
          () => {
            load()
            emit('changed')
          }
        "
        @refresh="load"
      />
      <div class="ops-meta w3-bar w3-small w3-text-grey w3-section">
        <span>{{ caseTeams[item.queueType]?.label }}</span>
        <span>Severity: {{ item.severity }}</span>
      </div>
      <div
        v-if="item.escalationLevel > 0"
        class="escalation-banner w3-panel w3-pale-yellow w3-padding w3-round-large"
      >
        <strong>Escalated for senior review</strong>
        <span>The specialist team remains responsible until a handover is confirmed.</span>
        <small class="w3-small">
          Escalation level {{ item.escalationLevel }} ·
          {{ item.escalatedAt ? formatDate(item.escalatedAt) : '' }}
        </small>
      </div>
      <DisclosurePanel class="workflow-guide">
        <template #heading>Review Steps</template>
        <ol>
          <li
            v-for="step in caseTeams[item.queueType]?.steps"
            :key="step"
          >
            {{ step }}
          </li>
        </ol>
      </DisclosurePanel>
      <CaseTimers :item="item" />
      <DisclosurePanel
        v-if="!customer"
        class="compact-disclosure"
      >
        <template #heading>
          {{ item.escalationLevel > 0 ? 'Team & Senior Reviewers' : 'Specialist Team' }} ·
          {{ qualified.length }}
        </template>
        <ul class="assignment-roster w3-ul w3-section">
          <li
            v-for="person in qualified"
            :key="person.id"
          >
            <span>
              <strong>{{ person.displayName }}</strong>
              <small class="w3-small">
                {{ userRoles[person.role]?.label
                }}{{ person.id === item.assignedTo ? ' · Current owner' : '' }}
              </small>
            </span>
            <span>
              {{ person.unavailableReason || 'Available' }}
              <small class="w3-small">{{ person.activeCases }}/{{ person.capacity }} cases</small>
            </span>
          </li>
        </ul>
        <p
          v-if="!qualified.length"
          class="user-editor-note w3-small w3-text-grey"
        >
          No staff are configured for this team yet. A super admin can add qualified users.
        </p>
      </DisclosurePanel>
      <DisclosurePanel
        v-if="supervisor && !item.slaClosedAt"
        ref="assignmentDisclosure"
        class="compact-disclosure assignment-disclosure"
      >
        <template #heading>
          <span aria-hidden="true">+</span>
          {{ item.assignedTo ? 'Change Assignment' : 'Assign Case' }}
        </template>
        <form
          class="ops-form w3-bar"
          @submit.prevent="save('assignment')"
        >
          <p class="assignment-eligibility w3-small w3-text-grey w3-section">
            {{ eligible.length }} eligible ·
            {{ caseTeams[item.queueType]?.label }}
          </p>
          <button
            v-if="item.assignedTo && selected"
            type="button"
            class="text-button assignment-unassign w3-button w3-text-black w3-round-large"
            :disabled="saving"
            @click="selected = ''"
          >
            Unassign This Case
          </button>
          <div class="access-field w3-section">
            <span>Case Owner</span>
            <ChoiceSheet
              v-model="selected"
              label="Case Owner"
              :options="staffOptions"
              :disabled="saving"
            />
          </div>
          <p
            v-if="!eligible.length"
            class="user-editor-note w3-small w3-text-grey"
          >
            No qualified staff are available. Staff can update their availability in the case
            management; a super admin can add team members or review capacity.
          </p>
          <small
            v-else
            class="w3-small"
          >
            {{
              item.escalationLevel > 0
                ? 'Choose a team specialist or a senior reviewer. Unavailable staff are shown with a reason.'
                : 'Only qualified staff with availability and capacity can receive this case.'
            }}
          </small>
          <label class="assignment-reason w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Assignment Reason
            <textarea
              v-model="assignmentReason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              rows="3"
              required
              :minlength="reasonLimits?.minimum"
              :maxlength="reasonLimits?.maximum ?? undefined"
              :disabled="saving"
            />
          </label>
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="saving || !canSaveAssignment"
          >
            {{ saving ? 'Saving…' : !selected ? 'Confirm Unassignment' : 'Save Assignment' }}
          </button>
        </form>
      </DisclosurePanel>
      <DisclosurePanel
        v-if="supervisor && !item.slaClosedAt"
        class="compact-disclosure"
      >
        <template #heading>Priority &amp; Team</template>
        <form
          class="ops-form w3-bar"
          @submit.prevent="save('triage')"
        >
          <div class="ops-form-row w3-section">
            <div class="access-field w3-section">
              <span>Priority</span>
              <ChoiceSheet
                :model-value="String(priority)"
                label="Priority"
                :options="
                  Object.entries(priorityLabels).map(([value, label]) => ({ value, label }))
                "
                :disabled="saving"
                @update:model-value="priority = Number($event)"
              />
            </div>
            <div class="access-field w3-section">
              <span>Severity</span>
              <ChoiceSheet
                v-model="severity"
                label="Severity"
                :options="(reference.severities ?? []).map((value) => ({ value, label: value }))"
                :disabled="saving"
              />
            </div>
            <div class="access-field w3-section">
              <span>Specialist Team</span>
              <ChoiceSheet
                v-model="queue"
                label="Specialist Team"
                :options="
                  Object.entries(caseTeams).map(([value, team]) => ({ value, label: team.label }))
                "
                :disabled="saving"
              />
            </div>
          </div>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Reason For Priority Or Team Change
            <input
              v-model.trim="triageReason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              required
              :minlength="reasonLimits?.minimum"
              :maxlength="reasonLimits?.maximum ?? undefined"
            />
          </label>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="saving || triageReason.trim().length < (reasonLimits?.minimum ?? Infinity)"
          >
            Save Priority
          </button>
          <small class="w3-small">
            Targets run from submission. Team changes return the case for qualified reassignment.
          </small>
        </form>
      </DisclosurePanel>
      <DisclosurePanel
        v-if="supervisor && !item.slaClosedAt && !isClosedStatus(item.status)"
        class="compact-disclosure deadline-disclosure"
      >
        <template #heading>Review Targets</template>
        <form
          class="ops-form w3-bar"
          @submit.prevent="saveDeadlines"
        >
          <div class="deadline-inputs w3-row-padding">
            <div class="w3-col s12 m6 w3-margin-bottom">
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                First Response · SAST
                <input
                  v-model="responseTarget"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  type="datetime-local"
                  required
                  :readonly="!!item.firstResponseAt"
                  :aria-readonly="!!item.firstResponseAt"
                  :disabled="saving"
                  :max="item.firstResponseAt ? undefined : resolutionTarget || undefined"
                />
              </label>
            </div>
            <div class="w3-col s12 m6 w3-margin-bottom">
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Resolution · SAST
                <input
                  v-model="resolutionTarget"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  type="datetime-local"
                  required
                  :disabled="saving"
                  :min="item.firstResponseAt ? undefined : responseTarget || undefined"
                />
              </label>
            </div>
          </div>
          <small
            v-if="item.firstResponseAt"
            class="w3-small"
          >
            The recorded first-response target is locked.
          </small>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Reason For Target Change
            <textarea
              v-model.trim="targetReason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              rows="2"
              required
              :minlength="reasonLimits?.minimum"
              :maxlength="reasonLimits?.maximum ?? undefined"
              :disabled="saving"
            />
          </label>
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="saving || targetReason.trim().length < (reasonLimits?.minimum ?? Infinity)"
          >
            {{ saving ? 'Saving…' : 'Save Targets' }}
          </button>
        </form>
      </DisclosurePanel>
      <DisclosurePanel
        v-if="canEscalate && !item.slaClosedAt"
        class="compact-disclosure"
      >
        <template #heading>Escalate Case</template>
        <form
          class="ops-form escalation-form w3-bar"
          @submit.prevent="escalate"
        >
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Internal Escalation Reason
            <textarea
              v-model.trim="escalationReason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              required
              :minlength="escalationLimits?.minimum"
              :maxlength="reasonLimits?.maximum ?? undefined"
              rows="3"
              placeholder="Explain why this case needs senior attention."
            />
          </label>
          <small class="w3-small">
            10–500 characters. The customer sees the escalation; your reason stays internal.
          </small>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="
              saving || escalationReason.trim().length < (escalationLimits?.minimum ?? Infinity)
            "
          >
            Request Senior Review
          </button>
        </form>
      </DisclosurePanel>
    </template>
  </section>
</template>
