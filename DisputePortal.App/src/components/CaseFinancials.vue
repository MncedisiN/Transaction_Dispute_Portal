<script setup lang="ts">
import { reference } from '../portalReference'
import { caseNextStep, isClosedStatus } from '../utils/casePresentation'
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RotateCcw, Undo2 } from '@lucide/vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import CopyReference from './CopyReference.vue'
import { api, ApiError } from '../api'
import type { FinancialSummary } from '../financial'
import { formatDate, formatMoney } from '../utils/format'
import { requestId } from '../utils/requestId'
const auth = useAuthStore(),
  router = useRouter()
const props = defineProps<{
  caseId: string
  version: number
  status: string
  resolution?: string | null
  canRefund?: boolean
  accountNumber?: string | null
  accountType?: string | null
}>()
const emit = defineEmits<{ changed: [] }>()
const data = ref<FinancialSummary | null>(null),
  reason = ref(''),
  error = ref(''),
  busy = ref(false)
const action = ref<'refund' | 'recall' | 'reinstate' | null>(null)
const financialPending = computed(() => data.value?.pending?.status === 'Processing')
const staff = computed(() => auth.isStaffWorkspace)
const canApproveRefund = computed(
  () => staff.value && data.value?.canApproveRefund === true && !financialPending.value
)
const actionTitle = computed(() =>
  action.value === 'recall'
    ? 'Recall Refund'
    : action.value === 'reinstate'
      ? 'Reinstate Dispute'
      : 'Approve Refund'
)
const actionHelp = computed(() =>
  action.value === 'recall'
    ? 'Reverse the refund from the original account. The case stays closed. The customer can reinstate it for another review.'
    : action.value === 'reinstate'
      ? 'Create a linked dispute for another review. This case and its refund history stay available.'
      : ''
)
let controller: AbortController | undefined,
  generation = 0,
  disposed = false
let submission: { payload: string; key: string } | null = null
async function load() {
  const n = ++generation,
    id = props.caseId
  controller?.abort()
  const request = new AbortController()
  controller = request
  try {
    const result = await api.get<FinancialSummary>('/api/v1/finance/disputes/' + id, request.signal)
    if (!disposed && n === generation && props.caseId === id) data.value = result
  } catch {
    if (!disposed && n === generation && !request.signal.aborted)
      error.value = 'Financial details are temporarily unavailable.'
  }
}
function begin(value: 'refund' | 'recall' | 'reinstate') {
  if (busy.value || financialPending.value) return
  action.value = value
  reason.value = ''
  error.value = ''
  submission = null
}
async function confirm() {
  const kind = action.value
  if (
    !kind ||
    busy.value ||
    financialPending.value ||
    reason.value.trim().length < (reference.financialReason?.minimum ?? Infinity)
  )
    return
  if (
    (kind === 'refund' && !canApproveRefund.value) ||
    (kind === 'recall' && (!staff.value || !data.value?.canRecall)) ||
    (kind === 'reinstate' && (staff.value || !data.value?.canReinstate))
  )
    return
  const id = props.caseId,
    body = { expectedVersion: props.version, reason: reason.value.trim() }
  const serialized = JSON.stringify({ id, kind, ...body })
  if (submission?.payload !== serialized) submission = { payload: serialized, key: requestId() }
  busy.value = true
  error.value = ''
  try {
    const path =
      kind === 'reinstate'
        ? '/api/v1/disputes/' + id + '/reinstate'
        : '/api/v1/finance/disputes/' + id + '/' + kind
    const result = await api.post<{ id?: string }>(path, { ...body, requestId: submission.key })
    if (disposed || props.caseId !== id) return
    action.value = null
    reason.value = ''
    submission = null
    if (kind === 'reinstate' && result.id) {
      await router.push('/disputes/' + result.id)
      return
    }
    await load()
    emit('changed')
  } catch (cause) {
    if (disposed || props.caseId !== id) return
    error.value =
      cause instanceof ApiError
        ? cause.message
        : 'The change was not confirmed. You can safely retry.'
    if (cause instanceof ApiError && cause.status === 409) {
      await load()
      emit('changed')
    }
  } finally {
    if (!disposed && props.caseId === id) busy.value = false
  }
}
const pendingPoll = setInterval(() => {
  if (financialPending.value) void load()
}, 3000)
onMounted(load)
function closeStaleConfirmation() {
  action.value = null
  if (!busy.value) {
    reason.value = ''
    submission = null
  }
}
watch(canApproveRefund, (allowed) => {
  if (!allowed && action.value === 'refund') closeStaleConfirmation()
})
watch(
  () => [props.version, props.status],
  () => {
    data.value = null
    closeStaleConfirmation()
    void load()
  },
  { flush: 'sync' }
)
watch(
  () => props.caseId,
  () => {
    data.value = null
    action.value = null
    reason.value = ''
    error.value = ''
    submission = null
    busy.value = false
    void load()
  }
)
onUnmounted(() => {
  clearInterval(pendingPoll)
  disposed = true
  generation++
  controller?.abort()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <div
    v-if="data?.pending"
    :class="data.pending.status === 'Failed' ? 'form-alert' : 'info-notice'"
    role="status"
  >
    {{
      data.pending.status === 'Failed'
        ? 'The financial request could not be completed. Review the case before trying again.'
        : data.pending.kind === 'Recall'
          ? 'The refund recall is processing. We’ll update the case after confirmation.'
          : 'The refund is processing. We’ll update the balance and case after confirmation.'
    }}
  </div>
  <section
    v-if="
      canApproveRefund ||
      canRefund ||
      data?.refund ||
      data?.decision ||
      isClosedStatus(status) ||
      data?.parentDisputeId ||
      error
    "
    class="surface-panel financial-outcome w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    aria-label="Financial Details"
  >
    <h2 class="w3-sans-serif w3-large">
      {{
        data?.recall
          ? 'Refund Recalled'
          : data?.refund
            ? 'Refund'
            : isClosedStatus(status)
              ? 'Resolution'
              : 'Review Status'
      }}
    </h2>
    <RouterLink
      v-if="data?.parentDisputeId"
      :to="(staff ? '/admin/disputes/' : '/disputes/') + data.parentDisputeId"
      class="text-link parent-dispute-link w3-text-blue-grey"
    >
      View Previous Dispute
    </RouterLink>
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <template v-if="data?.refund">
      <span class="status-badge">
        {{
          data.recall
            ? 'Refund recalled'
            : data.refund.automatic
              ? 'Automatically resolved'
              : 'Refund approved'
        }}
      </span>
      <strong class="refund-value w3-block w3-xxlarge w3-text-blue-grey w3-section">
        {{ formatMoney(data.refund.amount, data.refund.currency) }}
      </strong>
      <div
        v-if="accountNumber || accountType"
        class="refund-account w3-section w3-small"
      >
        <span>{{ accountType ? accountType + ' account' : 'Account' }}</span>
        <b>{{ accountNumber || 'Number unavailable' }}</b>
      </div>
      <p>
        {{
          data.recall
            ? 'Reversed · ' + formatDate(data.recall.postedAt)
            : 'Credited · ' + formatDate(data.refund.postedAt)
        }}
      </p>
      <p
        v-if="data.recall"
        class="recall-reason w3-panel w3-pale-yellow w3-padding w3-round-large"
      >
        {{ data.recall.reason }}
      </p>
      <div
        v-if="data.refund.customerReference"
        class="refund-reference w3-section w3-small"
      >
        <span>Refund reference</span>
        <b>{{ data.refund.customerReference }}</b>
        <CopyReference
          :reference="data.refund.customerReference"
          label="Copy Refund Reference"
        />
      </div>
      <div
        v-if="staff && data.refund.creditReference"
        class="refund-reference w3-section w3-small"
      >
        <span>Bank refund reference</span>
        <b>{{ data.refund.creditReference }}</b>
        <CopyReference
          :reference="data.refund.creditReference"
          label="Copy Bank Refund Reference"
        />
      </div>
      <button
        v-if="staff && data.canRecall && !action"
        type="button"
        :disabled="busy"
        class="button button-secondary full-width financial-action w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        @click="begin('recall')"
      >
        <Undo2
          class="w3-margin-right"
          :size="17"
        />
        Recall Refund
      </button>
      <RouterLink
        v-if="data.reinstatedDisputeId"
        :to="(staff ? '/admin/disputes/' : '/disputes/') + data.reinstatedDisputeId"
        class="button button-secondary full-width financial-action w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      >
        View Reinstated Dispute
      </RouterLink>
      <button
        v-if="!staff && data.canReinstate && !action"
        type="button"
        :disabled="busy"
        class="button button-primary full-width financial-action w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        @click="begin('reinstate')"
      >
        <RotateCcw
          class="w3-margin-right"
          :size="17"
        />
        Reinstate Dispute
      </button>
    </template>
    <template v-else>
      <p>
        {{
          isClosedStatus(status)
            ? resolution?.trim() || caseNextStep(status) || 'This case is closed.'
            : caseNextStep(status) || 'Your case is waiting for review.'
        }}
      </p>
      <button
        v-if="canApproveRefund && !action"
        type="button"
        :disabled="busy"
        class="button button-primary full-width w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        @click="begin('refund')"
      >
        Approve Refund
      </button>
    </template>
    <form
      v-if="action"
      class="staff-form financial-confirmation w3-bar w3-section"
      @submit.prevent="confirm"
    >
      <h3 class="w3-large w3-sans-serif">{{ actionTitle }}</h3>
      <p v-if="actionHelp">
        {{ actionHelp }}
      </p>
      <label
        class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
        :for="'financial-reason-' + caseId"
      >
        {{
          action === 'refund'
            ? 'Approval Reason'
            : action === 'recall'
              ? 'Recall Reason'
              : 'Reason For Reinstating'
        }}
      </label>
      <textarea
        :id="'financial-reason-' + caseId"
        v-model="reason"
        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
        required
        :minlength="reference.financialReason?.minimum"
        :maxlength="reference.financialReason?.maximum ?? undefined"
        rows="3"
        :disabled="busy || financialPending"
      />
      <div class="financial-confirmation-actions">
        <button
          type="button"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="busy || financialPending"
          @click="action = null"
        >
          Cancel
        </button>
        <button
          type="submit"
          class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
          :disabled="
            busy || reason.trim().length < (reference.financialReason?.minimum ?? Infinity)
          "
        >
          {{
            busy
              ? 'Saving…'
              : action === 'recall'
                ? 'Confirm Recall'
                : action === 'reinstate'
                  ? 'Confirm Reinstatement'
                  : 'Confirm Refund'
          }}
        </button>
      </div>
    </form>
  </section>
</template>
