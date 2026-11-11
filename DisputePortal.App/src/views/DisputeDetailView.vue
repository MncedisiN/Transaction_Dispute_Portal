<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { useLiveRefresh } from '../composables/useLiveRefresh'
import CopyReference from '../components/CopyReference.vue'
import BackLink from '../components/BackLink.vue'
import { computed, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ArrowUpRight, MessageCircle, ChevronRight } from '@lucide/vue'
import { api, ApiError } from '../api'
import type { DisputeDetail, Transaction } from '../types'
import type { CaseOperations } from '../operations'
import { formatMoney } from '../utils/format'
import { caseDate, isClosedStatus } from '../utils/casePresentation'
import { caseTeams } from '../operations'
import EvidencePanel from '../components/EvidencePanel.vue'
import AppShell from '../components/AppShell.vue'
import StatusBadge from '../components/StatusBadge.vue'
import CaseProgress from '../components/CaseProgress.vue'
import CaseActivity from '../components/CaseActivity.vue'
import CaseFinancials from '../components/CaseFinancials.vue'
const route = useRoute(),
  detail = ref<DisputeDetail | null>(null),
  transaction = ref<Transaction | null>(null),
  operations = ref<CaseOperations | null>(null),
  error = ref(''),
  loading = ref(true)
const tab = ref<'progress' | 'details' | 'evidence'>('progress')
function confirmWithdrawal() {
  confirmingWithdrawal.value = true
  withdrawalError.value = ''
}
const confirmingWithdrawal = ref(false),
  withdrawing = ref(false),
  withdrawalError = ref(''),
  withdrawalNotice = ref('')
let generation = 0,
  request: AbortController | undefined
const closed = computed(() => isClosedStatus(detail.value?.dispute.status ?? ''))
async function load() {
  const current = ++generation
  request?.abort()
  request = new AbortController()
  loading.value = true
  error.value = ''
  detail.value = null
  transaction.value = null
  operations.value = null
  try {
    const result = await api.get<DisputeDetail>(
      '/api/v1/disputes/' + route.params.id,
      request.signal
    )
    if (current !== generation) return
    detail.value = result
    const results = await Promise.allSettled([
      api.get<Transaction>('/api/v1/transactions/' + result.dispute.transactionId, request.signal),
      api.get<CaseOperations>('/api/v1/operations/cases/' + result.dispute.id, request.signal)
    ])
    if (current !== generation) return
    if (results[0].status === 'fulfilled') transaction.value = results[0].value
    if (results[1].status === 'fulfilled') operations.value = results[1].value
  } catch {
    if (current === generation) error.value = 'This case is unavailable. Please try again.'
  } finally {
    if (current === generation) loading.value = false
  }
}
async function refreshEvidenceActivity() {
  const id = detail.value?.dispute.id
  if (!id) return
  try {
    const updated = await api.get<DisputeDetail>('/api/v1/disputes/' + id, request?.signal)
    if (detail.value?.dispute.id === id) detail.value = updated
  } catch {
    /* Evidence stays saved; the next case refresh reloads its activity. */
  }
}
async function withdraw() {
  if (
    !detail.value ||
    operations.value?.actions?.canWithdraw !== true ||
    closed.value ||
    withdrawing.value ||
    !confirmingWithdrawal.value
  )
    return
  const id = detail.value.dispute.id,
    version = detail.value.dispute.version
  withdrawing.value = true
  withdrawalError.value = ''
  try {
    const updated = await api.post<DisputeDetail>('/api/v1/disputes/' + id + '/withdraw', {
      expectedVersion: version
    })
    if (detail.value?.dispute.id !== id) return
    detail.value = updated
    confirmingWithdrawal.value = false
    tab.value = 'progress'
    withdrawalNotice.value = 'Your dispute has been withdrawn. Its history remains available.'
  } catch (cause) {
    if (detail.value?.dispute.id !== id) return
    withdrawalError.value =
      cause instanceof ApiError
        ? cause.message
        : 'We could not withdraw your dispute. Please try again.'
    if (cause instanceof ApiError && cause.status === 409) await refreshEvidenceActivity()
  } finally {
    if (detail.value?.dispute.id === id) withdrawing.value = false
  }
}
watch(
  () => route.params.id,
  () => {
    tab.value = 'progress'
    confirmingWithdrawal.value = false
    withdrawing.value = false
    withdrawalError.value = ''
    withdrawalNotice.value = ''
    void load()
  }
)
watch(closed, (value) => {
  if (value) confirmingWithdrawal.value = false
})
onUnmounted(() => {
  generation++
  request?.abort()
})

useLiveRefresh(
  async (signal) => {
    if (!detail.value) {
      await load()
      return
    }
    const id = detail.value.dispute.id
    if (loading.value) return
    const updated = await api.get<DisputeDetail>('/api/v1/disputes/' + id, signal)
    if (!signal.aborted && detail.value?.dispute.id === id) detail.value = updated
    const service = await api.get<CaseOperations>('/api/v1/operations/cases/' + id, signal)
    if (!signal.aborted && detail.value?.dispute.id === id) operations.value = service
  },
  ['disputes']
)
useFeedback(error, undefined, undefined)
</script>
<template>
  <AppShell>
    <BackLink
      fallback="/disputes"
      label="Back To Disputes"
    />
    <div
      v-if="loading"
      class="page-loading"
      aria-label="Loading Case"
    >
      <span />
      <span />
    </div>
    <section
      v-else-if="error || !detail"
      class="surface-panel empty-state w3-container w3-margin-bottom w3-center w3-padding-32 w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <h1 class="w3-xxlarge w3-sans-serif w3-text-black">Case Unavailable</h1>
      <p>{{ error }}</p>
      <button
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        @click="load"
      >
        Try Again
      </button>
    </section>
    <template v-else>
      <header class="case-page-heading w3-bar w3-margin-bottom">
        <div>
          <p class="case-caption w3-small w3-text-grey w3-bar">
            <b class="w3-text-blue-grey">{{ detail.dispute.reference }}</b>
            <CopyReference :reference="detail.dispute.reference" />
            <time
              class="w3-block w3-margin-top"
              :datetime="detail.dispute.createdAt"
            >
              {{ caseDate(detail.dispute.createdAt) }}
            </time>
          </p>
          <h1 class="w3-xxlarge w3-sans-serif w3-text-black">
            {{ detail.merchantName || detail.dispute.transactionDescription }}
          </h1>
        </div>
        <StatusBadge :status="detail.dispute.status" />
      </header>
      <p
        v-if="withdrawalNotice"
        class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
        role="status"
      >
        {{ withdrawalNotice }}
      </p>
      <p
        v-if="withdrawalError"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ withdrawalError }}
      </p>
      <section
        class="surface-panel case-focus w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <div class="case-focus-amount w3-margin-bottom">
          <span class="w3-block w3-small w3-text-grey">Disputed Amount</span>
          <strong class="w3-block w3-xxlarge w3-text-blue-grey w3-margin-top">
            {{ formatMoney(detail.dispute.amount, detail.dispute.currency) }}
          </strong>
        </div>
        <CaseProgress :status="detail.dispute.status" />
        <div
          v-if="detail.dispute.status === 'AwaitingCustomer'"
          class="case-next needs-reply w3-panel w3-pale-yellow w3-round-large w3-padding"
        >
          <strong>Your reply is needed</strong>
        </div>
        <div
          v-if="operations?.escalationLevel"
          class="case-escalated w3-panel w3-pale-yellow w3-round-large w3-padding"
        >
          <ArrowUpRight :size="16" />
          <span>
            Escalated for senior review
            <small
              v-if="operations.escalatedAt"
              class="w3-small"
            >
              · {{ caseDate(operations.escalatedAt) }}
            </small>
          </span>
        </div>
        <div
          v-if="detail.dispute.resolution"
          class="case-outcome w3-panel w3-light-grey w3-round-large w3-padding"
        >
          <p>{{ detail.dispute.resolution }}</p>
        </div>
        <p
          v-if="closed && detail.resolvedBy"
          class="case-resolver w3-small w3-text-grey w3-section"
        >
          {{ detail.dispute.status === 'Resolved' ? 'Resolved by' : 'Closed by' }}
          <strong>
            {{
              detail.resolvedBy.fullName || detail.resolvedBy.accountName || detail.resolvedBy.label
            }}
          </strong>
        </p>
        <RouterLink
          :to="{
            path: '/support',
            query: { dispute: detail.dispute.id, returnTo: route.fullPath }
          }"
          class="case-message-link w3-button w3-block w3-left-align w3-padding-16 w3-text-blue-grey w3-topbar w3-border-light-grey"
        >
          <MessageCircle
            :size="20"
            class="w3-margin-right"
          />
          <span>
            {{
              detail.dispute.status === 'AwaitingCustomer'
                ? 'Reply To Your Specialist'
                : 'Case Messages'
            }}
          </span>
          <ChevronRight
            :size="18"
            class="w3-right"
          />
        </RouterLink>
      </section>
      <CaseFinancials
        :case-id="detail.dispute.id"
        :version="detail.dispute.version"
        :status="detail.dispute.status"
        :resolution="detail.dispute.resolution"
        :account-number="transaction?.accountNumber"
        :account-type="transaction?.accountType"
      />
      <nav
        class="section-tabs w3-bar w3-light-grey w3-round-large w3-margin-bottom w3-row w3-border w3-white w3-padding-small"
        aria-label="Case Sections"
      >
        <button
          :class="{ 'w3-theme-l4 w3-text-black': (tab === 'progress') === true }"
          class="w3-button w3-round-large w3-center w3-col s4 w3-padding-16"
          :aria-pressed="tab === 'progress'"
          @click="tab = 'progress'"
        >
          Progress
        </button>
        <button
          :class="{ 'w3-theme-l4 w3-text-black': (tab === 'details') === true }"
          class="w3-button w3-round-large w3-center w3-col s4 w3-padding-16"
          :aria-pressed="tab === 'details'"
          @click="tab = 'details'"
        >
          Details
        </button>
        <button
          :class="{ 'w3-theme-l4 w3-text-black': (tab === 'evidence') === true }"
          class="w3-button w3-round-large w3-center w3-col s4 w3-padding-16"
          :aria-pressed="tab === 'evidence'"
          @click="tab = 'evidence'"
        >
          Evidence
        </button>
      </nav>
      <section
        v-if="tab === 'progress'"
        class="surface-panel case-section w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <CaseActivity
          :case-id="detail.dispute.id"
          :version="detail.dispute.version"
        />
      </section>
      <section
        v-else-if="tab === 'details'"
        class="surface-panel case-section w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <h2 class="w3-sans-serif w3-large">Transaction &amp; Dispute</h2>
        <div class="w3-responsive">
          <table class="detail-definition w3-container w3-table w3-bordered">
            <tbody>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Transaction Type
                </th>
                <td class="w3-mobile">
                  {{
                    detail.dispute.transactionTypeLabel ||
                    transaction?.typeLabel ||
                    transaction?.type ||
                    detail.dispute.transactionType ||
                    '—'
                  }}
                </td>
              </tr>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Description
                </th>
                <td class="w3-mobile">
                  {{ transaction?.description || detail.dispute.transactionDescription }}
                </td>
              </tr>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Transaction Date
                </th>
                <td class="w3-mobile">{{ caseDate(detail.transactionAt) }}</td>
              </tr>
              <tr v-if="transaction">
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Account
                </th>
                <td class="w3-mobile">
                  <RouterLink :to="'/accounts/' + transaction.accountId">
                    {{ transaction.accountType ? transaction.accountType + ' · ' : ''
                    }}{{ transaction.accountNumber || 'Number Unavailable' }}
                  </RouterLink>
                </td>
              </tr>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Reason
                </th>
                <td class="w3-mobile">
                  {{ detail.dispute.category.replace(/([A-Z])/g, ' $1').trim() }}
                </td>
              </tr>
              <tr v-if="operations">
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Review Team
                </th>
                <td class="w3-mobile">{{ caseTeams[operations.queueType]?.label }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
      <EvidencePanel
        v-if="tab === 'evidence'"
        :case-id="detail.dispute.id"
        :can-upload="!closed"
        :status="detail.dispute.status"
        @changed="refreshEvidenceActivity"
      />
      <section
        v-if="!closed && operations?.actions?.canWithdraw === true"
        class="surface-panel withdrawal-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
        aria-label="Withdraw Dispute"
      >
        <template v-if="confirmingWithdrawal">
          <h2 class="w3-sans-serif w3-large">Withdraw This Dispute?</h2>
          <p>
            This ends the review and closes case messages. Your dispute and evidence remain in your
            case history.
          </p>
          <div class="withdrawal-actions">
            <button
              type="button"
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
              :disabled="withdrawing"
              @click="withdraw"
            >
              {{ withdrawing ? 'Withdrawing…' : 'Confirm Withdrawal' }}
            </button>
            <button
              type="button"
              class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
              :disabled="withdrawing"
              @click="confirmingWithdrawal = false"
            >
              Keep Dispute Open
            </button>
          </div>
        </template>
        <template v-else>
          <h2 class="w3-sans-serif w3-large">No Longer Need This Review?</h2>
          <p>You can withdraw your dispute while it is open.</p>
          <button
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            @click="confirmWithdrawal"
          >
            Withdraw Dispute
          </button>
        </template>
      </section>
    </template>
  </AppShell>
</template>
