<script setup lang="ts">
import { ArrowUpRight } from '@lucide/vue'
import { clientOption } from '../utils/portalContent'
import { isClosedStatus, reference, statusReferences } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, onBeforeUnmount, ref } from 'vue'
import { ChevronRight, Search, ShieldCheck } from '@lucide/vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { caseTeams, priorityLabels, type StaffUser } from '../operations'
import { api } from '../api'
import { useQuietLiveRefresh } from '../composables/useQuietLiveRefresh'
import type { AdminQueueItem, DashboardMetrics, PageResult } from '../types'
import { formatDate, formatMoney } from '../utils/format'
import { caseOwnerLabel } from '../utils/casePresentation'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ChoiceSheet from '../components/ChoiceSheet.vue'
import CaseHistoryFilters from '../components/CaseHistoryFilters.vue'
import {
  emptyHistoryFilters,
  historyFilterQuery,
  type HistoryFilters
} from '../utils/historyFilters'
import CaseTimers from '../components/CaseTimers.vue'
import CaseClaimAction from '../components/CaseClaimAction.vue'
const auth = useAuthStore(),
  router = useRouter(),
  route = useRoute()
const staff = ref<StaffUser[]>([]),
  savingAvailability = ref(false)
const currentAgent = computed(() => staff.value.find((person) => person.id === auth.user?.id))
const data = ref<PageResult<AdminQueueItem> | null>(null),
  metrics = ref<DashboardMetrics | null>(null)
const loading = ref(true),
  error = ref(''),
  secondaryError = ref('')
const search = ref(typeof route.query.search === 'string' ? route.query.search : '')
const appliedSearch = ref(search.value)
const status = ref(typeof route.query.status === 'string' ? route.query.status : ''),
  page = ref(1)
const history = ref<HistoryFilters>({
  ...emptyHistoryFilters(),
  status: status.value,
  from: typeof route.query.from === 'string' ? route.query.from : '',
  to: typeof route.query.to === 'string' ? route.query.to : '',
  priority: typeof route.query.priority === 'string' ? route.query.priority : undefined
})
async function applyHistory(value: HistoryFilters) {
  history.value = value
  await filter(value.status)
}
const statusOptions = computed(() => [
  { value: 'active', label: 'Active Cases' },
  ...Object.entries(statusReferences).map(([value, status]) => ({ value, label: status.label })),
  { value: 'closed', label: 'Completed Cases' },
  { value: '', label: 'All Cases' }
])
const availabilityOptions = computed(() =>
  (reference.availability ?? []).map((value) => ({ value, label: value }))
)
let controller: AbortController | undefined
async function fetchData(quiet = false) {
  controller?.abort()
  const request = new AbortController()
  controller = request
  if (!quiet) {
    loading.value = true
    error.value = ''
    secondaryError.value = ''
    appliedSearch.value = search.value
  }
  try {
    const [queue, dashboard, roster] = await Promise.allSettled([
      api.page<AdminQueueItem>(
        '/api/v1/admin/disputes',
        {
          page: page.value,
          pageSize: clientOption('staffPageSize'),
          search: appliedSearch.value,
          ...historyFilterQuery({ ...history.value, status: status.value })
        },
        request.signal
      ),
      api.get<DashboardMetrics>('/api/v1/admin/dashboard', request.signal),
      api.get<StaffUser[]>('/api/v1/operations/staff', request.signal)
    ])
    if (request.signal.aborted) return
    if (queue.status === 'rejected') throw queue.reason
    data.value = queue.value
    error.value = ''
    secondaryError.value = ''
    if (dashboard.status === 'fulfilled') metrics.value = dashboard.value
    else if (!quiet) metrics.value = null
    if (roster.status === 'fulfilled') staff.value = roster.value
    else if (!quiet) staff.value = []
    if (dashboard.status === 'rejected' || roster.status === 'rejected')
      secondaryError.value = 'Workload details are temporarily unavailable.'
  } catch {
    if (!request.signal.aborted && (!quiet || !data.value))
      error.value = 'The queue is temporarily unavailable. Please retry.'
  } finally {
    if (!request.signal.aborted) {
      if (!quiet) loading.value = false
      liveReady()
    }
  }
}
async function load() {
  await fetchData()
}
const liveReady = useQuietLiveRefresh(
  () => fetchData(true),
  () => loading.value,
  ['disputes', 'administration']
)
async function filter(value = status.value) {
  status.value = value
  history.value.status = value
  page.value = 1
  await router.replace({
    query: {
      ...route.query,
      search: search.value || undefined,
      ...historyFilterQuery(history.value)
    }
  })
  await load()
}
async function setAvailability(value: string) {
  if (savingAvailability.value) return
  savingAvailability.value = true
  try {
    await api.post('/api/v1/operations/availability', { availability: value })
    await load()
  } catch {
    error.value = 'Your work status was not updated. Please retry.'
  } finally {
    savingAvailability.value = false
  }
}
async function openClaimed(item: AdminQueueItem) {
  await router.push({ path: '/admin/disputes/' + item.dispute.id, query: { section: 'overview' } })
}
onMounted(load)
onBeforeUnmount(() => controller?.abort())
useFeedback(error, undefined, undefined)
</script>
<template>
  <AppShell>
    <PageHeading
      title="Case Management"
      description="Active cases and completed history."
    />
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
    <p
      v-if="secondaryError"
      class="queue-service-notice w3-panel w3-pale-yellow w3-round-large w3-padding"
      role="status"
    >
      {{ secondaryError }}
      <button
        class="text-button w3-button w3-text-black w3-round-large"
        aria-label="Retry Workload Details"
        @click="load"
      >
        Retry
      </button>
    </p>
    <section
      v-if="currentAgent"
      class="case-workload w3-row w3-white w3-border w3-round-large w3-container w3-padding-16 w3-margin-bottom"
    >
      <div class="w3-col s12 m8 w3-padding-small">
        <b class="w3-block">Your Workload</b>
        <span class="w3-block w3-small w3-text-grey">
          {{ currentAgent.activeCases }} / {{ currentAgent.capacity }} active cases
        </span>
      </div>
      <div class="w3-col s12 m4 w3-padding-small">
        <ChoiceSheet
          :model-value="currentAgent.availability"
          label="Your Availability"
          :options="availabilityOptions"
          :disabled="savingAvailability"
          @update:model-value="setAvailability"
        />
      </div>
    </section>
    <section
      class="case-management-metrics w3-row-padding w3-margin-bottom"
      aria-label="Case Overview"
    >
      <div class="w3-col s6 m3 w3-margin-bottom">
        <button
          class="w3-button w3-white w3-border w3-block w3-round-large w3-padding-16 w3-left-align"
          @click="filter('Submitted')"
        >
          <b class="w3-block w3-xlarge">{{ metrics?.submitted ?? '—' }}</b>
          <span class="w3-block w3-small w3-text-grey">
            {{ statusReferences.Submitted?.label ?? 'Submitted' }}
          </span>
        </button>
      </div>
      <div class="w3-col s6 m3 w3-margin-bottom">
        <button
          class="w3-button w3-white w3-border w3-block w3-round-large w3-padding-16 w3-left-align"
          @click="filter('UnderReview')"
        >
          <b class="w3-block w3-xlarge">{{ metrics?.underReview ?? '—' }}</b>
          <span class="w3-block w3-small w3-text-grey">
            {{ statusReferences.UnderReview?.label ?? 'UnderReview' }}
          </span>
        </button>
      </div>
      <div class="w3-col s6 m3 w3-margin-bottom">
        <button
          class="w3-button w3-white w3-border w3-block w3-round-large w3-padding-16 w3-left-align"
          @click="filter('AwaitingCustomer')"
        >
          <b class="w3-block w3-xlarge">{{ metrics?.awaitingCustomer ?? '—' }}</b>
          <span class="w3-block w3-small w3-text-grey">
            {{ statusReferences.AwaitingCustomer?.label ?? 'AwaitingCustomer' }}
          </span>
        </button>
      </div>
      <div class="w3-col s6 m3 w3-margin-bottom">
        <button
          class="w3-button w3-white w3-border w3-block w3-round-large w3-padding-16 w3-left-align"
          @click="filter('closed')"
        >
          <b class="w3-block w3-xlarge"><ArrowUpRight :size="24" /></b>
          <span class="w3-block w3-small w3-text-grey">Closed Cases</span>
        </button>
      </div>
    </section>
    <section
      class="case-management-workspace w3-white w3-border w3-round-large w3-container w3-padding-large"
      aria-label="Cases"
    >
      <ChoiceSheet
        :model-value="status"
        label="Case Status"
        :options="statusOptions"
        @update:model-value="filter"
      />
      <form
        class="filter-bar compact-transaction-toolbar w3-section w3-flex"
        role="search"
        @submit.prevent="filter()"
      >
        <div class="w3-block w3-rest">
          <label class="search-field w3-block w3-text-dark-grey w3-medium">
            <input
              v-model="search"
              type="search"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              aria-label="Search Disputes"
              placeholder="Reference, customer or merchant"
              maxlength="100"
            />
          </label>
        </div>
        <div class="w3-padding-small">
          <CaseHistoryFilters
            :value="history"
            kind="disputes"
            staff
            hide-status
            @apply="applyHistory"
          />
        </div>
        <div class="w3-padding-small">
          <button
            class="icon-button search-submit w3-button w3-round-large w3-block w3-border w3-white w3-large w3-padding-small"
            type="submit"
            aria-label="Search Cases"
          >
            <Search :size="18" />
          </button>
        </div>
      </form>
      <p class="result-count w3-small w3-text-grey w3-section">
        {{ data?.total ?? '—' }} {{ data?.total === 1 ? 'case' : 'cases' }}
      </p>
      <div
        v-if="loading"
        class="list-skeleton"
        aria-label="Loading Cases"
      >
        <span
          v-for="item in 4"
          :key="item"
        />
      </div>
      <div
        v-else-if="data?.items.length"
        class="case-management-list w3-row-padding"
      >
        <article
          v-for="item in data.items"
          :key="item.dispute.id"
          class="case-management-card w3-col s12 l6 w3-margin-bottom"
        >
          <div class="w3-white w3-border w3-round-large w3-padding-16 w3-container">
            <RouterLink
              :to="'/admin/disputes/' + item.dispute.id"
              class="case-management-link w3-button w3-block w3-left-align w3-round-large w3-padding-small w3-hover-light-grey"
            >
              <header class="w3-cell-row">
                <span class="case-number w3-cell w3-cell-middle w3-small w3-text-grey">
                  {{ item.dispute.reference }}
                </span>
                <span class="w3-cell w3-cell-middle w3-right-align">
                  <StatusBadge
                    :status="item.dispute.status"
                    staff
                  />
                </span>
              </header>
              <div class="case-management-title w3-cell-row w3-padding-16">
                <h2 class="w3-cell w3-cell-middle w3-large w3-sans-serif">
                  {{ item.merchantName || item.dispute.transactionDescription }}
                </h2>
                <b class="w3-cell w3-cell-middle w3-right-align">
                  {{ formatMoney(item.dispute.amount, item.dispute.currency) }}
                </b>
              </div>
              <p class="case-management-customer w3-text-grey w3-small">{{ item.customerName }}</p>
              <div
                v-if="item.operations"
                class="case-management-meta w3-small w3-text-grey w3-padding-16"
              >
                <span class="w3-block">
                  {{ caseTeams[item.operations.queueType]?.label ?? item.operations.queueType }}
                </span>
                <span
                  v-if="!isClosedStatus(item.dispute.status)"
                  class="w3-block"
                >
                  {{ priorityLabels[item.dispute.priority] }} Priority ·
                  {{ item.operations.severity }} Severity
                </span>
              </div>
              <div class="case-management-owner w3-border-top w3-padding-16 w3-small">
                <span>
                  {{ caseOwnerLabel(item.dispute.status, item.operations?.assignedName) }}
                </span>
                <ChevronRight
                  :size="18"
                  class="w3-right"
                />
              </div>
              <div
                v-if="item.operations"
                class="case-management-target w3-light-grey w3-round w3-padding-small w3-small"
              >
                <time
                  v-if="isClosedStatus(item.dispute.status)"
                  :datetime="item.operations.slaClosedAt || item.dispute.createdAt"
                >
                  {{ statusReferences[item.dispute.status]?.label ?? item.dispute.status }}
                  {{ formatDate(item.operations.slaClosedAt || item.dispute.createdAt) }}
                </time>
                <CaseTimers
                  v-else
                  :item="item.operations"
                  compact
                />
              </div>
            </RouterLink>
            <div
              v-if="!isClosedStatus(item.dispute.status)"
              class="case-management-actions w3-row-padding w3-section"
            >
              <div
                v-if="item.operations?.actions?.canManage === true"
                class="case-action-slot w3-col s12 m6 w3-margin-bottom"
              >
                <RouterLink
                  :to="{
                    path: '/admin/disputes/' + item.dispute.id,
                    query: { section: 'overview' }
                  }"
                  class="button button-secondary full-width w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                >
                  Manage Case
                </RouterLink>
              </div>
              <div
                v-if="item.operations && !item.operations.assignedTo"
                class="w3-col s12 m6 w3-margin-bottom"
              >
                <CaseClaimAction
                  :item="item.operations"
                  :staff="currentAgent"
                  @claimed="openClaimed(item)"
                  @refresh="load"
                />
              </div>
            </div>
          </div>
        </article>
      </div>
      <div
        v-else
        class="empty-state compact-empty w3-container w3-center w3-padding-32 w3-padding-16"
      >
        <ShieldCheck :size="28" />
        <b>No matching cases</b>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          @click="
            () => {
              search = ''
              history = emptyHistoryFilters()
              filter('active')
            }
          "
        >
          Clear Filters
        </button>
      </div>
      <div
        v-if="data && data.total > data.pageSize"
        class="pagination w3-container w3-section w3-center"
      >
        <span>
          Page {{ page }}
          of
          {{ Math.max(1, Math.ceil(data.total / data.pageSize)) }}
        </span>
        <div>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="page <= 1 || loading"
            @click="
              () => {
                page--
                load()
              }
            "
          >
            Previous
          </button>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="page * data.pageSize >= data.total || loading"
            @click="
              () => {
                page++
                load()
              }
            "
          >
            Next
          </button>
        </div>
      </div>
    </section>
  </AppShell>
</template>
