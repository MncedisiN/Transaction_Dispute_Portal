<script setup lang="ts">
import PortalDialog from './PortalDialog.vue'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { api, ApiError } from '../api'
import { useFeedback } from '../composables/useFeedback'
import { formatDate, formatMoney } from '../utils/format'
import { operatingCountry } from '../platform'
import {
  policyBoundaryDelay,
  type PolicyHistory,
  type PolicyPreview,
  type PolicyVersion,
  type PolicyResolution
} from '../policyVersions'
type NumberKey =
  | 'maximumAmount'
  | 'duplicateWindowMinutes'
  | 'maximumAgeHours'
  | 'dailyLimit'
  | 'highValueThreshold'
  | 'extraCheckAfterDays'
  | 'debitOrderWindowDays'
  | 'debitOrderAppLimit'
interface Field {
  key: NumberKey
  label: string
  money?: boolean
}
interface Draft extends Partial<PolicyVersion> {
  operation: string
  reason: string
  priority: number
  effectiveFromUtc: string
  effectiveToUtc: string
  requestId: string
  concurrencyToken: number
  expectedVersion: number
  sourceVersionPublicId: string | null
}
const props = defineProps<{ kind: 'automatic' | 'routing' }>()
const history = ref<PolicyHistory | null>(null),
  search = ref(''),
  page = ref(1),
  busy = ref(false),
  error = ref(''),
  notice = ref('')
const draft = ref<Draft | null>(null),
  preview = ref<PolicyPreview | null>(null),
  detail = ref<PolicyVersion | null>(null)
const removal = ref<PolicyVersion | null>(null),
  removalReason = ref(''),
  removalPreview = ref<PolicyResolution | null>(null)
let boundary: ReturnType<typeof setTimeout> | undefined
let refresh: ReturnType<typeof setInterval> | undefined
let request: AbortController | undefined
let stopped = false
const title = computed(() => (props.kind === 'automatic' ? 'Automatic Refunds' : 'Dispute Routing'))
const base = computed(() => `/api/v1/admin/policies/${props.kind}`)
const fields = computed<Field[]>(() =>
  props.kind === 'automatic'
    ? [
        { key: 'maximumAmount', label: 'Maximum Refund', money: true },
        { key: 'maximumAgeHours', label: 'Maximum Age In Hours' },
        { key: 'duplicateWindowMinutes', label: 'Duplicate Match Window In Minutes' },
        { key: 'dailyLimit', label: 'Daily Customer Limit', money: true }
      ]
    : [
        { key: 'highValueThreshold', label: 'Identity Verification Threshold', money: true },
        { key: 'extraCheckAfterDays', label: 'Extra Verification After Days' },
        { key: 'debitOrderWindowDays', label: 'Debit Order Window In Days' },
        { key: 'debitOrderAppLimit', label: 'Specialist Review Threshold', money: true }
      ]
)
const current = computed(() => history.value?.effective.selected)
function date(value: string | null | undefined) {
  return value ? formatDate(value) : 'Unknown'
}
function value(row: PolicyVersion | null | undefined, key: string, money = false) {
  const result = row?.[key as keyof PolicyVersion]
  return result == null
    ? 'Unavailable'
    : money
      ? formatMoney(Number(result), operatingCountry()?.defaultCurrency ?? '')
      : String(result)
}
async function load() {
  request?.abort()
  const controller = new AbortController()
  request = controller
  try {
    const result = await api.get<PolicyHistory>(
      `${base.value}/history?page=${page.value}&pageSize=10&search=${encodeURIComponent(search.value)}`,
      controller.signal
    )
    if (controller.signal.aborted || stopped) return
    history.value = result
    if (boundary) clearTimeout(boundary)
    const delay = policyBoundaryDelay(result.effective)
    if (delay !== null) boundary = setTimeout(() => void load(), delay)
  } catch (e) {
    if (!controller.signal.aborted)
      error.value = e instanceof ApiError ? e.message : 'Policy history could not be loaded.'
  }
}
function begin(source?: PolicyVersion, operation = 'Create') {
  error.value = ''
  const row = source ?? current.value ?? history.value?.items[0]
  if (!row || !history.value) return
  draft.value = {
    ...row,
    operation,
    sourceVersionPublicId: operation === 'Create' ? null : row.versionPublicId,
    requestId: crypto.randomUUID(),
    concurrencyToken: history.value.concurrencyToken,
    expectedVersion: current.value?.version ?? row.version,
    reason: '',
    priority: row.priority,
    effectiveFromUtc: '',
    effectiveToUtc: ''
  }
  preview.value = null
  detail.value = null
  removal.value = null
}
function body() {
  const result: Record<string, unknown> = { ...draft.value }
  result.effectiveFromUtc = result.effectiveFromUtc
    ? new Date(String(result.effectiveFromUtc)).toISOString()
    : null
  result.effectiveToUtc = result.effectiveToUtc
    ? new Date(String(result.effectiveToUtc)).toISOString()
    : null
  // IDs, actors, timestamps and lifecycle flags in a displayed snapshot are never submitted as authority.
  return Object.fromEntries(
    Object.entries(result).filter(([key]) =>
      [
        'requestId',
        'concurrencyToken',
        'expectedVersion',
        'operation',
        'sourceVersionPublicId',
        'reason',
        'priority',
        'effectiveFromUtc',
        'effectiveToUtc',
        'enabled',
        ...fields.value.map((f) => f.key)
      ].includes(key)
    )
  )
}
async function check() {
  if (!draft.value || busy.value) return
  busy.value = true
  error.value = ''
  try {
    preview.value = await api.post<PolicyPreview>(`${base.value}/preview`, body())
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Preview could not be prepared.'
    await load()
  } finally {
    busy.value = false
  }
}
async function publish() {
  if (!preview.value || !draft.value || busy.value) return
  busy.value = true
  error.value = ''
  try {
    await api.post(`${base.value}/versions`, body())
    draft.value = null
    preview.value = null
    page.value = 1
    await load()
    notice.value = 'A new immutable policy version was published.'
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Publication could not be completed.'
    preview.value = null
    await load()
  } finally {
    busy.value = false
  }
}
function acceptRefresh() {
  if (draft.value && history.value) {
    draft.value.concurrencyToken = history.value.concurrencyToken
    draft.value.requestId = crypto.randomUUID()
    preview.value = null
  }
}
async function checkRemoval() {
  if (!removal.value || !history.value) return
  busy.value = true
  try {
    removalPreview.value = await api.post<PolicyResolution>(
      `${base.value}/${removal.value.versionPublicId}/removal-preview`,
      { concurrencyToken: history.value.concurrencyToken, reason: removalReason.value }
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Removal preview unavailable.'
  } finally {
    busy.value = false
  }
}
async function remove() {
  if (!removal.value || !history.value || !removalPreview.value) return
  busy.value = true
  try {
    await api.post(`${base.value}/${removal.value.versionPublicId}/remove`, {
      concurrencyToken: history.value.concurrencyToken,
      reason: removalReason.value
    })
    removal.value = null
    removalPreview.value = null
    await load()
    notice.value = 'The version was retained with its removal attribution.'
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'The version was not removed.'
    removalPreview.value = null
    await load()
  } finally {
    busy.value = false
  }
}
watch(
  draft,
  () => {
    preview.value = null
  },
  { deep: true }
)
watch(
  () => history.value?.concurrencyToken,
  (next, previous) => {
    if (previous !== undefined && next !== previous) {
      preview.value = null
      removalPreview.value = null
    }
  }
)
watch(removalReason, () => {
  removalPreview.value = null
})
onMounted(() => {
  void load()
  refresh = setInterval(() => {
    if (!document.hidden && !busy.value) void load()
  }, 15000)
})
onUnmounted(() => {
  stopped = true
  request?.abort()
  if (boundary) clearTimeout(boundary)
  if (refresh) clearInterval(refresh)
})
useFeedback(error, notice, undefined)
function cancelDraft() {
  draft.value = null
  preview.value = null
}
function beginRemoval(row: PolicyVersion) {
  error.value = ''
  removal.value = row
  removalReason.value = ''
  removalPreview.value = null
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
    class="surface-panel ops-panel policy-history w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <div class="ops-heading w3-bar w3-padding-16">
      <h2 class="w3-sans-serif w3-large">{{ title }}</h2>
      <span class="status-pill w3-tag w3-light-grey w3-round-large w3-small w3-padding-small">
        {{ current ? `Version ${current.version}` : 'No Applicable Policy' }}
      </span>
    </div>
    <p class="field-hint w3-small w3-text-grey w3-block">
      Published settings are retained. A replacement never revives an older version when it expires
      or is removed.
    </p>
    <div
      v-if="current"
      class="effective-summary w3-section"
    >
      <p>
        <strong>{{ current.enabled ? 'Enabled' : 'Disabled' }}</strong>
        · Priority {{ current.priority }}
      </p>
      <div class="w3-responsive">
        <table class="w3-table w3-bordered">
          <tbody>
            <tr
              v-for="field in fields"
              :key="field.key"
            >
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                {{ field.label }}
              </th>
              <td class="w3-mobile">{{ value(current, field.key, field.money) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="field-hint w3-small w3-text-grey w3-block">
        {{ date(current.effectiveFromUtc) }} —
        {{ current.effectiveToUtc ? date(current.effectiveToUtc) : 'No Expiry' }}
      </p>
    </div>
    <PortalDialog
      v-if="draft"
      :title="
        (draft.operation === 'Create'
          ? 'Create New Version'
          : draft.operation + ' As New Version') +
        ' · ' +
        title
      "
      :busy="busy"
      @close="cancelDraft"
    >
      <form
        class="ops-form version-form"
        @submit.prevent="check"
      >
        <div class="policy-editor-fields">
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Feature Availability
            <select
              v-model="draft.enabled"
              class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
              :disabled="draft.operation !== 'Create'"
            >
              <option :value="true">Enabled</option>
              <option :value="false">Disabled</option>
            </select>
          </label>
          <label
            v-for="field in fields"
            :key="field.key"
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          >
            {{ field.label }}
            <input
              v-model.number="draft[field.key]"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              :disabled="draft.operation !== 'Create'"
              type="number"
              :step="field.money ? '0.01' : '1'"
              required
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Priority
            <input
              v-model.number="draft.priority"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              type="number"
              min="-1000"
              max="1000"
              step="1"
              required
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Start At
            <small class="w3-small">Leave Empty To Publish Immediately</small>
            <input
              v-model="draft.effectiveFromUtc"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              type="datetime-local"
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            End At
            <small class="w3-small">Optional</small>
            <input
              v-model="draft.effectiveToUtc"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              type="datetime-local"
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Reason
            <textarea
              v-model="draft.reason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              minlength="10"
              maxlength="500"
              rows="3"
              required
            />
          </label>
        </div>
        <div class="dialog-actions">
          <button
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            @click="acceptRefresh"
          >
            Use Latest Version
          </button>
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="busy"
          >
            Preview New Version
          </button>
          <button
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="busy"
            @click="cancelDraft"
          >
            Cancel
          </button>
        </div>
        <div
          v-if="preview"
          class="comparison w3-section"
        >
          <h3 class="w3-large w3-sans-serif">Before And After</h3>
          <div class="table-scroll w3-responsive">
            <table class="w3-table w3-striped w3-bordered">
              <thead>
                <tr>
                  <th>Setting</th>
                  <th>Currently Effective</th>
                  <th>
                    New Version
                    {{ preview.proposed.version }}
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <th>Feature Availability</th>
                  <td>
                    {{
                      preview.before.selected
                        ? preview.before.selected.enabled
                          ? 'Enabled'
                          : 'Disabled'
                        : 'Unavailable'
                    }}
                  </td>
                  <td>{{ preview.proposed.enabled ? 'Enabled' : 'Disabled' }}</td>
                </tr>
                <tr>
                  <th>Priority</th>
                  <td>{{ preview.before.selected?.priority ?? 'Unavailable' }}</td>
                  <td>{{ preview.proposed.priority }}</td>
                </tr>
                <tr>
                  <th>Start</th>
                  <td>{{ date(preview.before.selected?.effectiveFromUtc) }}</td>
                  <td>{{ date(preview.proposed.effectiveFromUtc) }}</td>
                </tr>
                <tr>
                  <th>Expiry</th>
                  <td>
                    {{
                      preview.before.selected?.effectiveToUtc
                        ? date(preview.before.selected.effectiveToUtc)
                        : 'No Expiry'
                    }}
                  </td>
                  <td>
                    {{
                      preview.proposed.effectiveToUtc
                        ? date(preview.proposed.effectiveToUtc)
                        : 'No Expiry'
                    }}
                  </td>
                </tr>
                <tr
                  v-for="field in fields"
                  :key="field.key"
                >
                  <th>{{ field.label }}</th>
                  <td>{{ value(preview.before.selected, field.key, field.money) }}</td>
                  <td>{{ value(preview.proposed, field.key, field.money) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p>
            {{
              preview.after.selected
                ? `Version ${preview.after.selected.version} applies immediately.`
                : 'No policy will apply immediately.'
            }}
            {{
              preview.proposed.state === 'Scheduled'
                ? 'The new version starts at its scheduled boundary.'
                : ''
            }}
          </p>
          <p v-if="preview.atActivation">
            At The New Start:
            {{
              preview.atActivation.selected
                ? `Version ${preview.atActivation.selected.version} Applies`
                : 'No Applicable Policy'
            }}.
          </p>
          <p class="field-hint w3-small w3-text-grey w3-block">
            {{ preview.consequence }}
          </p>
          <button
            type="button"
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="busy"
            @click="publish"
          >
            Publish New Version
          </button>
        </div>
      </form>
    </PortalDialog>
    <form
      class="history-search policy-version-toolbar"
      @submit.prevent="searchHistory"
    >
      <button
        type="button"
        class="policy-create-button button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        :disabled="busy || !history"
        @click="begin()"
      >
        Create New Version
      </button>

      <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
        Search Version History
        <input
          v-model="search"
          class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          type="search"
          placeholder="Version Or Change Reason"
        />
      </label>
      <button
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      >
        Search History
      </button>
    </form>
    <div
      v-if="history"
      class="table-scroll w3-responsive"
    >
      <table class="mobile-history-table w3-table w3-striped w3-bordered">
        <thead>
          <tr>
            <th>Version</th>
            <th>Lifecycle</th>
            <th>Feature</th>
            <th>Published By</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="row in history.items"
            :key="row.versionPublicId"
          >
            <td data-label="Version">
              <button
                type="button"
                class="version-link"
                :aria-label="'View Version ' + row.version + ' · ' + title"
                @click="detail = row"
              >
                {{ row.version }}
              </button>
            </td>
            <td data-label="Lifecycle">{{ row.state }}</td>
            <td data-label="Feature">{{ row.enabled ? 'Enabled' : 'Disabled' }}</td>
            <td data-label="Published By">
              {{ row.createdByName ?? (row.createdBy ? 'Recorded Administrator' : 'Unknown') }}
              <small class="w3-small">{{ date(row.createdAtUtc) }}</small>
            </td>
            <td data-label="Actions">
              <div class="history-actions w3-section">
                <button
                  class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                  @click="detail = row"
                >
                  View Details
                </button>
                <button
                  class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                  @click="begin(row, row.isDeleted ? 'Restore' : 'Copy')"
                >
                  {{ row.isDeleted ? 'Restore' : 'Copy' }}
                  As New Version
                </button>
                <button
                  v-if="!row.isDeleted"
                  class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                  @click="beginRemoval(row)"
                >
                  Remove Version
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div
      v-if="history"
      class="history-pages w3-section"
    >
      <button
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        :disabled="page <= 1"
        @click="changePage(-1)"
      >
        Previous
      </button>
      <span>{{ page }} · {{ history.total }} Versions</span>
      <button
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        :disabled="page * 10 >= history.total"
        @click="changePage(1)"
      >
        Next
      </button>
    </div>
    <PortalDialog
      v-if="detail"
      :title="title + ' · Version ' + detail.version"
      @close="detail = null"
    >
      <section class="historical-details w3-section">
        <h3 class="w3-large w3-sans-serif">
          Version {{ detail.version }}
          · Read Only
        </h3>
        <p>{{ detail.changeReason }}</p>
        <p>
          {{ detail.provenance }} · {{ date(detail.createdAtUtc) }} ·
          {{ detail.createdByName ?? 'Unknown' }}
        </p>
        <p v-if="detail.isDeleted">
          Removed {{ date(detail.deletedAtUtc) }}
          By
          {{ detail.deletedByName ?? 'Recorded Administrator' }}. {{ detail.deletionReason }}
        </p>
        <div class="w3-responsive">
          <table class="w3-table w3-bordered">
            <tbody>
              <tr
                v-for="field in fields"
                :key="field.key"
              >
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  {{ field.label }}
                </th>
                <td class="w3-mobile">{{ value(detail, field.key, field.money) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          @click="detail = null"
        >
          Close Details
        </button>
      </section>
    </PortalDialog>
    <PortalDialog
      v-if="removal"
      :title="'Remove Version ' + removal.version + ' · ' + title"
      :busy="busy"
      @close="removal = null"
    >
      <form
        class="ops-form version-form w3-bar"
        @submit.prevent="checkRemoval"
      >
        <h3 class="w3-large w3-sans-serif">Remove Version {{ removal.version }}</h3>
        <p>The content and history remain retained. Older versions will not resume.</p>
        <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
          Removal Reason
          <textarea
            v-model="removalReason"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            minlength="10"
            maxlength="500"
            rows="3"
            required
          />
        </label>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="busy"
        >
          Preview Removal
        </button>
        <div v-if="removalPreview">
          <p>
            {{
              removalPreview.selected
                ? `Version ${removalPreview.selected.version} will remain effective.`
                : 'No policy will apply. The feature will be unavailable.'
            }}
          </p>
          <p v-if="removalPreview.atAffectedBoundary">
            At
            {{ date(removalPreview.atAffectedBoundary.evaluationTimeUtc) }}:
            {{
              removalPreview.atAffectedBoundary.selected
                ? `Version ${removalPreview.atAffectedBoundary.selected.version} Applies`
                : 'No Policy Will Apply'
            }}. Removing A Scheduled Replacement Does Not Reinstate Older Versions.
          </p>
          <button
            type="button"
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="busy"
            @click="remove"
          >
            Confirm Soft Deletion
          </button>
        </div>
        <button
          type="button"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="busy"
          @click="removal = null"
        >
          Cancel
        </button>
      </form>
    </PortalDialog>
  </section>
</template>
