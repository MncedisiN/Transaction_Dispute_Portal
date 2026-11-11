<script setup lang="ts">
import PortalDialog from './PortalDialog.vue'
import { ref, onMounted, onUnmounted } from 'vue'
import { api, ApiError } from '../api'
import { loadCaseStatuses, type CaseStatus, type CaseStatusCatalogue } from '../caseStatuses'
import { useFeedback } from '../composables/useFeedback'
import { loadPortalReference } from '../portalReference'
const catalogue = ref<CaseStatusCatalogue | null>(null),
  error = ref(''),
  notice = ref(''),
  busy = ref(false)
const selected = ref<CaseStatus | null>(null),
  reason = ref(''),
  token = ref(0)
let controller: AbortController | undefined
async function load() {
  controller?.abort()
  const current = new AbortController()
  controller = current
  try {
    catalogue.value = await loadCaseStatuses(current.signal)
  } catch (e) {
    if (!current.signal.aborted)
      error.value = e instanceof ApiError ? e.message : 'Workflow descriptions are unavailable.'
  }
}
function choose(row: CaseStatus) {
  error.value = ''
  selected.value = { ...row }
  token.value = catalogue.value?.revision ?? 0
  reason.value = ''
}
function label(code: string) {
  return catalogue.value?.items.find((row) => row.code === code)?.displayName ?? code
}
async function refreshToken() {
  await load()
  token.value = catalogue.value?.revision ?? token.value
}
async function save() {
  if (!selected.value || busy.value) return
  busy.value = true
  try {
    await api.post(`/api/v1/lookups/case-statuses/${encodeURIComponent(selected.value.code)}`, {
      expectedRevision: token.value,
      displayName: selected.value.displayName,
      customerLabel: selected.value.customerLabel,
      nextStep: selected.value.nextStep,
      sortOrder: selected.value.sortOrder,
      reason: reason.value
    })
    selected.value = null
    await Promise.all([load(), loadPortalReference(true)])
    notice.value =
      'Workflow descriptions were saved. Case history and financial values are unchanged.'
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'The descriptions could not be saved.'
    await load()
  } finally {
    busy.value = false
  }
}
onMounted(load)
onUnmounted(() => controller?.abort())
useFeedback(error, notice, undefined)
</script>
<template>
  <section
    class="surface-panel ops-panel catalogue w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <h2 class="w3-sans-serif w3-large">Workflow Statuses</h2>
    <p class="field-hint w3-small w3-text-grey w3-block">
      Codes and history are retained. These descriptions do not authorise case actions or establish
      that a refund was paid.
    </p>
    <div class="table-scroll w3-responsive">
      <table class="w3-table w3-striped w3-bordered">
        <thead>
          <tr>
            <th>Code</th>
            <th>Display Name</th>
            <th>Case State</th>
            <th>Workflow Paths</th>
            <th>Next Step</th>
            <th>Descriptions</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="row in catalogue?.items"
            :key="row.code"
          >
            <td>
              <code>{{ row.code }}</code>
            </td>
            <td>{{ row.displayName }}</td>
            <td>{{ row.closed ? 'Closed' : 'Active' }}</td>
            <td>{{ row.workflowTargets.map(label).join(', ') }}</td>
            <td>{{ row.nextStep }}</td>
            <td>
              <button
                type="button"
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                @click="choose(row)"
              >
                Update Descriptions
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <PortalDialog
      v-if="selected"
      :title="'Update Descriptions · ' + selected.displayName"
      :busy="busy"
      @close="selected = null"
    >
      <form
        class="ops-form w3-bar"
        @submit.prevent="save"
      >
        <p class="status-editor-code">{{ selected.code }}</p>
        <p class="field-hint w3-small w3-text-grey w3-block">
          The workflow code and permitted transitions stay unchanged.
        </p>
        <div class="policy-editor-fields">
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Display Name
            <input
              v-model="selected.displayName"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              maxlength="120"
              required
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Customer Label
            <input
              v-model="selected.customerLabel"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              maxlength="120"
              required
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Next Step
            <textarea
              v-model="selected.nextStep"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              rows="3"
              maxlength="500"
              required
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Display Order
            <input
              v-model.number="selected.sortOrder"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              type="number"
              min="0"
              max="1000"
              required
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Reason For Change
            <textarea
              v-model="reason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              rows="3"
              minlength="10"
              maxlength="500"
              required
            />
          </label>
        </div>
        <button
          class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
          :disabled="busy"
        >
          Save Descriptions
        </button>
        <button
          type="button"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="busy"
          @click="refreshToken"
        >
          Refresh Catalogue
        </button>
        <button
          type="button"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="busy"
          @click="selected = null"
        >
          Cancel
        </button>
      </form>
    </PortalDialog>
  </section>
</template>
