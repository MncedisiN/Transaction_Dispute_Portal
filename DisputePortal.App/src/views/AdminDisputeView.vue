<script setup lang="ts">
import DisclosurePanel from '../components/DisclosurePanel.vue'
import { useFeedback } from '../composables/useFeedback'
import { useDisputeInputPolicy } from '../composables/useDisputeInputPolicy'

import ActionActor from '../components/ActionActor.vue'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import CustomerAssistanceDetails from '../components/CustomerAssistanceDetails.vue'
import type { StaffTransactionDetail } from '../staffTransactions'
import CaseFinancials from '../components/CaseFinancials.vue'
import CaseActivity from '../components/CaseActivity.vue'
import CopyReference from '../components/CopyReference.vue'
import ChoiceSheet from '../components/ChoiceSheet.vue'
import BackLink from '../components/BackLink.vue'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Check, Plus, LockKeyhole, MessageCircle, ShieldCheck } from '@lucide/vue'
import { api, ApiError } from '../api'
import type { AdminDispute } from '../types'
import { formatDate, formatMoney } from '../utils/format'
import { useAuthStore } from '../stores/auth'
import { useOnline } from '../composables/useOnline'
import EvidencePanel from '../components/EvidencePanel.vue'
import CaseOperationsPanel from '../components/CaseOperationsPanel.vue'
import AppShell from '../components/AppShell.vue'
import StatusBadge from '../components/StatusBadge.vue'

const { policy: inputPolicy } = useDisputeInputPolicy()
const route = useRoute(),
  router = useRouter()
const section = ref<'overview' | 'workflow' | 'activity' | 'evidence'>(
  route.query.section === 'workflow' ||
    route.query.section === 'activity' ||
    route.query.section === 'evidence'
    ? route.query.section
    : 'overview'
)
watch(section, (value) => {
  void router.replace({
    query: { ...route.query, section: value === 'overview' ? undefined : value }
  })
})
const auth = useAuthStore()
const { online } = useOnline()
const detail = ref<AdminDispute | null>(null)
const customerDetail = ref<StaffTransactionDetail | null>(null)
const loading = ref(true)
const error = ref('')
const flash = ref('')
const nextStatus = ref('')
const comment = ref('')
const note = ref('')
const visibility = ref<'Internal' | 'Customer'>('Internal')
const saving = ref(false)
const canManage = computed(() => detail.value?.operations?.actions?.canManage === true)
const activeCase = computed(() => detail.value?.operations?.actions?.isClosed === false)
const canAssign = computed(() => detail.value?.operations?.actions?.canAssign === true)
const assignmentRequest = ref(0),
  assignmentIntent = ref<'assign' | 'unassign'>('assign')
function showAssignment(intent: 'assign' | 'unassign') {
  assignmentIntent.value = intent
  assignmentRequest.value++
  section.value = 'workflow'
}
const choices = computed(() => detail.value?.operations?.actions?.allowedTransitions ?? [])

let loadRequest: AbortController | undefined
async function load() {
  loadRequest?.abort()
  const request = new AbortController()
  loadRequest = request
  loading.value = true
  error.value = ''
  customerDetail.value = null
  detail.value = null
  const id = String(route.params.id)
  try {
    const result = await api.get<AdminDispute>('/api/v1/admin/disputes/' + id, request.signal)
    if (request.signal.aborted) return
    detail.value = result
    if (request.signal.aborted) return
    nextStatus.value = choices.value[0] ?? ''
    if (auth.user?.permissions.includes('transactions.assist')) {
      try {
        const customer = await api.get<StaffTransactionDetail>(
          '/api/v1/admin/transactions/' + result.dispute.transactionId,
          request.signal
        )
        if (!request.signal.aborted) customerDetail.value = customer
      } catch {
        /* Preserve the case if assisted details are temporarily unavailable. */
      }
    }
  } catch {
    if (!request.signal.aborted) error.value = 'This case could not be loaded.'
  } finally {
    if (!request.signal.aborted) loading.value = false
  }
}
onBeforeUnmount(() => loadRequest?.abort())
async function refreshCaseDetails() {
  const id = detail.value?.dispute.id
  if (!id) return
  try {
    const updated = await api.get<AdminDispute>('/api/v1/admin/disputes/' + id)
    if (
      detail.value?.dispute.id === id &&
      updated.dispute.version >= detail.value.dispute.version
    ) {
      detail.value = updated
      nextStatus.value = choices.value.includes(nextStatus.value)
        ? nextStatus.value
        : (choices.value[0] ?? '')
    }
  } catch {
    /* Keep the existing case visible; activity refresh can be retried. */
  }
}
watch(() => route.params.id, load)

async function updateStatus() {
  if (
    !detail.value ||
    !canManage.value ||
    !choices.value.includes(nextStatus.value) ||
    (!activeCase.value && comment.value.trim().length < 10)
  )
    return
  saving.value = true
  error.value = ''
  try {
    await api.post('/api/v1/admin/disputes/' + detail.value.dispute.id + '/transitions', {
      expectedVersion: detail.value.dispute.version,
      status: nextStatus.value,
      comment: comment.value.trim() || null
    })
    flash.value = 'Case updated.'
    comment.value = ''
    await load()
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : 'The status could not be updated.'
  } finally {
    saving.value = false
  }
}
async function addNote() {
  if (!detail.value || !canManage.value || !activeCase.value || !note.value.trim()) return
  saving.value = true
  error.value = ''
  try {
    await api.post('/api/v1/admin/disputes/' + detail.value.dispute.id + '/notes', {
      content: note.value.trim(),
      visibility: visibility.value
    })
    flash.value = visibility.value === 'Internal' ? 'Internal note saved.' : 'Customer update sent.'
    note.value = ''
    await load()
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : 'The note could not be added.'
  } finally {
    saving.value = false
  }
}

useLiveRefresh(
  async (signal) => {
    if (!detail.value) {
      await load()
      return
    }
    const id = detail.value.dispute.id
    if (loading.value) return
    const updated = await api.get<AdminDispute>('/api/v1/admin/disputes/' + id, signal)
    if (!signal.aborted && detail.value?.dispute.id === id) {
      detail.value = updated
    }
    if (!choices.value.includes(nextStatus.value)) nextStatus.value = choices.value[0] ?? ''
  },
  ['disputes']
)
useFeedback(error, flash, undefined)
</script>

<template>
  <AppShell>
    <BackLink
      fallback="/admin/disputes"
      label="Back To Case Management"
    />
    <div
      v-if="loading"
      class="page-loading"
    >
      <span />
      <span />
    </div>
    <section
      v-else-if="detail"
      class="admin-detail-page"
    >
      <header class="case-page-heading admin-case-heading w3-bar w3-section w3-padding-16">
        <div>
          <div class="admin-case-reference">
            <h1 class="w3-xxlarge w3-sans-serif w3-text-black">
              {{ detail.dispute.reference }}
            </h1>
            <CopyReference :reference="detail.dispute.reference" />
          </div>
          <p class="case-caption w3-small w3-text-grey w3-bar">
            {{ detail.dispute.transactionDescription }}
          </p>
        </div>
        <StatusBadge
          :status="detail.dispute.status"
          staff
        />
      </header>
      <div
        v-if="canAssign"
        class="case-assignment-toolbar w3-bar w3-white w3-padding-large w3-card w3-round-xlarge w3-section"
      >
        <span>{{ detail.operations?.assignedName || 'Unassigned' }}</span>
        <button
          type="button"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          @click="showAssignment('assign')"
        >
          <Plus
            :size="16"
            aria-hidden="true"
          />
          Assign
        </button>
        <button
          v-if="detail.operations?.assignedTo"
          type="button"
          class="text-button w3-button w3-text-black w3-round-large"
          @click="showAssignment('unassign')"
        >
          Unassign
        </button>
      </div>
      <p
        v-if="flash"
        class="success-alert w3-panel w3-pale-green w3-round-large w3-padding"
        role="status"
      >
        <Check
          class="w3-margin-right"
          :size="16"
        />
        {{ flash }}
      </p>
      <p
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
      </p>
      <div class="case-workspace-toolbar w3-bar w3-section">
        <nav
          class="section-tabs w3-bar w3-light-grey w3-round-large w3-margin-bottom w3-row w3-border w3-white w3-padding-small"
          aria-label="Case Workspace"
        >
          <button
            :class="{ 'w3-theme-l4 w3-text-black': (section === 'overview') === true }"
            class="w3-button w3-round-large w3-center w3-col s3 w3-padding-16"
            :aria-pressed="section === 'overview'"
            @click="section = 'overview'"
          >
            Overview
          </button>
          <button
            :class="{ 'w3-theme-l4 w3-text-black': (section === 'workflow') === true }"
            class="w3-button w3-round-large w3-center w3-col s3 w3-padding-16"
            :aria-pressed="section === 'workflow'"
            @click="section = 'workflow'"
          >
            Workflow
          </button>
          <button
            :class="{ 'w3-theme-l4 w3-text-black': (section === 'activity') === true }"
            class="w3-button w3-round-large w3-center w3-col s3 w3-padding-16"
            :aria-pressed="section === 'activity'"
            @click="section = 'activity'"
          >
            Activity
          </button>
          <button
            :class="{ 'w3-theme-l4 w3-text-black': (section === 'evidence') === true }"
            class="w3-button w3-round-large w3-center w3-col s3 w3-padding-16"
            :aria-pressed="section === 'evidence'"
            @click="section = 'evidence'"
          >
            Evidence
          </button>
        </nav>
        <RouterLink
          :to="{
            path: '/support',
            query: { dispute: detail.dispute.id, returnTo: route.fullPath }
          }"
          class="text-link w3-text-blue-grey"
        >
          <MessageCircle
            class="w3-margin-right"
            :size="16"
          />
          Case Messages
        </RouterLink>
      </div>
      <button
        v-if="
          section === 'overview' &&
          canManage &&
          ['Submitted', 'AwaitingCustomer'].includes(detail.dispute.status)
        "
        class="button button-primary overview-review-action w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        :disabled="saving || !online"
        @click="
          () => {
            nextStatus = 'UnderReview'
            updateStatus()
          }
        "
      >
        {{ saving ? 'Starting Review…' : 'Start Review' }}
      </button>
      <CaseFinancials
        v-if="section === 'overview'"
        :case-id="detail.dispute.id"
        :version="detail.dispute.version"
        :status="detail.dispute.status"
        :resolution="detail.dispute.resolution"
        :can-refund="Boolean(canManage)"
        :account-number="customerDetail?.fullAccountNumber"
        :account-type="customerDetail?.transaction.accountType"
        @changed="refreshCaseDetails"
      />
      <CaseOperationsPanel
        v-if="section === 'workflow'"
        :case-id="detail.dispute.id"
        :resolved-by="detail.resolvedBy"
        :status="detail.dispute.status"
        :assignment-request="assignmentRequest"
        :assignment-intent="assignmentIntent"
        @changed="refreshCaseDetails"
      />

      <EvidencePanel
        v-if="section === 'evidence'"
        :case-id="detail.dispute.id"
        :can-manage-access="auth.isStaffWorkspace"
        :can-upload="detail.operations?.actions?.canAddNote === true"
        :status="detail.dispute.status"
        @changed="refreshCaseDetails"
      />
      <div
        v-if="section !== 'evidence'"
        class="admin-detail-grid w3-row-padding"
        :class="{
          'workflow-only': section === 'workflow',
          'single-section': section !== 'workflow'
        }"
      >
        <div class="w3-col s12 m6 w3-margin-bottom">
          <div class="admin-detail-main">
            <CustomerAssistanceDetails
              v-if="section === 'overview' && customerDetail"
              :detail="customerDetail"
            />
            <section
              v-if="section === 'overview' && !customerDetail"
              class="surface-panel staff-card w3-container w3-margin-bottom w3-padding-large w3-section w3-white w3-round-xlarge w3-border w3-border-light-grey"
            >
              <div class="panel-heading w3-bar w3-padding-16">
                <div>
                  <p class="eyebrow w3-small w3-wide w3-text-grey">CUSTOMER &amp; TRANSACTION</p>
                  <h2 class="w3-sans-serif w3-large">Case Overview</h2>
                </div>
                <span
                  class="priority-pill"
                  :class="{ 'priority-pill-high': detail.dispute.priority >= 4 }"
                >
                  Priority {{ detail.dispute.priority }}
                </span>
              </div>
              <div class="staff-facts w3-section">
                <DisclosurePanel class="case-customer-disclosure">
                  <template #heading>Customer &amp; Account</template>
                  <div>
                    <small class="w3-small">CUSTOMER</small>
                    <b>{{ detail.customerName }}</b>
                    <span>{{ detail.maskedEmail }}</span>
                  </div>
                </DisclosurePanel>
                <div>
                  <small class="w3-small">TRANSACTION</small>
                  <b>{{ detail.merchantName }}</b>
                  <span>
                    {{ detail.transactionReference }} · {{ formatDate(detail.transactionAt) }}
                  </span>
                </div>
                <div>
                  <small class="w3-small">AMOUNT</small>
                  <b>{{ formatMoney(detail.dispute.amount, detail.dispute.currency) }}</b>
                  <span>{{ detail.dispute.category }}</span>
                </div>
              </div>
            </section>
            <section
              v-if="section === 'activity'"
              class="surface-panel staff-card w3-container w3-margin-bottom w3-padding-large w3-section w3-white w3-round-xlarge w3-border w3-border-light-grey"
            >
              <div class="panel-heading w3-bar w3-padding-16">
                <div>
                  <p class="eyebrow w3-small w3-wide w3-text-grey">CASE HISTORY</p>
                  <h2 class="w3-sans-serif w3-large">Status And Notes</h2>
                </div>
              </div>
              <CaseActivity
                :case-id="detail.dispute.id"
                :version="detail.dispute.version"
              />
              <div class="staff-notes w3-section">
                <article
                  v-for="(item, index) in detail.notes"
                  :key="item.createdAt + index"
                  class="staff-note w3-panel w3-light-grey w3-padding w3-round-large"
                  :class="item.visibility === 'Internal' ? 'internal-note' : 'customer-note'"
                >
                  <div>
                    <span>
                      {{ item.visibility === 'Internal' ? 'INTERNAL NOTE' : 'CUSTOMER UPDATE' }}
                    </span>
                    <ActionActor
                      :identity="item.actorIdentity"
                      :fallback="item.authorName"
                    />
                    <small class="w3-small">{{ formatDate(item.createdAt) }}</small>
                  </div>
                  <p>{{ item.content }}</p>
                </article>
              </div>
            </section>
          </div>
        </div>
        <div
          v-if="section === 'workflow'"
          class="w3-col s12 m6 w3-margin-bottom"
        >
          <aside class="admin-actions">
            <section
              v-if="canManage && choices.length"
              class="surface-panel staff-card w3-container w3-margin-bottom w3-padding-large w3-section w3-white w3-round-xlarge w3-border w3-border-light-grey"
            >
              <p class="eyebrow w3-small w3-wide w3-text-grey">
                {{ activeCase ? 'MOVE CASE FORWARD' : 'REVIEW AGAIN' }}
              </p>
              <h2 class="w3-sans-serif w3-large">
                {{ activeCase ? 'Update Status' : 'Reopen Case' }}
              </h2>
              <p class="staff-card-copy w3-text-grey w3-small">
                {{
                  activeCase
                    ? 'Choose the next step for this case.'
                    : 'Reopen this case for another review. Enter a reason of at least 10 characters.'
                }}
              </p>
              <p
                v-if="!online"
                class="offline-form-note w3-panel w3-pale-yellow"
                role="status"
              >
                Reconnect before saving a case update.
              </p>
              <form
                class="staff-form w3-section"
                @submit.prevent="updateStatus"
              >
                <span
                  v-if="activeCase"
                  class="field-label w3-block w3-margin-top"
                >
                  Next Status
                </span>
                <ChoiceSheet
                  v-if="activeCase"
                  v-model="nextStatus"
                  label="Next Status"
                  :options="
                    choices.map((value) => ({
                      value,
                      label:
                        value === 'Resolved'
                          ? 'Close Without Refund'
                          : value.replace(/([A-Z])/g, ' $1').trim()
                    }))
                  "
                  :disabled="choices.length === 0"
                />
                <label
                  class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
                  for="status-comment"
                >
                  {{ activeCase ? 'Customer Update Or Outcome' : 'Reason For Reopening' }}
                </label>
                <textarea
                  id="status-comment"
                  v-model="comment"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  rows="3"
                  :maxlength="inputPolicy?.commentMaximumLength"
                  :minlength="activeCase ? undefined : 10"
                  :required="!activeCase"
                  placeholder="Keep this clear and appropriate for the customer."
                />
                <button
                  class="button button-primary full-width w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
                  type="submit"
                  :disabled="
                    saving ||
                    !inputPolicy ||
                    !nextStatus ||
                    !online ||
                    (!activeCase && comment.trim().length < 10)
                  "
                >
                  {{ saving ? 'Saving…' : activeCase ? 'Save Status Update' : 'Reopen Case' }}
                  <Check :size="16" />
                </button>
              </form>
            </section>
            <section
              v-if="canManage && activeCase"
              class="surface-panel staff-card note-editor w3-container w3-margin-bottom w3-padding-large w3-section w3-white w3-round-xlarge w3-border w3-border-light-grey"
            >
              <p class="eyebrow w3-small w3-wide w3-text-grey">CASE NOTES</p>
              <h2 class="w3-sans-serif w3-large">Add A Note</h2>
              <p
                v-if="!online"
                class="offline-form-note w3-panel w3-pale-yellow"
                role="status"
              >
                Reconnect before adding a note.
              </p>
              <form
                class="staff-form w3-section"
                @submit.prevent="addNote"
              >
                <label
                  class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
                  for="visibility"
                >
                  Who Can See This?
                </label>
                <select
                  id="visibility"
                  v-model="visibility"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                >
                  <option value="Internal">Internal staff only</option>
                  <option value="Customer">Customer-visible update</option>
                </select>
                <div
                  v-if="visibility === 'Internal'"
                  class="visibility-warning w3-panel w3-pale-yellow w3-round-large w3-padding"
                >
                  <LockKeyhole
                    class="w3-margin-right"
                    :size="15"
                  />
                  This note will never appear in the customer's portal.
                </div>
                <label
                  class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
                  for="staff-note"
                >
                  Message
                </label>
                <textarea
                  id="staff-note"
                  v-model="note"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  rows="4"
                  :minlength="inputPolicy?.noteMinimumLength"
                  :maxlength="inputPolicy?.noteMaximumLength"
                  placeholder="Add a clear, relevant note…"
                  required
                />
                <button
                  class="button button-secondary full-width w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                  type="submit"
                  :disabled="saving || !inputPolicy || !note.trim() || !online"
                >
                  <MessageCircle
                    class="w3-margin-right"
                    :size="16"
                  />
                  Save Note
                </button>
              </form>
            </section>
            <section
              v-if="!canManage || (!activeCase && !choices.length)"
              class="surface-panel access-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
            >
              <ShieldCheck :size="20" />
              <b>Read-only access</b>
              <p>
                {{
                  activeCase
                    ? 'Your permissions allow you to review this case but not change it.'
                    : 'This case is closed. Its history and existing evidence remain available.'
                }}
              </p>
            </section>
          </aside>
        </div>
      </div>
    </section>
    <section
      v-else
      class="surface-panel not-found w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <h2 class="w3-sans-serif w3-large">Case Unavailable</h2>
      <p>{{ error }}</p>
    </section>
  </AppShell>
</template>
