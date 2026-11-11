<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Activity, RefreshCw, Search, ShieldCheck } from '@lucide/vue'
import AppShell from '../components/AppShell.vue'
import { api } from '../api'
import { useFeedback } from '../composables/useFeedback'
interface Endpoint {
  method: string
  route: string
  statusCode: number
  requestCount: number
  failureCount: number
  clientErrorCount: number
  averageDurationMs: number
  minimumDurationMs: number
  maximumDurationMs: number
}
interface Overview {
  persisted: {
    from: string
    to: string
    requestCount: number
    failureCount: number
    clientErrorCount: number
    averageDurationMs: number
    lastPersistedAt: string | null
    endpoints: Endpoint[]
  }
  collection: { bufferedSeries: number; droppedMeasurements: number; droppedLogEvents?: number }
}
const data = ref<Overview | null>(null),
  loading = ref(false),
  error = ref(''),
  hours = ref(24),
  query = ref(''),
  status = ref('')
const rows = computed(
  () =>
    data.value?.persisted.endpoints.filter(
      (e) =>
        (!status.value || String(e.statusCode).startsWith(status.value)) &&
        (e.method + ' ' + e.route + ' ' + e.statusCode)
          .toLowerCase()
          .includes(query.value.trim().toLowerCase())
    ) ?? []
)
const number = (value: number) => value.toLocaleString(undefined, { maximumFractionDigits: 2 })
async function load() {
  if (loading.value) return
  loading.value = true
  error.value = ''
  try {
    const until = new Date(),
      from = new Date(until.getTime() - hours.value * 3600000)
    data.value = await api.get<Overview>(
      '/api/v1/admin/monitoring/overview?' +
        new URLSearchParams({ from: from.toISOString(), to: until.toISOString(), limit: '200' })
    )
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
}
onMounted(load)
useFeedback(error)
</script>
<template>
  <AppShell>
    <div class="monitoring-page">
      <header class="operations-page-heading w3-bar w3-section">
        <div>
          <p class="eyebrow w3-small w3-wide w3-text-grey">SERVICE PERFORMANCE</p>
          <h1 class="w3-xxlarge w3-sans-serif w3-text-black">API Monitoring</h1>
          <p>Stored request statistics from the portal's database.</p>
        </div>
        <button
          class="icon-button w3-button w3-round-large"
          aria-label="Refresh Monitoring"
          :disabled="loading"
          @click="load"
        >
          <RefreshCw :size="20" />
        </button>
      </header>
      <div class="monitoring-controls w3-section">
        <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
          Period
          <select
            v-model="hours"
            class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
            @change="load"
          >
            <option :value="1">Last Hour</option>
            <option :value="24">Last 24 Hours</option>
            <option :value="168">Last 7 Days</option>
          </select>
        </label>
        <label class="search-field w3-block w3-text-dark-grey w3-medium">
          <Search :size="18" />
          <input
            v-model="query"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            type="search"
            placeholder="Search Routes Or Methods"
            aria-label="Search Monitoring"
          />
        </label>
        <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
          Status
          <select
            v-model="status"
            class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
          >
            <option value="">All Statuses</option>
            <option value="2">Successful</option>
            <option value="3">Redirects</option>
            <option value="4">Client Errors</option>
            <option value="5">Server Errors</option>
          </select>
        </label>
      </div>
      <div
        v-if="data"
        class="operations-summary w3-section"
      >
        <div>
          <Activity :size="22" />
          <span>
            Requests
            <strong>{{ number(data.persisted.requestCount) }}</strong>
          </span>
        </div>
        <div>
          <ShieldCheck :size="22" />
          <span>
            Server Failures
            <strong>{{ number(data.persisted.failureCount) }}</strong>
          </span>
        </div>
        <div>
          <Activity :size="22" />
          <span>
            Average Duration
            <strong>{{ number(data.persisted.averageDurationMs) }} ms</strong>
          </span>
        </div>
      </div>
      <section
        class="surface-panel monitoring-results w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <div class="account-section-heading w3-bar w3-section">
          <div>
            <h2 class="w3-sans-serif w3-large">Endpoint Performance</h2>
            <p>Up to 200 endpoint groups. HTTP 4xx are counted separately from server failures.</p>
          </div>
        </div>
        <p
          v-if="loading"
          role="status"
        >
          Loading Stored Metrics…
        </p>
        <div
          v-if="rows.length"
          class="monitoring-table-wrap w3-responsive"
        >
          <table class="w3-table w3-striped w3-bordered">
            <thead>
              <tr>
                <th>Endpoint</th>
                <th>Status</th>
                <th>Requests</th>
                <th>Failures</th>
                <th>Average</th>
                <th>Maximum</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in rows"
                :key="row.method + row.route + row.statusCode"
              >
                <td>
                  <b>{{ row.method }}</b>
                  <code>{{ row.route }}</code>
                </td>
                <td>{{ row.statusCode || 'Aborted' }}</td>
                <td>{{ number(row.requestCount) }}</td>
                <td>{{ number(row.failureCount) }}</td>
                <td>{{ number(row.averageDurationMs) }} ms</td>
                <td>{{ number(row.maximumDurationMs) }} ms</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div
          v-else-if="!loading"
          class="empty-state w3-container w3-center w3-padding-32"
        >
          <Activity :size="28" />
          <h3 class="w3-large w3-sans-serif">No Matching Metrics</h3>
          <p>Completed requests appear after the next aggregation flush.</p>
        </div>
      </section>
      <p
        v-if="data"
        class="monitoring-footnote w3-small w3-text-grey"
      >
        Last Saved:
        {{
          data.persisted.lastPersistedAt
            ? new Date(data.persisted.lastPersistedAt).toLocaleString()
            : 'Awaiting First Save'
        }}
        · Buffered Groups:
        {{ data.collection.bufferedSeries }}
        · Dropped Measurements:
        {{ data.collection.droppedMeasurements }}
      </p>
    </div>
  </AppShell>
</template>
