<script setup lang="ts">
import { reference } from '../portalReference'
import { clientOption } from '../utils/portalContent'
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Search, SlidersHorizontal, X } from '@lucide/vue'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
import { api, ApiError } from '../api'
import type { Account, PageResult, Transaction } from '../types'
import { useQuietLiveRefresh } from '../composables/useQuietLiveRefresh'
import AccountSwitcher from '../components/AccountSwitcher.vue'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import TransactionList from '../components/TransactionList.vue'

const customerSearchEnabled = computed(
  () => reference.customerSearchEnabled === true || useAuthStore().isStaffWorkspace
)
const router = useRouter()
const route = useRoute()
const accounts = useAccountsStore()
let request: AbortController | undefined
let generation = 0
onUnmounted(() => {
  generation++
  request?.abort()
})
const data = ref<PageResult<Transaction> | null>(null)
const loading = ref(true)
const error = ref('')
const search = ref(accounts.activity.search)
const direction = ref(accounts.activity.direction)
const type = ref(accounts.activity.type)
const sort = ref(accounts.activity.sort)
const from = ref(accounts.activity.from)
const to = ref(accounts.activity.to)
const page = ref(accounts.activity.page)
const pageSize = computed(() => clientOption('transactionPageSize'))
const transactionTypes = ref<{ typeKey: string; label: string }[]>([])
const dateRangeError = computed(() =>
  draft.value.from && draft.value.to && draft.value.from > draft.value.to
    ? 'End date must be on or after start date.'
    : ''
)
const filterSheet = ref<HTMLDialogElement | null>(null)
const draft = ref({ direction: '', type: '', from: '', to: '', sort: 'date_desc' })
function openFilters() {
  draft.value = {
    direction: direction.value,
    type: type.value,
    from: from.value,
    to: to.value,
    sort: sort.value
  }
  filterSheet.value?.showModal()
}
function confirmFilters() {
  if (dateRangeError.value) return
  direction.value = draft.value.direction
  type.value = draft.value.type
  from.value = draft.value.from
  to.value = draft.value.to
  sort.value = draft.value.sort
  applyFilters()
  filterSheet.value?.close()
}
function resetDraft() {
  draft.value = { direction: '', type: '', from: '', to: '', sort: 'date_desc' }
}
const filterCount = computed(
  () =>
    [
      direction.value,
      type.value,
      from.value,
      to.value,
      sort.value !== 'date_desc' ? sort.value : ''
    ].filter(Boolean).length
)
function resetFilters() {
  search.value = ''
  direction.value = ''
  type.value = ''
  from.value = ''
  to.value = ''
  sort.value = 'date_desc'
  applyFilters()
}

async function fetchData(quiet = false) {
  if (!quiet && from.value && to.value && from.value > to.value) {
    from.value = ''
    to.value = ''
  }
  if (!quiet)
    accounts.activity = {
      search: search.value,
      direction: direction.value,
      type: type.value,
      sort: sort.value,
      from: from.value,
      to: to.value,
      page: page.value
    }
  const applied = customerSearchEnabled.value
    ? accounts.activity
    : {
        ...accounts.activity,
        search: '',
        direction: '',
        type: '',
        from: '',
        to: '',
        sort: 'date_desc'
      }
  const owner = useAuthStore().user?.id
  const current = ++generation
  request?.abort()
  request = new AbortController()
  if (!quiet) {
    loading.value = true
    data.value = null
    error.value = ''
  }
  try {
    const [result, items] = await Promise.all([
      api.page<Transaction>(
        '/api/v1/transactions',
        {
          accountId: accounts.selectedId,
          page: applied.page,
          pageSize: pageSize.value,
          search: applied.search.trim(),
          direction: applied.direction,
          type: applied.type,
          from: applied.from,
          to: applied.to,
          sort: applied.sort
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
  } catch (cause) {
    if (current !== generation) return
    if (!quiet || !data.value)
      error.value = cause instanceof ApiError ? cause.message : 'We could not load transactions.'
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
  await load()
}
function clearSearch() {
  search.value = ''
  applyFilters()
}
function applyFilters() {
  page.value = 1
  void router.replace({ query: { ...route.query, account: accounts.selectedId || undefined } })
  void load()
}
function open(item: Transaction) {
  void router.push('/transactions/' + item.id)
}
let initializing = true
const liveReady = useQuietLiveRefresh(
  () => fetchData(true),
  () => initializing || loading.value,
  ['banking', 'disputes']
)
onMounted(async () => {
  const auth = useAuthStore()
  try {
    const [, types] = await Promise.all([
      auth.user ? accounts.load(auth.user.id) : Promise.resolve(),
      api.get<{ typeKey: string; label: string }[]>('/api/v1/transactions/types')
    ])
    transactionTypes.value = types
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
watch(
  () => accounts.selectedId,
  () => {
    if (!initializing) applyFilters()
  }
)
useFeedback(error, undefined, undefined)
</script>

<template>
  <AppShell class="customer-activity-page">
    <PageHeading title="Transactions" />
    <section
      class="surface-panel transaction-history w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <AccountSwitcher compact />
      <form
        v-if="customerSearchEnabled"
        class="filter-bar compact-transaction-toolbar w3-section w3-flex"
        @submit.prevent="applyFilters"
      >
        <div class="w3-block w3-rest">
          <label class="search-field w3-block w3-text-dark-grey w3-medium">
            <input
              v-model="search"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              aria-label="Search Transactions"
              type="search"
              maxlength="100"
              placeholder="Search transactions"
              @search="!search &amp;&amp; clearSearch()"
            />
          </label>
        </div>
        <div class="w3-padding-small">
          <button
            class="icon-button filter-toggle w3-button w3-round-large w3-block w3-border w3-white w3-large w3-padding-small"
            type="button"
            aria-label="Filters"
            aria-haspopup="dialog"
            @click="openFilters"
          >
            <SlidersHorizontal :size="19" />
            <span
              v-if="filterCount"
              class="filter-count"
            >
              {{ filterCount }}
            </span>
          </button>
        </div>
        <div class="w3-padding-small">
          <button
            class="icon-button search-submit filter-submit w3-button w3-round-large w3-block w3-border w3-white w3-large w3-padding-small"
            aria-label="Search Transactions"
            type="submit"
          >
            <Search :size="18" />
          </button>
        </div>
      </form>
      <dialog
        ref="filterSheet"
        class="filter-sheet w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
        aria-label="Transaction Filters"
        @click="
          (event) => {
            if (event.target === filterSheet) filterSheet?.close()
          }
        "
      >
        <div class="w3-bar">
          <div class="sheet-heading w3-bar w3-padding-16">
            <button
              type="button"
              class="icon-button w3-button w3-round-large w3-right"
              aria-label="Close Filters"
              @click="filterSheet?.close()"
            >
              <X :size="19" />
            </button>
            <h2 class="w3-sans-serif w3-large">Filter Transactions</h2>
          </div>
          <form @submit.prevent="confirmFilters">
            <div class="advanced-filter-grid">
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                <span class="filter-field-label w3-block w3-margin-top">Direction</span>
                <select
                  v-model="draft.direction"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  aria-label="Direction"
                >
                  <option value="">All Activity</option>
                  <option value="Debit">Money Out</option>
                  <option value="Credit">Money In</option>
                </select>
              </label>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                <span class="filter-field-label w3-block w3-margin-top">Type</span>
                <select
                  v-model="draft.type"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  aria-label="Transaction Type"
                >
                  <option value="">All Types</option>
                  <option
                    v-for="item in transactionTypes"
                    :key="item.typeKey"
                    :value="item.typeKey"
                  >
                    {{ item.label }}
                  </option>
                </select>
              </label>
              <fieldset class="transaction-date-range">
                <legend class="sr-only w3-hide">Transaction Date Range</legend>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  <span class="filter-field-label w3-block w3-margin-top">Start Date</span>
                  <input
                    v-model="draft.from"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    type="date"
                    aria-label="From Date"
                    :max="draft.to || undefined"
                  />
                </label>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  <span class="filter-field-label w3-block w3-margin-top">End Date</span>
                  <input
                    v-model="draft.to"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    type="date"
                    aria-label="To Date"
                    :min="draft.from || undefined"
                    :aria-invalid="!!dateRangeError"
                    :aria-describedby="dateRangeError ? 'date-range-error' : undefined"
                  />
                </label>
              </fieldset>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                <span class="filter-field-label w3-block w3-margin-top">Sort</span>
                <select
                  v-model="draft.sort"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  aria-label="Sort By"
                >
                  <option value="date_desc">Newest First</option>
                  <option value="date_asc">Oldest First</option>
                  <option value="amount_desc">Highest Amount</option>
                  <option value="amount_asc">Lowest Amount</option>
                </select>
              </label>
            </div>
            <p
              v-if="dateRangeError"
              id="date-range-error"
              class="date-range-error w3-panel w3-pale-red"
              role="alert"
            >
              {{ dateRangeError }}
            </p>
            <div class="filter-sheet-actions w3-section">
              <button
                type="button"
                class="text-button w3-button w3-text-black w3-round-large"
                @click="resetDraft"
              >
                Reset
              </button>
              <button
                type="submit"
                class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
                :disabled="!!dateRangeError"
              >
                Show Transactions
              </button>
            </div>
          </form>
        </div>
      </dialog>
      <div
        v-if="customerSearchEnabled && (filterCount || search)"
        class="filter-reset-row w3-bar w3-section"
      >
        <button
          type="button"
          class="text-button w3-button w3-text-black w3-round-large"
          @click="resetFilters"
        >
          Clear Filters
        </button>
      </div>
      <p
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
      </p>
      <div
        v-if="!error"
        class="history-caption w3-bar w3-padding-16 w3-small w3-text-grey"
      >
        <span>
          {{
            loading
              ? 'Loading activity…'
              : (data?.total ?? 0) + (data?.total === 1 ? ' transaction' : ' transactions')
          }}
        </span>
      </div>
      <TransactionList
        v-if="!error"
        :transactions="data?.items ?? []"
        :loading="loading"
        clickable
        @select="open"
      />
      <div
        v-if="data && data.total > data.pageSize"
        class="pagination transaction-pagination"
      >
        <span>
          Page {{ data.page }}
          of
          {{ Math.max(1, Math.ceil(data.total / data.pageSize)) }}
        </span>
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
