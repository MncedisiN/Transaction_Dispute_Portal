<script setup lang="ts">
import { clientOption } from '../utils/portalContent'
import { isClosedStatus, statusReferences } from '../portalReference'

import { useFeedback } from '../composables/useFeedback'

import AccountBalances from '../components/AccountBalances.vue'
import type { AccountBalance } from '../financial'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import {
  ArrowRight,
  ArrowUpRight,
  BadgeCheck,
  FileClock,
  ShieldCheck,
  WalletCards
} from '@lucide/vue'
import { api } from '../api'
import { useAccountsStore } from '../stores/accounts'
import type { Account, DisputeSummary, Transaction } from '../types'
import { useQuietLiveRefresh } from '../composables/useQuietLiveRefresh'
import { useAuthStore } from '../stores/auth'
import { useNow } from '../composables/useNow'
import { formatDate, formatMoney } from '../utils/format'
import AccountSwitcher from '../components/AccountSwitcher.vue'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import TransactionList from '../components/TransactionList.vue'
import StatusBadge from '../components/StatusBadge.vue'

const auth = useAuthStore()
const accountStore = useAccountsStore()
const accounts = computed(() => accountStore.items)
const balanceRefresh = ref(0)
const accountBalances = ref<AccountBalance[]>([])
function accountAmount(id: string) {
  const balance = accountBalances.value.find((item) => item.accountId === id)
  return !balance
    ? '—'
    : balance.balanceKnown
      ? formatMoney(balance.currentBalance ?? 0, balance.currency)
      : 'Awaiting settlement'
}
const activeCount = ref(0)
const transactions = ref<Transaction[]>([])
const disputes = ref<DisputeSummary[]>([])
const loading = ref(true)
const error = ref('')
const clock = useNow()
const today = computed(() =>
  formatDate(new Date(clock.value).toISOString(), {
    weekday: 'long',
    day: 'numeric',
    month: 'long'
  }).toUpperCase()
)
const firstName = computed(() => auth.user?.displayName.split(' ')[0] ?? 'there')
const activeDisputes = computed(() => disputes.value.filter((item) => !isClosedStatus(item.status)))

let generation = 0
let request: AbortController | undefined
async function fetchData(quiet = false) {
  if (!quiet) balanceRefresh.value++
  const current = ++generation,
    owner = auth.user?.id
  request?.abort()
  request = new AbortController()
  if (!quiet) {
    loading.value = true
    error.value = ''
    transactions.value = []
    disputes.value = []
  }
  try {
    const params = {
      accountId: accountStore.selectedId,
      page: 1,
      pageSize: clientOption('disputePageSize')
    }
    const [transactionData, accountItems, ...activePages] = await Promise.all([
      api.page<Transaction>(
        '/api/v1/transactions',
        { ...params, pageSize: clientOption('transactionPageSize') },
        request.signal
      ),
      quiet
        ? api.get<Account[]>('/api/v1/accounts', request.signal).catch(() => null)
        : Promise.resolve(null),
      ...Object.keys(statusReferences)
        .filter((status) => !isClosedStatus(status))
        .map((status) =>
          api.page<DisputeSummary>('/api/v1/disputes', { ...params, status }, request!.signal)
        )
    ])
    if (current !== generation) return
    if (accountItems && accountStore.owner === owner) accountStore.items = accountItems
    error.value = ''
    transactions.value = transactionData.items
    disputes.value = activePages
      .flatMap((result) => result.items)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    activeCount.value = activePages.reduce((sum, result) => sum + result.total, 0)
  } catch {
    if (current === generation && !quiet)
      error.value = 'Please refresh to load your account overview.'
  } finally {
    if (current === generation) {
      if (!quiet) loading.value = false
      liveReady()
    }
  }
}
async function load() {
  await fetchData()
}
const liveReady = useQuietLiveRefresh(
  () => fetchData(true),
  () => loading.value,
  ['banking', 'disputes']
)
onMounted(load)
onUnmounted(() => {
  generation++
  request?.abort()
})
watch(() => accountStore.selectedId, load)
useFeedback(error, undefined, undefined)
</script>

<template>
  <AppShell>
    <div class="dashboard-page">
      <div class="welcome-line">
        <PageHeading
          :eyebrow="today"
          :title="'Hi, ' + firstName"
        />
        <div class="welcome-art">
          <div class="art-ring art-ring-one" />
          <div class="art-ring art-ring-two" />
          <span><ShieldCheck :size="34" /></span>
        </div>
      </div>
      <div
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
      </div>
      <AccountBalances
        :refresh-key="balanceRefresh"
        @loaded="accountBalances = $event"
      />
      <section
        class="home-accounts"
        aria-label="Your Accounts"
      >
        <div class="home-section-label">
          <h2 class="w3-sans-serif w3-large">Accounts</h2>
          <span>{{ accounts.length }} linked</span>
        </div>
        <div class="home-account-list">
          <RouterLink
            v-for="account in accounts"
            :key="account.id"
            :to="'/accounts/' + account.id"
            class="home-account-card"
          >
            <WalletCards :size="20" />
            <div>
              <strong>{{ account.accountType }}</strong>
              <span>{{ account.accountNumber || 'Number Unavailable' }}</span>
              <b class="home-account-balance">{{ accountAmount(account.id) }}</b>
            </div>
            <ArrowUpRight :size="16" />
          </RouterLink>
        </div>
      </section>
      <RouterLink
        to="/disputes"
        class="home-case-summary"
      >
        <span class="metric-icon"><FileClock :size="18" /></span>
        <span>
          <strong>
            {{ loading ? '—' : activeCount }} Open {{ activeCount === 1 ? 'case' : 'cases' }}
          </strong>
          <small class="w3-small">
            {{
              activeDisputes.some((d) => d.status === 'AwaitingCustomer')
                ? 'A Case Needs Your Reply'
                : 'Track Your Disputes'
            }}
          </small>
        </span>
        <ArrowRight :size="16" />
      </RouterLink>

      <div class="dashboard-columns w3-row-padding">
        <div class="w3-col s12 m6 w3-margin-bottom">
          <section
            class="surface-panel recent-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <div class="panel-heading w3-bar w3-padding-16">
              <div>
                <p class="eyebrow w3-small w3-wide w3-text-grey">YOUR ACTIVITY</p>
                <h2 class="w3-sans-serif w3-large">Recent Transactions</h2>
              </div>
              <RouterLink
                to="/transactions"
                class="quiet-link"
              >
                View All
                <ArrowRight :size="15" />
              </RouterLink>
            </div>
            <AccountSwitcher compact />
            <TransactionList
              :transactions="transactions"
              :loading="loading"
              clickable
              @select="(item) => $router.push('/transactions/' + item.id)"
            />
          </section>
        </div>
        <div class="w3-col s12 m6 w3-margin-bottom">
          <section
            class="surface-panel dispute-preview w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <div class="panel-heading w3-bar w3-padding-16">
              <div>
                <p class="eyebrow w3-small w3-wide w3-text-grey">CASE UPDATES</p>
                <h2 class="w3-sans-serif w3-large">Dispute Tracker</h2>
              </div>
              <span class="preview-icon"><BadgeCheck :size="18" /></span>
            </div>
            <div
              v-if="loading"
              class="small-loading"
            />
            <div
              v-else-if="activeDisputes.length"
              class="dispute-mini-list"
            >
              <RouterLink
                v-for="item in activeDisputes.slice(0, 3)"
                :key="item.id"
                :to="'/disputes/' + item.id"
                class="dispute-mini"
              >
                <div class="dispute-mini-top">
                  <b>{{ item.reference }}</b>
                  <StatusBadge :status="item.status" />
                </div>
                <p>{{ item.transactionDescription }}</p>
                <small class="w3-small">{{ formatDate(item.createdAt) }}</small>
              </RouterLink>
              <RouterLink
                to="/disputes"
                class="all-cases-link"
              >
                All Disputes
                <ArrowRight :size="15" />
              </RouterLink>
            </div>
            <div
              v-else
              class="empty-state compact-empty w3-container w3-center w3-padding-32 w3-padding-16"
            >
              <span
                class="empty-icon w3-tag w3-light-grey w3-text-blue-grey w3-round-xlarge w3-padding w3-margin-bottom"
              >
                <BadgeCheck :size="19" />
              </span>
              <b>All clear for now</b>
              <p>Your submitted disputes and updates will appear here.</p>
              <RouterLink
                to="/transactions"
                class="text-link w3-text-blue-grey"
              >
                Review A Transaction
                <ArrowRight :size="15" />
              </RouterLink>
            </div>
          </section>
        </div>
      </div>
    </div>
  </AppShell>
</template>
