<script setup lang="ts">
import { clientOption } from '../utils/portalContent'
import { useFeedback } from '../composables/useFeedback'

import { onMounted, onUnmounted, ref } from 'vue'
import { Search, ChevronRight } from '@lucide/vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '../api'
import type { PageResult } from '../types'
import type { StaffTransaction } from '../staffTransactions'
import { formatTransactionTime, formatMoney, transactionTypeLabel } from '../utils/format'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import CaseHistoryFilters from '../components/CaseHistoryFilters.vue'
import { emptyHistoryFilters, type HistoryFilters } from '../utils/historyFilters'
const route = useRoute(),
  router = useRouter(),
  search = ref(typeof route.query.search === 'string' ? route.query.search : ''),
  page = ref(1),
  filters = ref<HistoryFilters>({
    ...emptyHistoryFilters(),
    status: typeof route.query.status === 'string' ? route.query.status : '',
    from: typeof route.query.from === 'string' ? route.query.from : '',
    to: typeof route.query.to === 'string' ? route.query.to : ''
  }),
  data = ref<PageResult<StaffTransaction> | null>(null),
  loading = ref(true),
  error = ref('')
let controller: AbortController | undefined
async function load() {
  controller?.abort()
  const request = new AbortController()
  controller = request
  loading.value = true
  error.value = ''
  try {
    const result = await api.page<StaffTransaction>(
      '/api/v1/admin/transactions',
      {
        page: page.value,
        pageSize: clientOption('staffPageSize'),
        search: search.value,
        status: filters.value.status,
        from: filters.value.from,
        to: filters.value.to
      },
      request.signal
    )
    if (!request.signal.aborted) data.value = result
  } catch (e) {
    if (!request.signal.aborted)
      error.value = e instanceof ApiError ? e.message : 'Transactions are temporarily unavailable.'
  } finally {
    if (!request.signal.aborted) loading.value = false
  }
}
async function apply(value = filters.value) {
  filters.value = value
  page.value = 1
  await router.replace({
    query: {
      search: search.value || undefined,
      status: value.status || undefined,
      from: value.from || undefined,
      to: value.to || undefined
    }
  })
  await load()
}
onMounted(load)
onUnmounted(() => controller?.abort())
function changePage(delta: number) {
  page.value += delta
  void load()
}
useFeedback(error, undefined, undefined)
</script>
<template>
  <AppShell>
    <PageHeading title="Customer Transactions" />

    <section
      class="surface-panel staff-transactions w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <form
        class="filter-bar compact-transaction-toolbar w3-section w3-flex"
        role="search"
        @submit.prevent="apply()"
      >
        <div class="w3-block w3-rest">
          <label class="search-field w3-block w3-text-dark-grey w3-medium">
            <input
              v-model="search"
              type="search"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              aria-label="Search Customer Transactions"
              maxlength="100"
              placeholder="Customer, contact, merchant or reference"
            />
          </label>
        </div>
        <div class="w3-padding-small">
          <CaseHistoryFilters
            :value="filters"
            kind="transactions"
            @apply="apply"
          />
        </div>
        <div class="w3-padding-small">
          <button
            class="icon-button search-submit w3-button w3-round-large w3-block w3-border w3-white w3-large w3-padding-small"
            aria-label="Search Transactions"
            type="submit"
          >
            <Search :size="18" />
          </button>
        </div>
      </form>
      <p
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
        <button
          type="button"
          class="text-button w3-button w3-text-black w3-round-large"
          @click="load"
        >
          Retry
        </button>
      </p>
      <p class="result-count w3-small w3-text-grey w3-section">
        {{ data?.total ?? '—' }} transactions
      </p>
      <p
        v-if="loading"
        role="status"
      >
        Loading transactions…
      </p>
      <div
        v-else-if="data?.items.length"
        class="staff-transaction-list w3-section"
      >
        <RouterLink
          v-for="item in data.items"
          :key="item.id"
          :to="'/admin/transactions/' + item.id"
          class="staff-transaction-row w3-button w3-block w3-left-align w3-border-bottom w3-padding-16"
        >
          <div class="staff-transaction-info w3-block">
            <strong class="w3-block">
              {{ item.type === 'Refund' ? 'Dispute Refund' : item.merchantName }}
            </strong>
            <small class="w3-small w3-block">{{ formatTransactionTime(item.transactionAt) }}</small>
            <small class="w3-small">{{ transactionTypeLabel(item.type) }}</small>
            <span class="w3-block">{{ item.customerName }} · {{ item.accountType }}</span>
          </div>
          <div
            class="staff-transaction-amount w3-right-align"
            :class="{ 'incoming-credit': item.direction === 'Credit' }"
          >
            <b>
              {{ item.direction === 'Credit' ? '+' : '−'
              }}{{ formatMoney(item.amount, item.currency) }}
            </b>
            <small
              v-if="item.disputeId"
              class="w3-small"
            >
              Case Linked
            </small>
            <small
              v-else-if="item.status === 'Pending'"
              class="w3-small"
            >
              Pending
            </small>
            <small
              v-else-if="item.status === 'Reversed'"
              class="w3-small"
            >
              Reversed
            </small>
          </div>
          <ChevronRight
            class="staff-row-chevron w3-right"
            :size="18"
          />
        </RouterLink>
      </div>
      <p v-else>No transactions match your filters.</p>
      <div
        v-if="data && data.total > data.pageSize"
        class="pagination w3-container w3-section w3-center"
      >
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="page === 1 || loading"
          @click="changePage(-1)"
        >
          Previous
        </button>
        <span>Page {{ page }}</span>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="page * data.pageSize >= data.total || loading"
          @click="changePage(1)"
        >
          Next
        </button>
      </div>
    </section>
  </AppShell>
</template>
