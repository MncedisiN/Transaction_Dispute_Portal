<script setup lang="ts">
import DisclosurePanel from './DisclosurePanel.vue'
import { useFeedback } from '../composables/useFeedback'

import { computed, onUnmounted, ref, watch } from 'vue'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { api } from '../api'
import type { Account } from '../types'
import {
  uniqueBalances,
  type AccountBalance,
  type BalanceEntry,
  type BalanceOverview
} from '../financial'
import { formatMoney, formatDate } from '../utils/format'
const props = defineProps<{
  accountId?: string
  showAccounts?: boolean
  history?: boolean
  refreshKey?: number
}>()
const emit = defineEmits<{ loaded: [balances: AccountBalance[]] }>()
const overview = ref<BalanceOverview | null>(null)
const balances = ref<AccountBalance[]>([]),
  accounts = ref<Account[]>([]),
  entries = ref<BalanceEntry[]>([]),
  loading = ref(true),
  error = ref('')
let controller: AbortController | undefined,
  generation = 0,
  disposed = false
const summary = computed(
  () =>
    overview.value ?? {
      currentBalance: null,
      availableBalance: null,
      accountCount: 0,
      balanceKnown: false,
      currency: null
    }
)
const selected = computed(() => balances.value.find((x) => x.accountId === props.accountId))
async function load() {
  const current = ++generation
  controller?.abort()
  controller = new AbortController()
  try {
    const [a, b, h] = await Promise.all([
      api.get<Account[]>('/api/v1/accounts', controller.signal),
      api.get<BalanceOverview>('/api/v1/finance/overview', controller.signal),
      props.accountId && props.history
        ? api.get<BalanceEntry[]>(
            '/api/v1/finance/accounts/' + props.accountId + '/history',
            controller.signal
          )
        : Promise.resolve([])
    ])
    if (disposed || current !== generation) return
    accounts.value = a
    overview.value = b
    balances.value = uniqueBalances(b.accounts)
    emit('loaded', balances.value)
    entries.value = h
    error.value = ''
  } catch {
    if (!disposed && current === generation && !controller.signal.aborted) {
      error.value = 'Balances are temporarily unavailable.'
      emit('loaded', [])
    }
  } finally {
    if (!disposed && current === generation) loading.value = false
  }
}
useLiveRefresh(() => load(), ['banking'])
watch(
  () => [props.accountId, props.refreshKey],
  () => void load()
)
onUnmounted(() => {
  disposed = true
  generation++
  controller?.abort()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <section
    class="surface-panel balance-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    aria-label="Account Balances"
    :aria-busy="loading"
  >
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
    <template v-else-if="accountId">
      <span class="balance-caption w3-block w3-small w3-text-grey">Available Balance</span>
      <strong class="balance-value w3-xxlarge">
        {{
          loading
            ? '—'
            : selected?.balanceKnown
              ? formatMoney(selected.availableBalance ?? 0, selected.currency)
              : 'Awaiting settlement'
        }}
      </strong>
      <div class="w3-responsive">
        <table class="balance-breakdown w3-table w3-bordered">
          <tbody>
            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Current Balance
              </th>
              <td class="w3-mobile">
                {{
                  selected?.balanceKnown
                    ? formatMoney(selected.currentBalance ?? 0, selected.currency)
                    : '—'
                }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
    <template v-else>
      <span class="balance-caption w3-block w3-small w3-text-grey">Main Balance</span>
      <strong class="balance-value w3-xxlarge">
        {{
          loading
            ? '—'
            : summary.balanceKnown
              ? formatMoney(summary.currentBalance ?? 0, summary.currency || undefined)
              : 'Awaiting settlement'
        }}
      </strong>
      <div class="w3-responsive">
        <table class="balance-breakdown w3-table w3-bordered">
          <tbody>
            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Available
              </th>
              <td class="w3-mobile">
                {{
                  loading || !summary.balanceKnown
                    ? '—'
                    : formatMoney(summary.availableBalance ?? 0, summary.currency || undefined)
                }}
              </td>
            </tr>
            <tr>
              <th
                class="w3-mobile w3-text-grey w3-small"
                scope="row"
              >
                Active Accounts
              </th>
              <td class="w3-mobile">{{ summary.accountCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
    <ul
      v-if="showAccounts"
      class="balance-account-list w3-ul w3-section"
    >
      <li
        v-for="account in accounts"
        :key="account.id"
      >
        <RouterLink :to="'/accounts/' + account.id">
          <span>
            <strong>{{ account.accountType }}</strong>
            <small class="w3-small">
              {{ account.accountNumber || 'Number Unavailable' }} · {{ account.status }}
            </small>
          </span>
          <b>
            {{
              balances.find((x) => x.accountId === account.id)?.balanceKnown
                ? formatMoney(
                    balances.find((x) => x.accountId === account.id)?.currentBalance ?? 0,
                    account.currency
                  )
                : 'Awaiting Settlement'
            }}
          </b>
        </RouterLink>
      </li>
    </ul>
    <DisclosurePanel
      v-if="history && entries.length"
      class="balance-history"
    >
      <template #heading>Balance History</template>
      <ol>
        <li
          v-for="entry in entries"
          :key="entry.entryId"
        >
          <span>
            {{ entry.description }}
            <time>{{ formatDate(entry.recordedAt) }}</time>
          </span>
          <b>{{ formatMoney(entry.currentDelta, selected?.currency) }}</b>
          <small
            v-if="entry.reservedDelta"
            class="w3-small"
          >
            Reserved {{ formatMoney(entry.reservedDelta, selected?.currency) }}
          </small>
        </li>
      </ol>
    </DisclosurePanel>
  </section>
</template>
