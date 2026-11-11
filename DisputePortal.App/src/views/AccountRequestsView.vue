<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, onUnmounted, ref } from 'vue'
import { api, ApiError } from '../api'
import { formatDate } from '../utils/format'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import ChoiceSheet from '../components/ChoiceSheet.vue'
interface RequestRow {
  id: string
  reference: string
  status: string
  version: number
  customerName: string
  email: string | null
  reason: string
  decision: string | null
  createdAt: string
  reviewedAt: string | null
}
const rows = ref<RequestRow[]>([]),
  filter = ref(''),
  selected = ref(''),
  status = ref('UnderReview'),
  comment = ref(''),
  error = ref(''),
  notice = ref(''),
  busy = ref(false)
const visible = computed(() =>
  rows.value.filter((row) => !filter.value || row.status === filter.value)
)
const states = [
  { value: '', label: 'All Requests' },
  ...['Pending', 'UnderReview', 'Declined', 'Completed', 'Cancelled'].map((value) => ({
    value,
    label: value.replace(/([a-z])([A-Z])/g, '$1 $2')
  }))
]
let controller: AbortController | undefined,
  disposed = false
async function load() {
  controller?.abort()
  controller = new AbortController()
  try {
    const data = await api.get<RequestRow[]>(
      '/api/v1/account-management/requests',
      controller.signal
    )
    if (!disposed) rows.value = data
  } catch (cause) {
    if (!disposed && !controller.signal.aborted)
      error.value = cause instanceof ApiError ? cause.message : 'Requests are unavailable.'
  }
}
function edit(row: RequestRow) {
  selected.value = selected.value === row.id ? '' : row.id
  status.value = row.status === 'Pending' ? 'UnderReview' : 'Completed'
  comment.value = ''
  error.value = ''
  notice.value = ''
}
async function save(row: RequestRow) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    await api.post('/api/v1/account-management/requests/' + row.id + '/review', {
      expectedVersion: row.version,
      status: status.value,
      comment: comment.value.trim()
    })
    selected.value = ''
    notice.value = 'Request updated.'
    await load()
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : 'Please try again.'
    if (cause instanceof ApiError && cause.status === 409) await load()
  } finally {
    busy.value = false
  }
}
onMounted(load)
onUnmounted(() => {
  disposed = true
  controller?.abort()
})
useFeedback(error, undefined, notice)
</script>
<template>
  <AppShell>
    <PageHeading title="Account Closure Requests" />
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-if="notice"
      class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
      role="status"
    >
      {{ notice }}
    </p>
    <section class="account-requests">
      <ChoiceSheet
        v-model="filter"
        label="Request Status"
        :options="states"
      />
      <p
        v-if="!visible.length"
        class="muted w3-text-grey"
      >
        No requests here.
      </p>
      <article
        v-for="row in visible"
        :key="row.id"
        class="surface-panel account-request-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <header>
          <div>
            <h2 class="w3-sans-serif w3-large">{{ row.customerName }}</h2>
            <span>{{ row.reference }}</span>
          </div>
          <span
            class="request-status w3-tag w3-light-grey w3-round-large w3-small w3-padding-small"
          >
            {{ row.status.replace(/([a-z])([A-Z])/g, '$1 $2') }}
          </span>
        </header>
        <p v-if="row.email">
          {{ row.email }}
        </p>
        <time :datetime="row.createdAt">{{ formatDate(row.createdAt) }}</time>
        <p v-if="row.reason">
          {{ row.reason }}
        </p>
        <p v-if="row.decision">
          {{ row.decision }}
        </p>
        <button
          v-if="['Pending', 'UnderReview'].includes(row.status)"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          @click="edit(row)"
        >
          {{ selected === row.id ? 'Close Review' : 'Review Request' }}
        </button>
        <form
          v-if="selected === row.id"
          class="ops-form"
          @submit.prevent="save(row)"
        >
          <ChoiceSheet
            v-model="status"
            label="Next Step"
            :options="[
              {
                value: row.status === 'Pending' ? 'UnderReview' : 'Completed',
                label: row.status === 'Pending' ? 'Start Review' : 'Complete Closure'
              },
              { value: 'Declined', label: 'Decline Request' }
            ]"
          />
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Review Note
            <textarea
              v-model="comment"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              required
              minlength="10"
              maxlength="500"
              rows="3"
            />
          </label>
          <p
            v-if="status === 'Completed'"
            class="field-hint w3-small w3-text-grey w3-block"
          >
            Completing closure closes access, signs out every device and removes personal profile
            details. Open disputes must be resolved first.
          </p>
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="busy || comment.trim().length < 10"
          >
            {{ busy ? 'Saving…' : 'Save Decision' }}
          </button>
        </form>
      </article>
    </section>
  </AppShell>
</template>
