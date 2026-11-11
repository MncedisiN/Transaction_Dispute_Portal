<script setup lang="ts">
import { computed, ref } from 'vue'
import { SlidersHorizontal, X } from '@lucide/vue'
import { priorityLabels } from '../operations'
import { reference, statusReferences } from '../portalReference'
import type { HistoryFilters } from '../utils/historyFilters'
import { emptyHistoryFilters } from '../utils/historyFilters'
const props = defineProps<{
  value: HistoryFilters
  kind: 'disputes' | 'messages' | 'transactions'
  hideStatus?: boolean
  staff?: boolean
  accounts?: {
    id: string
    accountType: string
    maskedAccountNumber: string
    accountNumber?: string | null
  }[]
}>()
const emit = defineEmits<{ apply: [value: HistoryFilters] }>(),
  sheet = ref<HTMLDialogElement | null>(null),
  draft = ref(emptyHistoryFilters())
const statuses = computed(() =>
  props.kind === 'transactions'
    ? [
        ['', 'All Statuses'],
        ...Object.entries(reference.transactionStatuses ?? {}).map(([code, label]) => [code, label])
      ]
    : [
        ['', 'All Statuses'],
        ['active', 'Open'],
        ['closed', 'Closed'],
        ...Object.entries(statusReferences).map(([code, status]) => [code, status.label])
      ]
)
const count = computed(
  () =>
    [
      props.value.status,
      props.value.account,
      props.value.from,
      props.value.to,
      props.value.unreadOnly,
      props.value.priority
    ].filter(Boolean).length
)
const invalid = computed(
  () => !!draft.value.from && !!draft.value.to && draft.value.from > draft.value.to
)
const heading = computed(
  () =>
    ({
      disputes: 'Dispute Filters',
      messages: 'Message Filters',
      transactions: 'Transaction Filters'
    })[props.kind]
)
function open() {
  draft.value = { ...props.value }
  sheet.value?.showModal()
}
function apply() {
  if (invalid.value) return
  emit('apply', { ...draft.value })
  sheet.value?.close()
}
</script>
<template>
  <button
    class="icon-button filter-toggle w3-button w3-round-large w3-block w3-border w3-white w3-large w3-padding-small"
    type="button"
    aria-label="Filters"
    aria-haspopup="dialog"
    @click="open"
  >
    <SlidersHorizontal :size="19" />
    <span
      v-if="count"
      class="filter-count"
    >
      {{ count }}
    </span>
  </button>
  <Teleport to="body">
    <dialog
      ref="sheet"
      class="filter-sheet w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
      :aria-label="heading"
      @click="
        (event) => {
          if (event.target === sheet) sheet?.close()
        }
      "
    >
      <div class="w3-bar">
        <div class="sheet-heading w3-bar w3-padding-16">
          <button
            type="button"
            class="icon-button w3-button w3-round-large w3-right"
            aria-label="Close Filters"
            @click="sheet?.close()"
          >
            <X :size="19" />
          </button>
          <h2 class="w3-sans-serif w3-large">{{ heading }}</h2>
        </div>
        <form @submit.prevent="apply">
          <div class="filter-stack">
            <label
              v-if="!hideStatus"
              class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            >
              <span class="filter-field-label w3-block w3-margin-top">Status</span>
              <select
                v-model="draft.status"
                class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                aria-label="Case Status"
              >
                <option
                  v-for="item in statuses"
                  :key="item[0]"
                  :value="item[0]"
                >
                  {{ item[1] }}
                </option>
              </select>
            </label>
            <label
              v-if="staff"
              class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            >
              <span class="filter-field-label w3-block w3-margin-top">Priority</span>
              <select
                v-model="draft.priority"
                class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                aria-label="Case Priority"
              >
                <option :value="undefined">All Priorities</option>
                <option
                  v-for="(_, level) in priorityLabels"
                  :key="level"
                  :value="String(level)"
                >
                  {{ priorityLabels[level] }}
                </option>
              </select>
            </label>
            <label
              v-if="accounts"
              class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            >
              <span class="filter-field-label w3-block w3-margin-top">Account</span>
              <select
                v-model="draft.account"
                class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                aria-label="Dispute Account"
              >
                <option value="">All My Accounts</option>
                <option
                  v-for="account in accounts"
                  :key="account.id"
                  :value="account.id"
                >
                  {{ account.accountType }} · {{ account.accountNumber || 'Number Unavailable' }}
                </option>
              </select>
            </label>
            <label
              v-if="kind === 'messages'"
              class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            >
              <span class="filter-field-label w3-block w3-margin-top">Messages</span>
              <select
                v-model="draft.unreadOnly"
                class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                aria-label="Message Visibility"
              >
                <option :value="false">All Conversations</option>
                <option :value="true">Unread Only</option>
              </select>
            </label>
            <fieldset class="transaction-date-range">
              <legend class="sr-only w3-hide">
                {{ kind === 'messages' ? 'Last Message' : 'Dispute Created' }}
                Date Range
              </legend>
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
                  :aria-invalid="invalid"
                />
              </label>
            </fieldset>
          </div>
          <p
            v-if="invalid"
            class="date-range-error w3-panel w3-pale-red"
            role="alert"
          >
            End date must be on or after start date.
          </p>
          <div class="filter-sheet-actions w3-section">
            <button
              type="button"
              class="text-button w3-button w3-text-black w3-round-large"
              @click="draft = emptyHistoryFilters()"
            >
              Reset
            </button>
            <button
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
              type="submit"
              :disabled="invalid"
            >
              Show {{ kind }}
            </button>
          </div>
        </form>
      </div>
    </dialog>
  </Teleport>
</template>
