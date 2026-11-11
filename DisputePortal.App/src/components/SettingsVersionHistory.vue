<script setup lang="ts">
import PortalDialog from './PortalDialog.vue'
import SettingsComparison from './SettingsComparison.vue'
import { onMounted, onUnmounted, ref } from 'vue'
import { api, ApiError } from '../api'
import type { Settings } from '../platform'
import type { PolicyVersion, PolicyResolution } from '../policyVersions'
import { formatDate } from '../utils/format'
import { useFeedback } from '../composables/useFeedback'
interface Revision extends PolicyVersion {
  settings: Settings
}
interface History {
  items: Revision[]
  total: number
  concurrencyToken: number
  effective: PolicyResolution
}
const emit = defineEmits<{ published: [] }>()
const history = ref<History | null>(null),
  page = ref(1),
  search = ref(''),
  selected = ref<Revision | null>(null),
  reason = ref(''),
  mode = ref('Copy'),
  preview = ref<Settings | null>(null),
  error = ref(''),
  notice = ref(''),
  busy = ref(false)
const requestId = ref(''),
  token = ref(0),
  removal = ref<PolicyResolution | null>(null)
let request: AbortController | undefined
async function load() {
  request?.abort()
  request = new AbortController()
  try {
    history.value = await api.get<History>(
      `/api/v1/platform/settings/history?page=${page.value}&pageSize=10&search=${encodeURIComponent(search.value)}`,
      request.signal
    )
  } catch (e) {
    if (!request.signal.aborted)
      error.value = e instanceof ApiError ? e.message : 'Configuration history unavailable.'
  }
}
function choose(row: Revision, operation: string) {
  error.value = ''
  selected.value = row
  mode.value = operation
  token.value = history.value?.concurrencyToken ?? 0
  requestId.value = crypto.randomUUID()
  reason.value = ''
  preview.value = null
  removal.value = null
}
async function refreshToken() {
  await load()
  token.value = history.value?.concurrencyToken ?? token.value
  requestId.value = crypto.randomUUID()
  preview.value = null
  removal.value = null
}
function body() {
  return {
    settings: selected.value!.settings,
    expectedVersion: selected.value!.version,
    concurrencyToken: token.value,
    requestId: requestId.value,
    sourceVersionPublicId: selected.value!.versionPublicId,
    operation: mode.value,
    reason: reason.value
  }
}
async function review() {
  busy.value = true
  try {
    if (mode.value === 'Remove')
      removal.value = await api.post<PolicyResolution>(
        `/api/v1/platform/settings/versions/${selected.value!.versionPublicId}/removal-preview`,
        { concurrencyToken: token.value, reason: reason.value }
      )
    else preview.value = await api.post<Settings>('/api/v1/platform/settings/preview', body())
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Preview unavailable.'
  } finally {
    busy.value = false
  }
}
async function confirm() {
  busy.value = true
  try {
    if (mode.value === 'Remove') {
      await api.post(
        `/api/v1/platform/settings/versions/${selected.value!.versionPublicId}/remove`,
        {
          concurrencyToken: token.value,
          reason: reason.value
        }
      )
    } else {
      await api.post('/api/v1/platform/settings', body())
    }
    selected.value = null
    await load()
    emit('published')
    notice.value = 'Configuration history was updated without overwriting published content.'
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'The change could not be published.'
    preview.value = null
    removal.value = null
    await load()
  } finally {
    busy.value = false
  }
}
onMounted(load)
onUnmounted(() => request?.abort())
useFeedback(error, notice, undefined)
function clearPreview() {
  preview.value = null
  removal.value = null
}
function changePage(amount: number) {
  page.value += amount
  void load()
}
function searchHistory() {
  page.value = 1
  void load()
}
</script>
<template>
  <section
    class="surface-panel settings-history w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <h2 class="w3-sans-serif w3-large">Configuration Version History</h2>
    <p>
      Country, registration, account and financial settings are retained as complete snapshots.
      Country changes remain immediate and recheck financial eligibility; they cannot be scheduled
      around that safeguard.
    </p>
    <form
      class="ops-form w3-bar"
      @submit.prevent="searchHistory"
    >
      <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
        Search History
        <input
          v-model="search"
          class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          type="search"
        />
      </label>
      <button
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      >
        Search Versions
      </button>
    </form>
    <div class="table-scroll w3-responsive">
      <table class="mobile-history-table w3-table w3-striped w3-bordered">
        <thead>
          <tr>
            <th>Version</th>
            <th>Lifecycle</th>
            <th>Country</th>
            <th>Recorded At</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="row in history?.items"
            :key="row.versionPublicId"
          >
            <td data-label="Version">
              <button
                type="button"
                class="version-link"
                :aria-label="'View Configuration Version ' + row.version"
                @click="choose(row, 'View')"
              >
                {{ row.version }}
              </button>
            </td>
            <td data-label="Lifecycle">{{ row.state }}</td>
            <td data-label="Country">{{ row.settings.operatingCountry }}</td>
            <td data-label="Recorded At">
              {{ row.createdAtUtc ? formatDate(row.createdAtUtc) : 'Unknown' }}
            </td>
            <td data-label="Actions">
              <div class="history-actions">
                <button
                  class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                  @click="choose(row, 'View')"
                >
                  Read Details
                </button>
                <button
                  class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                  @click="choose(row, row.isDeleted ? 'Restore' : 'Copy')"
                >
                  {{ row.isDeleted ? 'Restore' : 'Copy' }}
                  As New Version
                </button>
                <button
                  v-if="!row.isDeleted"
                  class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                  @click="choose(row, 'Remove')"
                >
                  Remove Version
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="history-pages w3-section">
      <button
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        :disabled="page <= 1"
        @click="changePage(-1)"
      >
        Previous
      </button>
      <span>{{ page }} · {{ history?.total ?? 0 }} Versions</span>
      <button
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        :disabled="page * 10 >= (history?.total ?? 0)"
        @click="changePage(1)"
      >
        Next
      </button>
    </div>
    <PortalDialog
      v-if="selected"
      :title="'Configuration · Version ' + selected.version"
      :busy="busy"
      @close="selected = null"
    >
      <section class="configuration-version-detail">
        <h3 class="w3-large w3-sans-serif">
          Version {{ selected.version }}
          · Read Only
        </h3>
        <p>{{ selected.changeReason }}</p>
        <p>
          Published By:
          {{ selected.createdBy ?? 'Unknown' }} ·
          {{
            selected.createdAtUtc ? formatDate(selected.createdAtUtc) : 'Unknown Publication Time'
          }}
        </p>
        <SettingsComparison :after="selected.settings" />
        <div class="w3-responsive">
          <table class="w3-table w3-bordered">
            <tbody>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Country
                </th>
                <td class="w3-mobile">{{ selected.settings.operatingCountry }}</td>
              </tr>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Account Types
                </th>
                <td class="w3-mobile">{{ selected.settings.accountTypes.join(', ') }}</td>
              </tr>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Maximum Accounts
                </th>
                <td class="w3-mobile">{{ selected.settings.maximumAccounts }}</td>
              </tr>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Identity Verification
                </th>
                <td class="w3-mobile">
                  {{ selected.settings.registration.requireVerification ? 'Required' : 'Optional' }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p v-if="selected.isDeleted">
          Removed
          {{ selected.deletedAtUtc ? formatDate(selected.deletedAtUtc) : 'Unknown' }}.
          {{ selected.deletionReason }} · Removed By:
          {{ selected.deletedBy ?? 'Unknown' }}
        </p>
        <form
          v-if="mode !== 'View'"
          class="ops-form w3-bar"
          @submit.prevent="review"
        >
          <h3 class="w3-large w3-sans-serif">{{ mode }} Version</h3>
          <button
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="busy"
            @click="refreshToken"
          >
            Refresh History
          </button>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Reason
            <textarea
              v-model="reason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              rows="3"
              minlength="10"
              maxlength="500"
              required
              @input="clearPreview"
            />
          </label>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="busy"
          >
            Preview Change
          </button>
          <div v-if="preview">
            <p>
              Before:
              {{
                history?.effective.selected
                  ? `Version ${history.effective.selected.version}`
                  : 'No Applicable Configuration'
              }}. After: Version {{ preview.version }} · {{ preview.operatingCountry }}. The
              retained source is copied; all financial safeguards are checked again.
            </p>
            <button
              type="button"
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
              :disabled="busy"
              @click="confirm"
            >
              Publish New Version
            </button>
          </div>
          <div v-if="removal">
            <p>
              {{
                removal.selected
                  ? `Version ${removal.selected.version} remains effective.`
                  : 'No configuration will apply. Financial and account requests will be unavailable until a new version is published.'
              }}
            </p>
            <button
              type="button"
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
              :disabled="busy"
              @click="confirm"
            >
              Confirm Soft Deletion
            </button>
          </div>
        </form>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="busy"
          @click="selected = null"
        >
          Close Details
        </button>
      </section>
    </PortalDialog>
  </section>
</template>
