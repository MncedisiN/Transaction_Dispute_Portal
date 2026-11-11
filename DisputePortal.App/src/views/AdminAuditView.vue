<script setup lang="ts">
import DisclosurePanel from '../components/DisclosurePanel.vue'
import { clientOption } from '../utils/portalContent'
import { useFeedback } from '../composables/useFeedback'

import ActionActor from '../components/ActionActor.vue'
import type { ActionActor as ActorIdentity } from '../types'
import { onMounted, onBeforeUnmount, ref } from 'vue'
import { ShieldCheck } from '@lucide/vue'
import { api } from '../api'
import type { PageResult } from '../types'
import { formatDate } from '../utils/format'
import { auditDetails, auditTitle } from '../utils/auditPresentation'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import ChoiceSheet from '../components/ChoiceSheet.vue'
interface AuditRecord {
  actorIdentity?: ActorIdentity | null
  source?: string
  entityId?: string | null
  correlationId?: string
  id: string
  actorName: string
  action: string
  entityType: string
  occurredAt: string
  safeMetadata: string
  outcome?: string
}
const data = ref<PageResult<AuditRecord> | null>(null)
const action = ref(''),
  page = ref(1),
  loading = ref(true),
  error = ref('')
const options = [
  { value: '', label: 'All Activity' },
  ...[
    'PolicyVersionPublished',
    'PolicyVersionSupersessionScheduled',
    'PolicyVersionCopy',
    'PolicyVersionRestore',
    'PolicyVersionDeleted',
    'PolicyHistoryMigrated',
    'SettingsVersionPublished',
    'SettingsVersionDeleted',
    'SettingsHistoryMigrated',
    'DisputeStatusChanged',
    'CaseEscalated',
    'CaseWorkflowStarted',
    'CaseAssigned',
    'CaseTriaged',
    'UserCreated',
    'UserNameChanged',
    'UserAccessChanged',
    'EvidenceAttached',
    'EvidencePreviewRequested',
    'EvidenceAccessGranted',
    'EvidenceAccessRevoked',
    'EvidenceDownloadRequested',
    'DisputeRulesChanged',
    'DisputePolicyDefaultsRestored',
    'ProfileDetailsChanged',
    'ContactVerified',
    'PasswordReset',
    'PasswordChanged',
    'SignIn',
    'SignOut',
    'PasswordCheck',
    'AutomaticDisputeDecision',
    'DisputeRefundApproved',
    'DisputeRefundPosted',
    'RefundFailed',
    'DisputeReopened'
  ].map((value) => ({ value, label: auditTitle(value) }))
]
let controller: AbortController | undefined
async function load() {
  controller?.abort()
  const request = new AbortController()
  controller = request
  loading.value = true
  error.value = ''
  try {
    data.value = await api.page<AuditRecord>(
      '/api/v1/admin/audit-logs',
      { page: page.value, pageSize: clientOption('staffPageSize'), action: action.value },
      request.signal
    )
  } catch {
    if (!request.signal.aborted)
      error.value = 'Audit history is temporarily unavailable. Please retry.'
  } finally {
    if (!request.signal.aborted) loading.value = false
  }
}
function filter(value: string) {
  action.value = value
  page.value = 1
  void load()
}
onMounted(load)
onBeforeUnmount(() => controller?.abort())
useFeedback(error, undefined, undefined)
function changePage(amount: number) {
  page.value += amount
  void load()
}
</script>
<template>
  <AppShell>
    <PageHeading
      title="Audit History"
      description="Account and case activity."
    />
    <div
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
    </div>
    <section class="activity-panel">
      <div class="operations-toolbar">
        <ChoiceSheet
          :model-value="action"
          label="Activity Type"
          :options="options"
          @update:model-value="filter"
        />
        <span class="result-count w3-small w3-text-grey w3-section">
          {{ data?.total ?? '—' }} {{ data?.total === 1 ? 'event' : 'events' }}
        </span>
      </div>
      <div
        v-if="loading"
        class="list-skeleton"
        aria-label="Loading Activity"
      >
        <span
          v-for="item in 3"
          :key="item"
        />
      </div>
      <div
        v-else-if="data?.items.length"
        class="activity-feed"
      >
        <article
          v-for="event in data.items"
          :key="event.id"
          class="activity-event w3-padding-16 w3-border-bottom"
        >
          <span class="activity-mark"><ShieldCheck :size="20" /></span>
          <div class="activity-content w3-section">
            <h2 class="w3-sans-serif w3-large">{{ auditTitle(event.action) }}</h2>
            <div>
              <ActionActor
                :identity="event.actorIdentity"
                :fallback="event.actorName"
              />
              <span
                v-if="event.outcome"
                class="request-status w3-tag w3-light-grey w3-round-large w3-small w3-padding-small"
              >
                {{ event.outcome }}
              </span>
            </div>
            <time :datetime="event.occurredAt">{{ formatDate(event.occurredAt) }}</time>
            <DisclosurePanel
              v-if="
                event.entityId || event.correlationId || auditDetails(event.safeMetadata).length
              "
              class="activity-details"
            >
              <template #heading>View Details</template>
              <div class="w3-responsive">
                <table class="w3-table w3-bordered">
                  <tbody>
                    <tr v-if="event.entityId">
                      <th
                        class="w3-mobile w3-text-grey w3-small"
                        scope="row"
                      >
                        {{ event.entityType }} Record
                      </th>
                      <td class="w3-mobile">{{ event.entityId }}</td>
                    </tr>
                    <tr v-if="event.correlationId">
                      <th
                        class="w3-mobile w3-text-grey w3-small"
                        scope="row"
                      >
                        Request Correlation
                      </th>
                      <td class="w3-mobile">{{ event.correlationId }}</td>
                    </tr>
                    <tr
                      v-for="field in auditDetails(event.safeMetadata)"
                      :key="field.label"
                    >
                      <th
                        class="w3-mobile w3-text-grey w3-small"
                        scope="row"
                      >
                        {{ field.label }}
                      </th>
                      <td class="w3-mobile">{{ field.value }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </DisclosurePanel>
          </div>
        </article>
      </div>
      <div
        v-else
        class="empty-state compact-empty w3-container w3-center w3-padding-32 w3-padding-16"
      >
        <b>No activity found</b>
        <p>Choose another activity type.</p>
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
            @click="changePage(-1)"
          >
            Previous
          </button>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="page * data.pageSize >= data.total || loading"
            @click="changePage(1)"
          >
            Next
          </button>
        </div>
      </div>
    </section>
  </AppShell>
</template>
