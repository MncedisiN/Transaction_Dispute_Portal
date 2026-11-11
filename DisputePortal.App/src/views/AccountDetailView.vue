<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import AccountBalances from '../components/AccountBalances.vue'
import BackLink from '../components/BackLink.vue'
import AccountNumberControl from '../components/AccountNumberControl.vue'
import { onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ArrowRight, RefreshCw, WalletCards } from '@lucide/vue'
import { api, ApiError } from '../api'
import type { Account } from '../types'
import { formatDate } from '../utils/format'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
const balanceRefresh = ref(0)
const route = useRoute(),
  account = ref<Account | null>(null),
  error = ref(''),
  feed = ref<{ connected: boolean; canRefresh: boolean; lastSyncedAt: string | null } | null>(null),
  refreshing = ref(false),
  notice = ref('')
let generation = 0,
  request: AbortController | undefined
async function load(id: string) {
  const current = ++generation
  request?.abort()
  request = new AbortController()
  account.value = null
  feed.value = null
  error.value = ''
  try {
    const data = await api.get<Account>('/api/v1/accounts/' + id, request.signal)
    if (current !== generation) return
    account.value = data
    try {
      const status = await api.get<typeof feed.value>(
        '/api/v1/accounts/' + id + '/connection',
        request.signal
      )
      if (current === generation) feed.value = status
    } catch {
      /* Account history remains usable if the connector is unavailable. */
    }
  } catch (e) {
    if (current === generation)
      error.value = e instanceof ApiError ? e.message : 'Account unavailable.'
  }
}
async function refresh() {
  if (!account.value || refreshing.value) return
  refreshing.value = true
  error.value = ''
  try {
    const result = await api.post<{ moreAvailable: boolean }>(
      '/api/v1/accounts/' + account.value.id + '/refresh'
    )
    balanceRefresh.value++
    notice.value = result.moreAvailable
      ? 'Transactions updated. More history is available on the next refresh.'
      : 'Transactions are up to date.'
    feed.value = await api.get('/api/v1/accounts/' + account.value.id + '/connection')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Please try refreshing again.'
  } finally {
    refreshing.value = false
  }
}
watch(
  () => route.params.id,
  (id) => {
    notice.value = ''
    void load(String(id))
  },
  { immediate: true }
)
onUnmounted(() => {
  generation++
  request?.abort()
})
useFeedback(error, undefined, notice)
</script>
<template>
  <AppShell>
    <div class="account-detail-page">
      <BackLink
        fallback="/"
        label="Back To Overview"
      />
      <PageHeading :title="account ? account.accountType + ' Account' : 'Account'" />
      <p
        v-if="error"
        role="alert"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      >
        {{ error }}
      </p>
      <p
        v-if="notice"
        role="status"
        class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
      >
        {{ notice }}
      </p>
      <AccountBalances
        v-if="account"
        :account-id="account.id"
        :refresh-key="balanceRefresh"
      />
      <section
        v-if="account"
        class="surface-panel account-overview w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <div class="account-identity w3-section">
          <span class="thread-avatar w3-margin-right w3-text-blue-grey w3-left w3-padding-small">
            <WalletCards :size="22" />
          </span>
          <div>
            <AccountNumberControl :account="account" />
            <span>{{ account.currency }} · {{ account.status }}</span>
          </div>
        </div>
        <div class="w3-responsive">
          <table class="detail-definition w3-container w3-table w3-bordered">
            <tbody>
              <tr>
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Opened
                </th>
                <td class="w3-mobile">
                  {{
                    formatDate(account.registeredAt, {
                      day: 'numeric',
                      month: 'short',
                      year: 'numeric'
                    })
                  }}
                </td>
              </tr>
              <tr v-if="feed?.connected">
                <th
                  class="w3-mobile w3-text-grey w3-small"
                  scope="row"
                >
                  Last Refreshed
                </th>
                <td class="w3-mobile">
                  {{
                    feed.lastSyncedAt
                      ? formatDate(feed.lastSyncedAt, {
                          day: 'numeric',
                          month: 'short',
                          hour: '2-digit',
                          minute: '2-digit'
                        })
                      : 'Awaiting first sync'
                  }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div
          v-if="feed?.canRefresh"
          class="account-refresh w3-section"
        >
          <button
            class="text-button w3-button w3-text-black w3-round-large"
            :disabled="refreshing"
            @click="refresh"
          >
            <RefreshCw
              class="w3-margin-right"
              :size="15"
            />
            {{ refreshing ? 'Refreshing…' : 'Refresh Transactions' }}
          </button>
        </div>
        <RouterLink
          :to="{ path: '/transactions', query: { account: account.id } }"
          class="account-action-row w3-button w3-block w3-left-align w3-padding-16 w3-border-bottom w3-hover-light-grey"
        >
          <span>
            <strong>Transactions</strong>
            <small class="w3-small">Payments And Transfers</small>
          </span>
          <ArrowRight :size="17" />
        </RouterLink>
        <RouterLink
          :to="{ path: '/disputes', query: { account: account.id } }"
          class="account-action-row w3-button w3-block w3-left-align w3-padding-16 w3-border-bottom w3-hover-light-grey"
        >
          <span>
            <strong>Disputes</strong>
            <small class="w3-small">Linked Cases</small>
          </span>
          <ArrowRight :size="17" />
        </RouterLink>
        <RouterLink
          to="/accounts/manage"
          class="account-action-row w3-button w3-block w3-left-align w3-padding-16 w3-border-bottom w3-hover-light-grey"
        >
          <span><strong>Account Services</strong></span>
          <ArrowRight :size="17" />
        </RouterLink>
      </section>
    </div>
  </AppShell>
</template>
