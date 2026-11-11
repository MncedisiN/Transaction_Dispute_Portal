<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '../api'
import type { StaffTransactionDetail } from '../staffTransactions'
import type { Transaction } from '../types'
import { requestId } from '../utils/requestId'
import AppShell from '../components/AppShell.vue'
import BackLink from '../components/BackLink.vue'
import PageHeading from '../components/PageHeading.vue'
import CustomerAssistanceDetails from '../components/CustomerAssistanceDetails.vue'
import DisputeSubmissionForm from '../components/DisputeSubmissionForm.vue'
const route = useRoute(),
  router = useRouter(),
  detail = ref<StaffTransactionDetail | null>(null),
  loading = ref(true),
  saving = ref(false),
  error = ref(''),
  intake = ref(false)
const transaction = computed<Transaction | null>(() =>
  detail.value
    ? { ...detail.value.transaction, hasActiveDispute: !!detail.value.transaction.disputeId }
    : null
)
let controller: AbortController | undefined,
  submission: { payload: string; key: string } | null = null
async function load() {
  controller?.abort()
  const current = new AbortController()
  controller = current
  loading.value = true
  error.value = ''
  detail.value = null
  intake.value = false
  submission = null
  try {
    const result = await api.get<StaffTransactionDetail>(
      '/api/v1/admin/transactions/' + String(route.params.id),
      current.signal
    )
    if (!current.signal.aborted) detail.value = result
  } catch (e) {
    if (!current.signal.aborted)
      error.value = e instanceof ApiError ? e.message : 'Transaction unavailable.'
  } finally {
    if (!current.signal.aborted) loading.value = false
  }
}
async function submit(payload: { category: string; reason: string; description: string }) {
  if (!detail.value || saving.value) return
  const item = detail.value.transaction
  const body = { transactionId: item.id, ...payload },
    serialized = JSON.stringify(body)
  if (submission?.payload !== serialized) submission = { payload: serialized, key: requestId() }
  saving.value = true
  error.value = ''
  try {
    const result = await api.post<{ id: string }>(
      '/api/v1/admin/transactions/' + item.id + '/disputes',
      body,
      { 'Idempotency-Key': submission.key },
      controller?.signal
    )
    await router.push('/admin/disputes/' + result.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'The dispute was not submitted. Please retry.'
  } finally {
    saving.value = false
  }
}
onMounted(load)
watch(() => route.params.id, load)
onUnmounted(() => controller?.abort())
useFeedback(error, undefined, undefined)
</script>
<template>
  <AppShell>
    <BackLink
      fallback="/admin/transactions"
      label="Back To Transactions"
    />
    <PageHeading title="Customer Assistance" />
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-if="loading"
      role="status"
    >
      Loading transaction…
    </p>
    <template v-else-if="detail">
      <CustomerAssistanceDetails :detail="detail" />
      <section
        class="surface-panel assisted-intake w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <RouterLink
          v-if="detail.transaction.disputeId"
          class="button button-primary full-width w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom"
          :to="'/admin/disputes/' + detail.transaction.disputeId"
        >
          Open Linked Case
        </RouterLink>
        <template
          v-else-if="detail.transaction.disputeEligible && detail.customer.status === 'Active'"
        >
          <button
            v-if="!intake"
            type="button"
            class="button button-primary full-width w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            @click="intake = true"
          >
            Create Dispute For Customer
          </button>
          <p
            v-else
            class="info-notice w3-panel w3-pale-blue w3-leftbar w3-border-blue"
          >
            Record the customer’s concern. The case will be attributed to you and routed to
            qualified staff.
          </p>
        </template>
        <p v-else>This transaction is available as history and cannot start a new dispute.</p>
      </section>
      <DisputeSubmissionForm
        v-if="intake && transaction"
        :transaction="transaction"
        :loading="saving"
        @submit="submit"
      />
    </template>
  </AppShell>
</template>
