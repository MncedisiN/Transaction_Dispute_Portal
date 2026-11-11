<script setup lang="ts">
import { ArrowDownLeft, ArrowLeftRight, ArrowUpRight } from '@lucide/vue'
import type { Transaction } from '../types'
import { formatDate, transactionTypeLabel, transactionStatusLabel } from '../utils/format'
import AmountText from './AmountText.vue'

defineProps<{ transactions: Transaction[]; loading?: boolean; clickable?: boolean }>()
const emit = defineEmits<{ select: [transaction: Transaction] }>()
</script>

<template>
  <div
    v-if="loading"
    class="list-skeleton"
    aria-label="Loading Transactions"
  >
    <span
      v-for="item in 4"
      :key="item"
    />
  </div>
  <div
    v-else-if="transactions.length === 0"
    class="empty-state compact-empty w3-container w3-center w3-padding-32 w3-padding-16"
  >
    <span
      class="empty-icon w3-tag w3-light-grey w3-text-blue-grey w3-round-xlarge w3-padding w3-margin-bottom"
    >
      <ArrowLeftRight :size="19" />
    </span>
    <b>No transactions found</b>
    <p>Try changing your filters or check back later.</p>
  </div>
  <div
    v-else
    class="transaction-list"
    role="list"
  >
    <button
      v-for="transaction in transactions"
      :key="transaction.id"
      type="button"
      class="transaction-row w3-button w3-block w3-left-align w3-border-bottom w3-padding-16 w3-white w3-padding-small"
      :class="{ 'row-clickable': clickable }"
      @click="emit('select', transaction)"
    >
      <span class="w3-cell-row">
        <span
          class="transaction-icon w3-cell w3-cell-middle w3-padding-small w3-text-blue-grey"
          :class="transaction.direction === 'Credit' ? 'icon-credit' : ''"
        >
          <ArrowDownLeft
            v-if="transaction.direction === 'Credit'"
            :size="18"
          />
          <ArrowUpRight
            v-else
            :size="18"
          />
        </span>
        <span class="transaction-info w3-cell w3-cell-middle">
          <b class="transaction-merchant w3-block">
            {{ transaction.type === 'Refund' ? 'Dispute Refund' : transaction.merchantName }}
          </b>
          <small class="transaction-date w3-small w3-block w3-text-grey w3-margin-top">
            <time
              :datetime="transaction.transactionAt"
              :title="formatDate(transaction.transactionAt)"
            >
              {{
                formatDate(transaction.transactionAt, {
                  day: '2-digit',
                  month: 'short',
                  year: 'numeric',
                  hour: '2-digit',
                  minute: '2-digit'
                })
              }}
            </time>
          </small>
          <small class="customer-transaction-description w3-block w3-small w3-text-grey">
            {{ transaction.description }}
          </small>
          <small class="transaction-type w3-block w3-small w3-text-grey">
            {{ transaction.typeLabel || transactionTypeLabel(transaction.type) }}
          </small>
        </span>
        <span class="transaction-right w3-cell w3-cell-middle w3-right-align w3-padding-small">
          <AmountText
            :amount="transaction.amount"
            :currency="transaction.currency"
            :direction="transaction.direction"
          />
          <span class="settled-label w3-block w3-small w3-text-grey">
            {{ transactionStatusLabel(transaction.status) }}
          </span>
        </span>
      </span>
    </button>
  </div>
</template>
