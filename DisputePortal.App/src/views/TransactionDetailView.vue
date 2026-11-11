<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import BackLink from '../components/BackLink.vue'
import CopyReference from '../components/CopyReference.vue'
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { CalendarDays, ShieldCheck, Store, WalletCards } from '@lucide/vue'
import { api } from '../api'
import type { DisputeIntake, Transaction } from '../types'
import { formatDate, formatMoney, transactionStatusLabel } from '../utils/format'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'

const route = useRoute()
const transaction = ref<Transaction | null>(null)
const intake = ref<DisputeIntake | null>(null)
const loading = ref(true)
const error = ref('')
let request: AbortController | undefined,
  generation = 0
async function load() {
  const current = ++generation
  request?.abort()
  request = new AbortController()
  loading.value = true
  error.value = ''
  transaction.value = null
  intake.value = null
  try {
    const id = route.params.id
    const [result, capability] = await Promise.all([
      api.get<Transaction>('/api/v1/transactions/' + id, request.signal),
      api
        .get<DisputeIntake>('/api/v1/transactions/' + id + '/dispute-check', request.signal)
        .catch(() => null)
    ])
    if (current === generation) {
      transaction.value = result
      intake.value = capability
    }
  } catch {
    if (current === generation) error.value = 'This transaction could not be found.'
  } finally {
    if (current === generation) loading.value = false
  }
}
onUnmounted(() => {
  generation++
  request?.abort()
})

onMounted(load)
watch(() => route.params.id, load)
useFeedback(error, undefined, undefined)
</script>

<template>
  <AppShell>
    <BackLink
      fallback="/transactions"
      label="Back To Transactions"
    />
    <div
      v-if="loading"
      class="page-loading"
    >
      <span />
      <span />
    </div>
    <div
      v-else-if="error || !transaction"
      class="surface-panel not-found w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <ShieldCheck :size="24" />
      <h2 class="w3-sans-serif w3-large">Transaction Unavailable</h2>
      <p>{{ error }}</p>
      <RouterLink
        to="/transactions"
        class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      >
        Return To Activity
      </RouterLink>
    </div>
    <template v-else>
      <PageHeading title="Transaction" />
      <div class="detail-layout w3-row-padding">
        <div class="w3-col s12 m6 w3-margin-bottom">
          <section
            class="surface-panel transaction-detail-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <div class="detail-amount-block w3-section w3-padding-16">
              <p>{{ transaction.merchantName }}</p>
              <strong class="w3-block w3-xxlarge w3-text-blue-grey w3-margin-bottom">
                {{ formatMoney(transaction.amount, transaction.currency) }}
              </strong>
              <span
                class="detail-status w3-tag w3-light-grey w3-round-large w3-padding-small w3-small"
              >
                {{ transactionStatusLabel(transaction.status) }}
              </span>
            </div>
            <div class="detail-lines w3-section">
              <div>
                <span class="w3-block w3-margin-bottom">
                  <CalendarDays
                    class="w3-margin-right"
                    :size="16"
                  />
                  Date &amp; Time
                </span>
                <b class="w3-block w3-margin-bottom">
                  {{
                    formatDate(transaction.transactionAt, {
                      weekday: 'short',
                      day: 'numeric',
                      month: 'long',
                      year: 'numeric'
                    })
                  }}
                </b>
              </div>
              <div>
                <span class="w3-block w3-margin-bottom">
                  <Store
                    class="w3-margin-right"
                    :size="16"
                  />
                  Description
                </span>
                <b class="w3-block w3-margin-bottom">{{ transaction.description }}</b>
              </div>
              <div class="transaction-account-line w3-section">
                <span class="w3-block w3-margin-bottom">
                  <WalletCards
                    class="w3-margin-right"
                    :size="16"
                  />
                  {{ transaction.accountType || 'Account' }}
                </span>
                <b class="w3-block w3-margin-bottom">
                  {{ transaction.accountNumber || 'Number unavailable' }}
                </b>
              </div>
              <div
                v-if="transaction.type === 'Refund' && transaction.reference"
                class="transaction-reference-line w3-section w3-small"
              >
                <span class="w3-block w3-margin-bottom">Refund reference</span>
                <b class="w3-block w3-margin-bottom">
                  {{ transaction.reference || 'Not available' }}
                </b>
                <CopyReference
                  v-if="transaction.reference"
                  :reference="transaction.reference"
                  label="Copy Refund Reference"
                />
              </div>
              <div>
                <span class="w3-block w3-margin-bottom">Type</span>
                <b class="w3-block w3-margin-bottom">
                  {{ transaction.typeLabel || transaction.type }}
                </b>
              </div>
            </div>
          </section>
        </div>
        <div class="w3-col s12 m6 w3-margin-bottom">
          <aside
            class="transaction-action surface-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <h2 class="w3-sans-serif w3-large">Disputes</h2>
            <RouterLink
              v-if="intake?.activeDisputeId"
              :to="'/disputes/' + intake.activeDisputeId"
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom"
            >
              View Existing Dispute
            </RouterLink>
            <RouterLink
              v-else-if="intake?.canStart === true"
              :to="'/disputes/new/' + transaction.id"
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom"
            >
              Start A Dispute
            </RouterLink>
            <p v-else>
              {{
                intake?.blockReason ||
                intake?.message ||
                'Dispute eligibility is unavailable. Please try again.'
              }}
            </p>
            <RouterLink
              v-if="transaction.disputeId && !intake?.activeDisputeId"
              :to="'/disputes/' + transaction.disputeId"
              class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            >
              View Dispute History
            </RouterLink>
            <RouterLink
              :to="{
                path: '/support',
                query: { transaction: transaction.id, returnTo: route.fullPath }
              }"
              class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            >
              Contact Support
            </RouterLink>
          </aside>
        </div>
      </div>
    </template>
  </AppShell>
</template>
