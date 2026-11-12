<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import {
  ArrowUpRight,
  ArrowRightLeft,
  ArrowRight,
  Plus,
  ShieldCheck,
  X,
  WalletCards,
  FileClock
} from '@lucide/vue'
import AppShell from '../components/AppShell.vue'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { useFeedback } from '../composables/useFeedback'
import { requestId as createRequestId } from '../utils/requestId'
import { api, ApiError } from '../api'
import { loadPlatform, platform, money, type FinancialAccount, type Quote } from '../platform'
import { reference } from '../portalReference'
import { useAuthStore } from '../stores/auth'
import { readTabValue, saveTabValue, removeTabValue } from '../utils/sessionStorage'
const auth = useAuthStore(),
  route = useRoute()
const accounts = ref<FinancialAccount[]>([]),
  selected = ref(typeof route.query.account === 'string' ? route.query.account : ''),
  loading = ref(true),
  error = ref(''),
  notice = ref(''),
  busy = ref(false)
const modal = ref<HTMLDialogElement | null>(null),
  action = ref<'Transfer' | 'Account'>('Transfer'),
  amount = ref(''),
  recipient = ref(''),
  quote = ref<Quote | null>(null),
  target = ref<{ id: string; type: string; number: string; currency: string } | null>(null),
  requestId = ref(''),
  newType = ref('')
const account = computed(() => accounts.value.find((a) => a.id === selected.value)),
  country = computed(() =>
    platform.settings?.countries.find((c) => c.code === platform.settings?.operatingCountry)
  )
const newCurrency = computed(() => country.value?.defaultCurrency ?? '')
const canOpenAccount = computed(() => reference.customerAccountOpeningEnabled === true)
function statusLabel(value: string) {
  return reference.paymentStatuses?.[value] ?? value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

const transferAccounts = ref<FinancialAccount[]>([])
const clock = ref(Date.now())
const quoteTimer = setInterval(() => {
  if (modal.value?.open) clock.value = Date.now()
}, 1000)
onUnmounted(() => clearInterval(quoteTimer))
const transferProblem = computed(() => {
  if (!account.value) return 'Select a source account.'
  if (!transferAccounts.value.length) return 'No eligible destination accounts are available.'
  if (!recipient.value) return 'Select a destination account.'
  if (!amount.value.trim()) return 'Enter an amount.'
  const entered = Number(amount.value)
  if (!/^\d+(?:\.\d+)?$/.test(amount.value) || !Number.isFinite(entered) || entered <= 0)
    return 'Enter an amount greater than zero, using numbers and a decimal point.'
  if (account.value && entered > account.value.availableBalance)
    return (
      'Insufficient funds. Your available balance is ' +
      money(account.value.availableBalance, account.value.currency, account.value.countryCode) +
      '.'
    )
  if (quote.value && Date.parse(quote.value.expiresAt) <= clock.value)
    return 'The quote expired. Review a fresh quote before confirming.'
  return ''
})
let optionsRequest: AbortController | undefined
onUnmounted(() => optionsRequest?.abort())

const pendingKey = 'portal.payment-request:' + (auth.user?.id ?? ''),
  pendingRequest = ref(readTabValue(pendingKey) ?? '')
let disposed = false,
  request: AbortController | undefined
function clearPending() {
  removeTabValue(pendingKey)
  pendingRequest.value = ''
}
async function checkPending() {
  if (!pendingRequest.value || disposed) return
  try {
    const result = await api.get<{ status: string }>(
      '/api/v1/platform/payments/requests/' + pendingRequest.value
    )
    if (disposed) return
    if (result.status === 'Preparing') return
    clearPending()
    notice.value = 'Your request is ' + statusLabel(result.status).toLowerCase() + '.'
    await refresh(true)
  } catch {
    /* Keep uncertain requests until acknowledged or fenced. */
  }
}
async function cancelPending() {
  if (!pendingRequest.value || busy.value) return
  busy.value = true
  try {
    const result = await api.post<{ cancelled: boolean; payment?: { status: string } }>(
      '/api/v1/platform/payments/requests/' + pendingRequest.value + '/cancel'
    )
    if (result.cancelled || result.payment?.status !== 'Preparing') clearPending()
    notice.value = result.cancelled
      ? 'The request was cancelled before it started.'
      : 'This request has already started. Its status is ' +
        statusLabel(result.payment?.status ?? 'Processing') +
        '.'
    await refresh(true)
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
async function refresh(quiet = false) {
  if (!quiet) loading.value = true
  request?.abort()
  const current = new AbortController()
  request = current
  try {
    const a = await api.get<FinancialAccount[]>('/api/v1/platform/accounts', current.signal)
    if (disposed || current.signal.aborted) return
    accounts.value = a
    if (!a.some((i) => i.id === selected.value)) selected.value = a[0]?.id ?? ''
    error.value = ''
  } catch (e) {
    if (!current.signal.aborted && !disposed)
      error.value = e instanceof ApiError ? e.message : 'Your accounts are temporarily unavailable.'
  } finally {
    if (!disposed && !current.signal.aborted) loading.value = false
  }
}
onMounted(async () => {
  try {
    const settings = await loadPlatform()
    newType.value = settings.accountTypes[0] ?? ''
    await refresh()
    await checkPending()
  } catch (e) {
    error.value = (e as Error).message
    loading.value = false
  }
})
useLiveRefresh(async () => {
  await refresh(true)
  await checkPending()
}, ['banking', 'disputes'])
onUnmounted(() => {
  disposed = true
  request?.abort()
})
watch([selected, action, amount, recipient], () => {
  quote.value = null
  target.value = null
})
async function open(kind: 'Transfer' | 'Account') {
  if (kind === 'Account' && !canOpenAccount.value) return
  if (pendingRequest.value && kind === 'Transfer') {
    void checkPending()
    return
  }
  optionsRequest?.abort()
  const options = new AbortController()
  optionsRequest = options
  try {
    if (kind === 'Account') await loadPlatform(true)
    else
      transferAccounts.value = await api.get<FinancialAccount[]>(
        '/api/v1/platform/accounts/' + selected.value + '/transfer-targets',
        options.signal
      )
    if (disposed || options.signal.aborted) return
  } catch (cause) {
    if (!options.signal.aborted && !disposed)
      error.value =
        cause instanceof ApiError ? cause.message : 'Account options are temporarily unavailable.'
    return
  }
  action.value = kind
  amount.value = ''
  recipient.value = transferAccounts.value[0]?.id ?? ''
  quote.value = null
  target.value = null
  requestId.value = createRequestId()
  error.value = ''
  modal.value?.showModal()
}
async function transact() {
  if (busy.value) return
  if (action.value === 'Transfer' && transferProblem.value) {
    error.value = transferProblem.value
    return
  }
  busy.value = true
  error.value = ''
  try {
    if (action.value === 'Account') {
      if (!canOpenAccount.value) throw new Error('Account opening is currently unavailable.')
      await api.post('/api/v1/platform/accounts', {
        requestId: requestId.value,
        type: newType.value
      })
      notice.value = 'Your account is being activated.'
      modal.value?.close()
      await refresh(true)
      return
    }
    if (!quote.value) {
      if (!account.value) throw new Error('Select an account first.')
      target.value = transferAccounts.value.find((a) => a.id === recipient.value) ?? null
      if (!target.value) throw new Error('Select another active account belonging to your profile.')
      quote.value = await api.post<Quote>('/api/v1/platform/payments/quote', {
        kind: 'Transfer',
        accountId: account.value.id,
        recipientAccountId: target.value?.id,
        amount: amount.value
      })
      return
    }
    if (Date.parse(quote.value.expiresAt) <= Date.now()) {
      quote.value = null
      throw new Error('The quote expired. Review a fresh quote before confirming.')
    }
    pendingRequest.value = requestId.value
    saveTabValue(pendingKey, requestId.value)
    await api.post('/api/v1/platform/payments', {
      requestId: requestId.value,
      kind: 'Transfer',
      accountId: quote.value.accountId,
      recipientAccountId: quote.value.recipientAccountId,
      amount: quote.value.amountExact ?? String(quote.value.amount),
      quoteId: quote.value.id
    })
    clearPending()
    notice.value = 'Your transfer is processing. We will update its confirmed status here.'
    modal.value?.close()
    await refresh(true)
  } catch (e) {
    if (e instanceof ApiError && [400, 403, 404, 409, 422].includes(e.status)) {
      clearPending()
      requestId.value = createRequestId()
      quote.value = null
    }
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
useFeedback(error, undefined, notice)
</script>
<template>
  <AppShell>
    <div class="account-hub">
      <div
        v-if="pendingRequest"
        class="info-notice w3-panel w3-pale-yellow w3-round-large w3-padding-16"
      >
        <p>A previous transfer is awaiting a confirmed response.</p>
        <button
          class="button button-secondary w3-button w3-border w3-white w3-round-large"
          :disabled="busy"
          @click="cancelPending"
        >
          Check Or Cancel Request
        </button>
      </div>
      <section class="account-hub-grid w3-row">
        <div class="account-main w3-col l8 m12 s12 w3-padding-small">
          <div
            v-if="canOpenAccount"
            class="account-opening-action"
          >
            <span class="w3-cell w3-cell-middle w3-right-align">
              <button
                class="text-button w3-button w3-text-blue-grey w3-round-large w3-small w3-padding-small"
                @click="open('Account')"
              >
                <Plus
                  :size="16"
                  class="w3-margin-right"
                />
                Open Account
              </button>
            </span>
          </div>
          <nav
            class="account-tabs w3-row w3-margin-bottom"
            aria-label="Choose Account"
          >
            <div
              v-for="a in accounts"
              :key="a.id"
              class="w3-col s6 w3-margin-bottom"
            >
              <button
                class="w3-button w3-block w3-border w3-round-large w3-padding-16 w3-left-align"
                :class="selected === a.id ? 'w3-theme-d5' : 'w3-white'"
                :aria-pressed="selected === a.id"
                @click="selected = a.id"
              >
                <b class="w3-block">{{ a.type }}</b>
                <small class="w3-block w3-small">{{ a.currency }}</small>
              </button>
            </div>
          </nav>
          <RouterLink
            v-if="account"
            :to="'/accounts/' + account.id"
            class="premium-account-card w3-button w3-white w3-text-black w3-border w3-border-light-grey w3-hover-white w3-left-align w3-padding-large w3-round-xlarge w3-block w3-margin-bottom"
            aria-label="Open Account Details"
          >
            <div class="premium-account-top w3-cell-row w3-padding-16">
              <span class="w3-cell w3-cell-middle w3-large">
                <WalletCards
                  :size="21"
                  class="w3-margin-right"
                />
                <b>{{ account.type }} Account</b>
              </span>
              <span class="w3-cell w3-cell-middle w3-right-align">
                <span class="currency-chip w3-tag w3-white w3-round w3-small">
                  {{ account.currency }}
                </span>
              </span>
            </div>
            <div class="premium-account-balance w3-padding-16">
              <span class="w3-block w3-text-grey w3-medium">Available Balance</span>
              <strong class="w3-block w3-xxlarge w3-margin-top">
                {{ money(account.availableBalance, account.currency, account.countryCode) }}
              </strong>
            </div>
            <div class="premium-account-number w3-cell-row w3-padding-16">
              <span class="w3-cell w3-cell-middle w3-small">{{ account.number }}</span>
              <span class="w3-cell w3-cell-middle w3-right-align w3-text-blue-grey w3-small">
                View Account
                <ArrowRight :size="16" />
              </span>
            </div>
            <p
              v-if="account.reservedBalance > 0"
              class="account-balance-note"
            >
              Available balance excludes pending payments.
            </p>
          </RouterLink>
          <div
            v-else
            class="surface-panel account-empty w3-white w3-container w3-padding-32 w3-round-xlarge w3-margin-bottom w3-padding-large w3-border w3-border-light-grey"
          >
            <WalletCards :size="30" />
            <h2 class="w3-sans-serif w3-large">
              {{ loading ? 'Loading Your Accounts' : 'No Accounts Yet' }}
            </h2>
            <p>
              {{
                loading
                  ? 'We are retrieving your account information.'
                  : canOpenAccount
                    ? 'Open an account to get started.'
                    : 'Contact Support to link an account.'
              }}
            </p>
          </div>
          <div
            v-if="account"
            class="account-primary-actions w3-row w3-section"
          >
            <div class="w3-col s12 w3-margin-bottom">
              <RouterLink
                class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16"
                :to="{ path: '/transactions', query: { account: account.id } }"
              >
                <FileClock
                  :size="18"
                  class="w3-margin-right"
                />
                View Transactions
              </RouterLink>
            </div>
            <div class="w3-col s12 w3-margin-bottom">
              <button
                class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding-16"
                :disabled="!!pendingRequest || loading || !account"
                @click="open('Transfer')"
              >
                <ArrowRightLeft
                  :size="18"
                  class="w3-margin-right"
                />
                Transfer
              </button>
            </div>
          </div>
        </div>
        <aside class="w3-col l4 m12 s12 w3-padding-small">
          <div
            class="account-help-card w3-white w3-border w3-round-xlarge w3-container w3-padding-large w3-section"
          >
            <div class="account-help-heading w3-cell-row w3-padding-16">
              <span class="w3-cell w3-cell-middle w3-text-red"><ShieldCheck :size="28" /></span>
              <h2 class="w3-cell w3-cell-middle w3-large w3-sans-serif"><b>Disputes</b></h2>
            </div>
            <RouterLink
              class="disputes-primary-action w3-button w3-block w3-left-align w3-padding-16 w3-border-bottom"
              :to="{ path: '/disputes', query: { account: selected || undefined } }"
            >
              View Disputes
              <ArrowUpRight
                :size="18"
                class="w3-right"
              />
            </RouterLink>
            <RouterLink
              class="w3-button w3-block w3-left-align w3-padding-16"
              to="/support"
            >
              Contact Support
              <ArrowUpRight
                :size="18"
                class="w3-right"
              />
            </RouterLink>
          </div>
        </aside>
      </section>
      <dialog
        ref="modal"
        class="account-action-dialog w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
        @click="
          (e) => {
            if (e.target === modal) modal?.close()
          }
        "
      >
        <div class="w3-bar">
          <header>
            <button
              type="button"
              class="icon-button w3-button w3-round-large w3-right"
              aria-label="Close"
              @click="modal?.close()"
            >
              <X :size="20" />
            </button>
            <div>
              <p class="eyebrow w3-small w3-wide w3-text-grey">
                {{ action === 'Account' ? 'ACCOUNT SERVICES' : 'SECURE TRANSFER' }}
              </p>
              <h2 class="w3-sans-serif w3-large">
                {{
                  action === 'Account'
                    ? 'Open Account'
                    : quote
                      ? 'Review Transfer'
                      : 'Transfer Between My Accounts'
                }}
              </h2>
            </div>
          </header>
          <form
            class="ops-form"
            @submit.prevent="transact"
          >
            <template v-if="action === 'Account'">
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Account Type
                <select
                  v-model="newType"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                >
                  <option
                    v-for="type in platform.settings?.accountTypes"
                    :key="type"
                  >
                    {{ type }}
                  </option>
                </select>
              </label>
              <div class="new-account-context w3-panel w3-light-grey w3-round-large w3-padding">
                <span class="w3-block w3-text-grey">Country</span>
                <strong class="w3-block w3-margin-bottom">{{ country?.name }}</strong>
                <span class="w3-block w3-text-grey">Account Currency</span>
                <strong class="w3-block">{{ newCurrency }}</strong>
              </div>
            </template>
            <template v-else-if="!quote">
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                From Account
                <input
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  :value="account ? account.type + ' · ' + account.number : ''"
                  readonly
                />
              </label>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                To Account
                <select
                  v-model="recipient"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                  aria-label="To Account"
                >
                  <option
                    value=""
                    disabled
                  >
                    Select Your Account
                  </option>
                  <option
                    v-for="destination in transferAccounts"
                    :key="destination.id"
                    :value="destination.id"
                  >
                    {{ destination.type }} · {{ destination.number }}
                  </option>
                </select>
              </label>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Amount ({{ account?.currency }})
                <input
                  v-model="amount"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                  type="text"
                  pattern="[0-9]+([.][0-9]+)?"
                  maxlength="80"
                  autocomplete="off"
                  inputmode="decimal"
                />
              </label>
            </template>
            <dl
              v-else
              class="record-summary transfer-review"
            >
              <div>
                <dt>From Account</dt>
                <dd>
                  {{ account?.type }}
                  <small>{{ account?.number }}</small>
                </dd>
              </div>
              <div>
                <dt>To Your Account</dt>
                <dd>
                  {{ target?.type }}
                  <small>{{ target?.number }} · {{ target?.currency }}</small>
                </dd>
              </div>
              <div>
                <dt>Amount</dt>
                <dd>{{ money(quote.amount, quote.currency) }}</dd>
              </div>
              <div>
                <dt>Fee</dt>
                <dd>{{ money(quote.fee, quote.currency) }}</dd>
              </div>
              <div>
                <dt>Total Debit</dt>
                <dd class="summary-total">{{ quote.totalDebitExact }} {{ quote.currency }}</dd>
              </div>
              <div>
                <dt>Quote Expires</dt>
                <dd>{{ new Date(quote.expiresAt).toLocaleTimeString() }}</dd>
              </div>
            </dl>
            <p
              v-if="action === 'Transfer' && transferProblem"
              class="field-hint transfer-validation w3-small w3-text-grey w3-block"
              role="status"
            >
              {{ transferProblem }}
            </p>
            <button
              v-if="quote"
              type="button"
              class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
              :disabled="busy"
              @click="quote = null"
            >
              Edit Transfer
            </button>
            <button
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
              :disabled="busy || (action === 'Transfer' && !!transferProblem)"
            >
              {{
                busy
                  ? 'Please Wait…'
                  : action === 'Account'
                    ? 'Open Account'
                    : quote
                      ? 'Confirm Transfer'
                      : 'Review Transfer'
              }}
            </button>
          </form>
        </div>
      </dialog>
    </div>
  </AppShell>
</template>
