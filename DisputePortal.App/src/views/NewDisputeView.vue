<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { BadgeCheck, ShieldCheck } from '@lucide/vue'
import { api, ApiError } from '../api'
import type { DisputeDetail, DisputeIntake, Transaction } from '../types'
import { useFeedback } from '../composables/useFeedback'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { requestId } from '../utils/requestId'
import CopyReference from '../components/CopyReference.vue'
import BackLink from '../components/BackLink.vue'
import EvidencePanel from '../components/EvidencePanel.vue'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import DisputeSecurityCheck from '../components/DisputeSecurityCheck.vue'
import DisputeSubmissionForm from '../components/DisputeSubmissionForm.vue'

const route = useRoute()
const transaction = ref<Transaction | null>(null)
const intake = ref<DisputeIntake | null>(null)
const loading = ref(true)
const submitting = ref(false)
const error = ref('')
const confirmation = ref('')
const createdCaseId = ref('')
const createdCaseStatus = ref('')
const securityReady = ref(false)
const verificationId = ref<string | null>(null)
const requestSpecialistReview = ref(false)
let submission: { payload: string; key: string } | null = null
let request: AbortController | undefined
let generation = 0
let disposed = false

function securityVerified(id: string | null, specialistReview = false) {
  requestSpecialistReview.value = specialistReview
  verificationId.value = id
  securityReady.value = id !== ''
}

function securityRequirements(value: DisputeIntake): string {
  return [
    value.policyVersion,
    value.allowed,
    value.requiresVerification,
    value.assistedReview
  ].join(':')
}

function applyIntake(value: DisputeIntake) {
  if (
    !intake.value ||
    securityRequirements(intake.value) !== securityRequirements(value) ||
    !value.canStart
  ) {
    securityReady.value = false
    verificationId.value = null
    requestSpecialistReview.value = false
  }
  intake.value = value
}

async function load() {
  const current = ++generation
  request?.abort()
  request = new AbortController()
  loading.value = true
  error.value = ''
  transaction.value = null
  intake.value = null
  confirmation.value = ''
  createdCaseId.value = ''
  createdCaseStatus.value = ''
  securityReady.value = false
  verificationId.value = null
  submission = null
  try {
    const id = route.params.transactionId
    const [item, capability] = await Promise.all([
      api.get<Transaction>('/api/v1/transactions/' + id, request.signal),
      api.get<DisputeIntake>('/api/v1/transactions/' + id + '/dispute-check', request.signal)
    ])
    if (disposed || current !== generation) return
    transaction.value = item
    applyIntake(capability)
  } catch (cause) {
    if (!disposed && current === generation)
      error.value = cause instanceof ApiError ? cause.message : 'This transaction is unavailable.'
  } finally {
    if (!disposed && current === generation) loading.value = false
  }
}

async function refreshState(signal?: AbortSignal) {
  const caseId = createdCaseId.value
  if (caseId) {
    const updated = await api.get<DisputeDetail>('/api/v1/disputes/' + caseId, signal)
    if (!disposed && !signal?.aborted && createdCaseId.value === caseId)
      createdCaseStatus.value = updated.dispute.status
    return
  }
  const id = transaction.value?.id
  if (!id || submitting.value) return
  const capability = await api.get<DisputeIntake>(
    '/api/v1/transactions/' + id + '/dispute-check',
    signal
  )
  if (!disposed && !signal?.aborted && transaction.value?.id === id) applyIntake(capability)
}

onMounted(load)
watch(() => route.params.transactionId, load)
onUnmounted(() => {
  disposed = true
  generation++
  request?.abort()
})
useLiveRefresh(refreshState, ['disputes'])

async function submit(payload: { category: string; reason: string; description: string }) {
  if (
    !transaction.value ||
    submitting.value ||
    !securityReady.value ||
    intake.value?.canStart !== true
  )
    return
  const transactionId = transaction.value.id
  const submittedGeneration = generation
  submitting.value = true
  error.value = ''
  try {
    const previousRequirements = securityRequirements(intake.value)
    const capability = await api.get<DisputeIntake>(
      '/api/v1/transactions/' + transactionId + '/dispute-check'
    )
    if (disposed || submittedGeneration !== generation || transaction.value?.id !== transactionId)
      return
    applyIntake(capability)
    if (!capability.canStart) return
    if (previousRequirements !== securityRequirements(capability)) {
      error.value = 'Dispute requirements changed. Review the security check before submitting.'
      return
    }
    const body = {
      transactionId,
      verificationId: verificationId.value,
      requestSpecialistReview: requestSpecialistReview.value,
      ...payload
    }
    const serialized = JSON.stringify(body)
    if (submission?.payload !== serialized) submission = { payload: serialized, key: requestId() }
    const result = await api.post<{ id: string; reference: string; status?: string }>(
      '/api/v1/disputes',
      body,
      { 'Idempotency-Key': submission.key }
    )
    if (disposed || submittedGeneration !== generation) return
    confirmation.value = result.reference
    createdCaseId.value = result.id
    createdCaseStatus.value = result.status ?? ''
    if (!result.status) {
      try {
        await refreshState()
      } catch {
        /* Evidence stays read-only until confirmed case state is available. */
      }
    }
  } catch (cause) {
    if (!disposed && submittedGeneration === generation)
      error.value =
        cause instanceof ApiError
          ? cause.message
          : 'We could not submit the dispute. Please try again.'
  } finally {
    if (!disposed) submitting.value = false
  }
}
useFeedback(error, undefined, undefined)
</script>

<template>
  <AppShell>
    <BackLink
      :fallback="'/transactions/' + route.params.transactionId"
      label="Back To Transaction"
    />
    <div
      v-if="loading"
      class="page-loading"
    >
      <span />
      <span />
    </div>
    <div
      v-else-if="error && !transaction"
      class="surface-panel not-found w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <h2 class="w3-sans-serif w3-large">Transaction Unavailable</h2>
      <p>{{ error }}</p>
    </div>
    <section
      v-else-if="confirmation"
      class="confirmation-panel surface-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <div class="confirmation-hero">
        <span class="confirmation-mark w3-tag w3-theme-d5 w3-round-large w3-padding">
          <BadgeCheck :size="30" />
        </span>
        <h1 class="w3-xxlarge w3-sans-serif w3-text-black">Dispute Received</h1>
        <p>We’ve received your dispute. You can follow its progress at any time.</p>
      </div>
      <div class="reference-box w3-panel w3-light-grey w3-padding-large w3-round-xlarge">
        <small class="w3-small">Your dispute reference</small>
        <div class="reference-value w3-block">
          <b>{{ confirmation }}</b>
          <CopyReference :reference="confirmation" />
        </div>
      </div>
      <p class="confirmation-evidence-intro">
        Have anything else to share? Add supporting evidence below.
      </p>
      <EvidencePanel
        v-if="createdCaseId"
        :case-id="createdCaseId"
        :status="createdCaseStatus"
      />
      <div class="confirmation-actions w3-row-padding w3-section">
        <div class="w3-col s12 m6 w3-margin-bottom">
          <RouterLink
            :to="'/disputes/' + createdCaseId"
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom"
          >
            Track Your Dispute
          </RouterLink>
        </div>
        <div class="w3-col s12 m6 w3-margin-bottom">
          <RouterLink
            to="/transactions"
            class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          >
            Back To Transactions
          </RouterLink>
        </div>
      </div>
    </section>
    <section
      v-else-if="intake && !intake.canStart"
      class="surface-panel dispute-intake-state w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <span
        class="intake-mark"
        aria-hidden="true"
      >
        <ShieldCheck :size="28" />
      </span>
      <h1 class="w3-sans-serif w3-text-black">
        {{ intake.activeDisputeId ? 'Dispute Already Open' : 'Dispute Unavailable' }}
      </h1>
      <p>{{ intake.blockReason || intake.message }}</p>
      <div class="intake-actions">
        <RouterLink
          v-if="intake.activeDisputeId"
          :to="'/disputes/' + intake.activeDisputeId"
          class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom"
        >
          View Existing Dispute
        </RouterLink>
        <RouterLink
          :to="'/transactions/' + route.params.transactionId"
          class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        >
          Back To Transaction
        </RouterLink>
      </div>
    </section>
    <template v-else-if="transaction && intake?.canStart === true">
      <PageHeading title="Dispute Transaction" />
      <div class="dispute-form-layout focused-form-layout">
        <div>
          <div
            v-if="error"
            class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
            role="alert"
          >
            {{ error }}
          </div>
          <DisputeSecurityCheck
            :key="transaction.id + ':' + securityRequirements(intake)"
            :transaction-id="transaction.id"
            :initial-decision="intake"
            @ready="securityVerified"
          />
          <DisputeSubmissionForm
            v-if="securityReady"
            :transaction="transaction"
            :loading="submitting"
            @submit="submit"
          />
        </div>
      </div>
    </template>
  </AppShell>
</template>
