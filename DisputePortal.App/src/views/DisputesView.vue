<script setup lang="ts">
import { clientOption } from '../utils/portalContent'
import { reference, statusReferences } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { ArrowRight, ChevronRight, FileClock } from '@lucide/vue'
import { useRoute, useRouter } from 'vue-router'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
import { api } from '../api'
import type { Account, DisputeSummary, PageResult } from '../types'
import { useQuietLiveRefresh } from '../composables/useQuietLiveRefresh'
import { formatMoney } from '../utils/format'
import { caseDate, caseLabel, isClosedStatus } from '../utils/casePresentation'
import AppShell from '../components/AppShell.vue'
import CaseHistoryFilters from '../components/CaseHistoryFilters.vue'
import type { HistoryFilters } from '../utils/historyFilters'
import PageHeading from '../components/PageHeading.vue'
import StatusBadge from '../components/StatusBadge.vue'
const customerSearchEnabled = computed(
  () => reference.customerSearchEnabled === true || useAuthStore().isStaffWorkspace
)
const route = useRoute(),
  router = useRouter(),
  accounts = useAccountsStore(),
  data = ref<PageResult<DisputeSummary> | null>(null),
  loading = ref(true),
  error = ref(''),
  page = ref(Math.max(1, Number(route.query.page) || 1))
const filters = computed(() => [
  { key: '', label: 'All' },
  { key: 'active', label: 'Open' },
  { key: 'closed', label: 'Closed' },
  ...Object.entries(statusReferences)
    .sort((a, b) => (a[1].sortOrder ?? 0) - (b[1].sortOrder ?? 0))
    .map(([key, value]) => ({ key, label: value.customerLabel }))
])
const selected = ref(
  filters.value.some((f) => f.key === route.query.status) ? String(route.query.status) : 'active'
)
const viewingClosed = computed(
  () => selected.value === 'closed' || statusReferences[selected.value]?.closed === true
)
const search = ref(typeof route.query.search === 'string' ? route.query.search : '')
const appliedSearch = ref(search.value)
const from = ref(typeof route.query.from === 'string' ? route.query.from : ''),
  to = ref(typeof route.query.to === 'string' ? route.query.to : '')
const historyFilters = computed<HistoryFilters>(() => ({
  status: selected.value,
  account: accounts.selectedId,
  from: from.value,
  to: to.value,
  unreadOnly: false
}))
const filterSummary = computed(() =>
  [
    filters.value.find((item) => item.key === selected.value)?.label,
    accounts.items.find((item) => item.id === accounts.selectedId)?.accountType,
    from.value || to.value ? 'Date range' : ''
  ]
    .filter(Boolean)
    .join(' · ')
)
async function confirmFilters(value: HistoryFilters) {
  selected.value = value.status
  from.value = value.from
  to.value = value.to
  initializing = true
  accounts.selectedId = value.account
  await Promise.resolve()
  initializing = false
  page.value = 1
  await syncAndLoad()
}
async function selectCaseVisibility(status: 'active' | 'closed') {
  selected.value = status
  page.value = 1
  await syncAndLoad()
}
async function syncAndLoad() {
  await router.replace({
    query: {
      ...route.query,
      status: selected.value || undefined,
      from: from.value || undefined,
      to: to.value || undefined,
      account: accounts.selectedId || undefined,
      search: appliedSearch.value || undefined,
      page: page.value > 1 ? String(page.value) : undefined
    }
  })
  await load()
}
function searchCases() {
  appliedSearch.value = search.value.trim()
  page.value = 1
  void syncAndLoad()
}
function clearSearch() {
  search.value = ''
  searchCases()
}
let request: AbortController | undefined,
  generation = 0
const emptyTitle = computed(() =>
  appliedSearch.value
    ? 'No matching disputes'
    : selected.value === 'AwaitingCustomer'
      ? 'No replies needed'
      : selected.value === 'closed'
        ? 'No closed cases'
        : 'No disputes here'
)
async function fetchData(quiet = false) {
  const current = ++generation,
    owner = useAuthStore().user?.id
  request?.abort()
  request = new AbortController()
  if (!quiet) {
    loading.value = true
    error.value = ''
  }
  try {
    const [result, items] = await Promise.all([
      api.page<DisputeSummary>(
        '/api/v1/disputes',
        {
          page: page.value,
          pageSize: clientOption('disputePageSize'),
          accountId: accounts.selectedId,
          status: selected.value,
          search: customerSearchEnabled.value ? appliedSearch.value : undefined,
          from: from.value,
          to: to.value
        },
        request.signal
      ),
      quiet
        ? api.get<Account[]>('/api/v1/accounts', request.signal).catch(() => null)
        : Promise.resolve(null)
    ])
    if (current === generation) {
      data.value = result
      error.value = ''
      if (items && accounts.owner === owner) accounts.items = items
    }
  } catch {
    if (current === generation && (!quiet || !data.value))
      error.value = 'Please try loading your disputes again.'
  } finally {
    if (current === generation) {
      if (!quiet) loading.value = false
      liveReady()
    }
  }
}
async function load() {
  await fetchData()
}
async function changePage(next: number) {
  if (
    !data.value ||
    loading.value ||
    next < 1 ||
    next > Math.ceil(data.value.total / data.value.pageSize)
  )
    return
  page.value = next
  await syncAndLoad()
}
let initializing = true
const liveReady = useQuietLiveRefresh(
  () => fetchData(true),
  () => initializing || loading.value,
  ['disputes', 'banking']
)
onMounted(async () => {
  const auth = useAuthStore()
  try {
    if (auth.user) await accounts.load(auth.user.id)
    const id = route.query.account
    if (typeof id === 'string') {
      if (!accounts.items.some((a) => a.id === id)) {
        error.value = 'This account is unavailable.'
        loading.value = false
        return
      }
      accounts.selectedId = id
    }
    initializing = false
    await load()
  } catch {
    error.value = 'Accounts are unavailable.'
    loading.value = false
  }
})
onUnmounted(() => {
  generation++
  request?.abort()
})
watch(
  () => accounts.selectedId,
  () => {
    if (!initializing) {
      page.value = 1
      void syncAndLoad()
    }
  }
)
useFeedback(error, undefined, undefined)
</script>
<template>
  <AppShell class="customer-activity-page">
    <PageHeading title="My Disputes">
      <RouterLink
        to="/transactions"
        class="text-link heading-inline-action w3-button w3-theme-d5 w3-round-xlarge w3-padding w3-block"
      >
        New Dispute
        <ArrowRight :size="15" />
      </RouterLink>
    </PageHeading>
    <section
      class="surface-panel case-list-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <nav
        class="case-visibility-tabs"
        aria-label="Case History"
      >
        <button
          type="button"
          :aria-pressed="!viewingClosed"
          @click="selectCaseVisibility('active')"
        >
          Active Cases
        </button>
        <button
          type="button"
          :aria-pressed="viewingClosed"
          @click="selectCaseVisibility('closed')"
        >
          Closed Cases
        </button>
      </nav>
      <p
        v-if="viewingClosed"
        class="history-window-hint"
      >
        Closed in the last {{ clientOption('closedCaseHistoryDays') }} days
      </p>
      <div class="case-list-tools">
        <form
          v-if="customerSearchEnabled"
          class="filter-bar compact-transaction-toolbar w3-section w3-flex"
          role="search"
          @submit.prevent="searchCases"
        >
          <div class="w3-block w3-rest">
            <label class="search-field w3-block w3-text-dark-grey w3-medium">
              <input
                v-model="search"
                class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                type="search"
                maxlength="100"
                aria-label="Search Disputes"
                placeholder="Search disputes"
                @search="!search &amp;&amp; clearSearch()"
              />
            </label>
          </div>
          <div class="w3-padding-small">
            <CaseHistoryFilters
              :value="historyFilters"
              kind="disputes"
              :accounts="accounts.items"
              @apply="confirmFilters"
            />
          </div>
          <div class="w3-padding-small">
            <button
              type="submit"
              class="icon-button search-submit w3-button w3-round-large w3-block w3-border w3-white w3-large w3-padding-small"
              aria-label="Find Disputes"
            >
              <ArrowRight :size="18" />
            </button>
          </div>
        </form>
        <div class="list-filter-summary w3-bar w3-small w3-text-grey w3-padding-16">
          <span class="w3-left">{{ filterSummary }}</span>
          <span class="w3-right">
            {{ data?.total ?? 0 }} {{ data?.total === 1 ? 'case' : 'cases' }}
          </span>
        </div>
      </div>
      <p
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
        <button
          class="text-button w3-button w3-text-black w3-round-large"
          @click="load"
        >
          Retry
        </button>
      </p>
      <div
        v-if="loading"
        class="list-skeleton"
        aria-label="Loading Disputes"
      >
        <span
          v-for="n in 3"
          :key="n"
        />
      </div>
      <div
        v-else-if="data?.items.length"
        class="case-list"
      >
        <RouterLink
          v-for="item in data.items"
          :key="item.id"
          :to="'/disputes/' + item.id"
          class="case-list-item w3-button w3-block w3-left-align w3-padding-16 w3-border-bottom w3-hover-light-grey"
        >
          <span class="case-row-main">
            <b class="w3-block w3-large">{{ caseLabel(item) }}</b>
            <small
              v-if="isClosedStatus(item.status) && item.resolution"
              class="case-resolution-preview"
            >
              {{ item.resolution }}
            </small>
            <small
              v-if="item.transactionTypeLabel"
              class="w3-small w3-text-grey"
            >
              {{ item.transactionTypeLabel }}
            </small>
          </span>
          <span class="case-row-meta w3-small w3-text-grey">
            <span>{{ item.reference }}</span>
            <time :datetime="item.createdAt">{{ caseDate(item.createdAt) }}</time>
          </span>
          <span class="case-row-status">
            <StatusBadge :status="item.status" />
            <small v-if="item.status === 'AwaitingCustomer'">Reply Requested</small>
          </span>
          <b class="case-row-amount">{{ formatMoney(item.amount, item.currency) }}</b>
          <ChevronRight
            :size="18"
            class="case-row-arrow w3-text-blue-grey"
          />
        </RouterLink>
      </div>
      <div
        v-else-if="!error"
        class="empty-state w3-container w3-center w3-padding-32"
      >
        <FileClock :size="25" />
        <h2 class="w3-sans-serif w3-large">{{ emptyTitle }}</h2>
        <p>
          {{
            appliedSearch
              ? 'Try another case reference or merchant.'
              : selected
                ? 'Choose another filter to view your cases.'
                : 'Start from the transaction you want us to review.'
          }}
        </p>
      </div>
      <div
        v-if="data && data.total > data.pageSize"
        class="pagination w3-container w3-section w3-center"
      >
        <span>{{ page }} / {{ Math.max(1, Math.ceil(data.total / data.pageSize)) }}</span>
        <div>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="page <= 1 || loading"
            @click="changePage(page - 1)"
          >
            Previous
          </button>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="page * data.pageSize >= data.total || loading"
            @click="changePage(page + 1)"
          >
            Next
          </button>
        </div>
      </div>
    </section>
  </AppShell>
</template>
