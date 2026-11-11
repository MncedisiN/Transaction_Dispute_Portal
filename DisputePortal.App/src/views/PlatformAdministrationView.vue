<script setup lang="ts">
import CustomerPortalSettings from '../components/CustomerPortalSettings.vue'
import DisclosurePanel from '../components/DisclosurePanel.vue'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useRefundReviews, type Review } from '../composables/useRefundReviews'
import { rawTimeZones, timeZonesNames, getCountryCode } from '@vvo/tzdb'
import {
  Search,
  RefreshCw,
  Globe,
  FileClock,
  CheckCircle,
  ArrowRight,
  ShieldCheck,
  Settings2
} from '@lucide/vue'
import AppShell from '../components/AppShell.vue'
import SettingsComparison from '../components/SettingsComparison.vue'
import SettingsVersionHistory from '../components/SettingsVersionHistory.vue'
import CountryPicker from '../components/CountryPicker.vue'
import { api } from '../api'
import { useFeedback } from '../composables/useFeedback'
import { loadPlatform, platform, money, localDate, type Settings, type Country } from '../platform'
interface Metadata {
  code: string
  name: string
  dialCode: string
  locale: string
  currency: string
  currencyName: string
  currencySymbol: string
  decimals: number
}
interface Report {
  journals: number
  balances: { currency: string; balance: number; accounts: number }[]
  payments: { currency: string; state: string; count: number; volume: number }[]
}
const tabs = [
  { key: 'reviews', label: 'Refund Reviews', icon: FileClock },
  { key: 'reports', label: 'Reconciliation', icon: CheckCircle },
  { key: 'countries', label: 'Country Settings', icon: Globe },
  { key: 'portal', label: 'Portal Settings', icon: Settings2 }
]
const tab = ref('reviews'),
  draft = ref<Settings | null>(null),
  selected = ref(''),
  catalog = ref<Metadata[]>([]),
  newCountry = ref(''),
  reason = ref(''),
  busy = ref(false),
  loading = ref(true),
  error = ref(''),
  notice = ref('')
const report = ref<Report | null>(null),
  reconciliation = ref<{ unbalancedJournals: number; positionDifferences: unknown[] } | null>(null)
const query = ref(''),
  reviewStatus = ref(''),
  currencyFilter = ref('')
const country = computed(() => draft.value?.countries.find((c) => c.code === selected.value)),
  rules = computed(
    () => draft.value?.financialRules.filter((r) => r.country === selected.value) ?? []
  ),
  availableCountries = computed(() =>
    catalog.value.filter(
      (c) => !draft.value?.countries.some((existing) => existing.code === c.code)
    )
  )
const zones = computed(() => timeZonesNames.filter((z) => getCountryCode(z) === selected.value))
const filteredCountries = computed(
  () =>
    draft.value?.countries.filter((c) =>
      (c.name + ' ' + c.code).toLowerCase().includes(query.value.trim().toLowerCase())
    ) ?? []
)
const route = useRoute()
const expandedReview = ref(typeof route.query.review === 'string' ? route.query.review : '')
const {
  schedules,
  reviews,
  reviewQueue,
  transactionSearch,
  found,
  original,
  searching,
  searched,
  reviewAt,
  reviewReason,
  reviewKind,
  searchTransactions,
  selectTransaction,
  schedule
} = useRefundReviews(query, reviewStatus, busy, error, notice, load)
watch(
  () => route.query.review,
  (value) => {
    expandedReview.value = typeof value === 'string' ? value : ''
  }
)
const reportBalances = computed(
  () =>
    report.value?.balances.filter(
      (b) => !currencyFilter.value || b.currency === currencyFilter.value
    ) ?? []
)
const reportPayments = computed(
  () =>
    report.value?.payments.filter(
      (p) =>
        (!currencyFilter.value || p.currency === currencyFilter.value) &&
        [p.currency, p.state].join(' ').toLowerCase().includes(query.value.trim().toLowerCase())
    ) ?? []
)
async function load() {
  loading.value = true
  error.value = ''
  try {
    const [value, metadata, reports, ledger, jobs, checks] = await Promise.all([
      loadPlatform(),
      api.get<Metadata[]>('/api/v1/platform/country-catalog'),
      api.get<Report>('/api/v1/platform/reports'),
      api.get<typeof reconciliation.value>('/api/v1/platform/reconciliation'),
      api.get<Review[]>('/api/v1/platform/reversals'),
      api.get<Review[]>('/api/v1/platform/debt/reviews')
    ])
    draft.value = JSON.parse(JSON.stringify(value))
    catalog.value = metadata
    report.value = reports
    reconciliation.value = ledger
    schedules.value = jobs
    reviews.value = checks
    if (!selected.value) selected.value = value.operatingCountry
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading.value = false
  }
}
onMounted(load)
watch(tab, () => {
  query.value = ''
  reviewStatus.value = ''
})
const settingsPreview = ref<Settings | null>(null),
  settingsRequest = ref(crypto.randomUUID())
watch(
  draft,
  () => {
    settingsPreview.value = null
  },
  { deep: true }
)
watch(reason, () => {
  settingsPreview.value = null
})
async function refreshSettingsToken() {
  if (!draft.value || busy.value) return
  busy.value = true
  try {
    const latest = await api.get<{ concurrencyToken: number }>(
      '/api/v1/platform/settings/history?page=1&pageSize=1'
    )
    if (draft.value.publication) draft.value.publication.concurrencyToken = latest.concurrencyToken
    settingsPreview.value = null
    settingsRequest.value = crypto.randomUUID()
    notice.value =
      'Latest history loaded. Your form input is retained; preview it again before publishing.'
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
async function save() {
  if (!draft.value || busy.value) return
  busy.value = true
  error.value = ''
  try {
    const publication = {
      expectedVersion: draft.value.version,
      concurrencyToken: draft.value.publication?.concurrencyToken,
      requestId: settingsRequest.value,
      settings: draft.value,
      reason: reason.value
    }
    if (!settingsPreview.value) {
      settingsPreview.value = await api.post<Settings>(
        '/api/v1/platform/settings/preview',
        publication
      )
      return
    }
    const value = await api.post<Settings>('/api/v1/platform/settings', publication)
    settingsPreview.value = null
    settingsRequest.value = crypto.randomUUID()
    platform.settings = value
    draft.value = JSON.parse(JSON.stringify(value))
    reason.value = ''
    notice.value = 'A new configuration version was published. Previous settings remain retained.'
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
function addCountry() {
  if (!draft.value) return
  const metadata = catalog.value.find((c) => c.code === newCountry.value),
    base = country.value ?? draft.value.countries[0]
  if (!metadata || !base) return
  if (draft.value.countries.some((c) => c.code === metadata.code)) {
    error.value = 'This country is already configured.'
    return
  }
  const zone =
    rawTimeZones.find((z) => z.countryCode === metadata.code)?.name ??
    timeZonesNames.find((z) => getCountryCode(z) === metadata.code) ??
    ''
  if (!zone) {
    error.value = 'Select a country with available time-zone metadata.'
    return
  }
  if (!draft.value.currencies.some((c) => c.code === metadata.currency))
    draft.value.currencies.push({
      code: metadata.currency,
      name: metadata.currencyName,
      symbol: metadata.currencySymbol,
      decimals: metadata.decimals
    })
  const added: Country = {
    ...JSON.parse(JSON.stringify(base)),
    code: metadata.code,
    name: metadata.name,
    flag: metadata.code,
    dialCode: metadata.dialCode,
    locale: metadata.locale,
    timeZone: zone,
    defaultCurrency: metadata.currency,
    currencies: [metadata.currency],
    regions: [],
    documents: base.documents.filter((d) => d.validator === 'pattern').map((d) => ({ ...d })),
    regionLabel: 'Region',
    postalLabel: 'Postal Code',
    active: false
  }
  if (!added.documents.length) {
    error.value = 'Configure a document policy before adding this country.'
    return
  }
  draft.value.countries.push(added)
  const template = draft.value.financialRules.find((r) => r.country === base.code)
  if (template)
    draft.value.financialRules.push({
      ...template,
      country: added.code,
      currency: added.defaultCurrency
    })
  selected.value = added.code
  newCountry.value = ''
  notice.value =
    'Country details were filled automatically. Review document requirements and financial limits before enabling it.'
}
useFeedback(error, undefined, notice)
</script>
<template>
  <AppShell>
    <div class="dispute-operations">
      <header class="operations-page-heading w3-bar w3-section">
        <div>
          <p class="eyebrow w3-small w3-wide w3-text-grey">DISPUTE ADMINISTRATION</p>
          <h1 class="w3-xxlarge w3-sans-serif w3-text-black">Dispute Operations</h1>
          <p>Manage refund reviews, reconciliation and country policies.</p>
        </div>
        <button
          type="button"
          class="icon-button w3-button w3-round-large"
          aria-label="Refresh Dispute Operations"
          :disabled="loading"
          @click="load"
        >
          <RefreshCw :size="19" />
        </button>
      </header>
      <div class="operations-summary w3-section">
        <div>
          <FileClock :size="21" />
          <span>
            Scheduled Reviews
            <strong>{{ schedules.length }}</strong>
          </span>
        </div>
        <div>
          <CheckCircle :size="21" />
          <span>
            Posted Journals
            <strong>{{ report?.journals ?? '—' }}</strong>
          </span>
        </div>
        <div>
          <ShieldCheck :size="21" />
          <span>
            Reconciliation
            <strong>
              {{
                reconciliation &&
                reconciliation.unbalancedJournals === 0 &&
                !reconciliation.positionDifferences.length
                  ? 'Balanced'
                  : 'Review Required'
              }}
            </strong>
          </span>
        </div>
      </div>
      <nav
        class="operations-tabs"
        aria-label="Dispute Operations"
      >
        <button
          v-for="item in tabs"
          :key="item.key"
          :class="{ 'w3-theme-l4 w3-text-black': (tab === item.key) === true }"
          class="w3-button w3-round-large"
          :aria-pressed="tab === item.key"
          @click="tab = item.key"
        >
          <component
            :is="item.icon"
            class="w3-margin-right"
            :size="18"
          />
          {{ item.label }}
        </button>
      </nav>
      <div
        v-if="tab !== 'portal'"
        class="operations-filter-bar w3-section"
      >
        <label class="search-field w3-block w3-text-dark-grey w3-medium">
          <Search :size="18" />
          <input
            v-model="query"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            type="search"
            :placeholder="
              tab === 'countries'
                ? 'Search Countries'
                : tab === 'reports'
                  ? 'Search Outcomes'
                  : 'Search Reviews'
            "
            aria-label="Search Dispute Operations"
          />
        </label>
        <select
          v-if="tab === 'reviews'"
          v-model="reviewStatus"
          class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
          aria-label="Filter Review Status"
        >
          <option value="">All Statuses</option>
          <option
            v-for="s in [...new Set([...schedules, ...reviews].map((r) => r.status))]"
            :key="s"
          >
            {{ s }}
          </option>
        </select>
        <select
          v-if="tab === 'reports'"
          v-model="currencyFilter"
          class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
          aria-label="Filter Currency"
        >
          <option value="">All Currencies</option>
          <option
            v-for="c in draft?.currencies"
            :key="c.code"
          >
            {{ c.code }}
          </option>
        </select>
      </div>
      <CustomerPortalSettings v-if="tab === 'portal'" />
      <div
        v-if="tab === 'reviews'"
        class="operations-content-grid w3-row-padding"
      >
        <div class="w3-col s12 m6 w3-margin-bottom">
          <section
            class="surface-panel operations-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <h2 class="w3-sans-serif w3-large">Schedule A Refund Review</h2>
            <p>
              Select a confirmed transaction. Customer, account and amount details are captured
              automatically.
            </p>
            <form
              class="operations-transaction-search w3-section"
              @submit.prevent="searchTransactions"
            >
              <label class="search-field w3-block w3-text-dark-grey w3-medium">
                <Search :size="18" />
                <input
                  v-model="transactionSearch"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                  type="search"
                  placeholder="Customer, Merchant Or Reference"
                  aria-label="Find Original Transaction"
                />
              </label>
              <button
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                :disabled="searching"
              >
                {{ searching ? 'Searching…' : 'Find Transaction' }}
              </button>
            </form>
            <p
              v-if="searched && !found.length && !searching"
              role="status"
              class="transaction-search-empty w3-panel w3-light-grey w3-round-large w3-padding"
            >
              No Matching Transactions Found. Try A Customer, Merchant Or Reference.
            </p>
            <button
              v-for="t in found"
              :key="t.id"
              type="button"
              class="transaction-picker-row w3-button w3-round-large"
              @click="selectTransaction(t)"
            >
              <span>
                <strong>{{ t.customerName }}</strong>
                <small class="w3-small">{{ t.merchantName }} · {{ t.reference }}</small>
              </span>
              <b>{{ money(t.amount, t.currency) }}</b>
              <ArrowRight :size="16" />
            </button>
            <form
              v-if="original"
              class="ops-form review-form"
              @submit.prevent="schedule"
            >
              <div class="selected-transaction w3-panel w3-light-grey w3-round-large w3-padding">
                <strong>{{ original.customerName }}</strong>
                <span>
                  {{ original.merchantName }} · {{ money(original.amount, original.currency) }}
                </span>
                <small class="w3-small">{{ original.reference }}</small>
                <button
                  type="button"
                  class="text-button w3-button w3-text-black w3-round-large"
                  @click="original = null"
                >
                  Change Transaction
                </button>
              </div>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Review Action
                <select
                  v-model="reviewKind"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                >
                  <option :value="original.type === 'Refund' ? 'Recall' : 'Refund'">
                    {{
                      original.type === 'Refund' ? 'Review Refund Recall' : 'Review Eligible Refund'
                    }}
                  </option>
                </select>
              </label>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Review Time
                <input
                  v-model="reviewAt"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  type="datetime-local"
                />
                <small class="w3-small">Leave Empty To Review Now.</small>
              </label>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Reason
                <textarea
                  v-model.trim="reviewReason"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                  minlength="10"
                  maxlength="500"
                  rows="3"
                />
              </label>
              <button
                class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
                :disabled="busy"
              >
                {{ busy ? 'Scheduling…' : 'Schedule Review' }}
              </button>
            </form>
            <div
              v-else-if="!found.length"
              class="operations-empty w3-center w3-padding-32"
            >
              <FileClock :size="27" />
              <p>Search for a transaction to begin.</p>
            </div>
          </section>
        </div>
        <div class="w3-col s12 m6 w3-margin-bottom">
          <section
            class="surface-panel operations-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <h2 class="w3-sans-serif w3-large">Review Queue</h2>
            <p>Scheduled requests and their latest eligibility decisions.</p>
            <article
              v-for="review in reviewQueue"
              :key="review.id"
              class="review-queue-item w3-panel w3-white w3-padding-large w3-round-xlarge w3-card w3-section"
            >
              <button
                type="button"
                class="review-queue-row review-queue-toggle w3-button w3-round-large w3-section"
                :aria-expanded="expandedReview === review.id"
                @click="expandedReview = expandedReview === review.id ? '' : review.id"
              >
                <span>
                  <strong>{{ review.kind || 'Eligibility' }} Review</strong>
                  <small
                    v-if="review.createdAt || review.dueAt"
                    class="w3-small"
                  >
                    {{ localDate(review.createdAt || review.dueAt || '') }}
                  </small>
                </span>
                <span
                  class="queue-status w3-tag w3-light-grey w3-round-large w3-small w3-padding-small"
                >
                  {{ review.status }}
                </span>
              </button>
              <div
                v-if="expandedReview === review.id"
                class="review-outcome-detail w3-section w3-small"
              >
                <p>{{ review.outcomeMessage }}</p>
                <dl class="review-metadata">
                  <div v-if="review.dueAt">
                    <dt>Review Time</dt>
                    <dd>{{ localDate(review.dueAt) }}</dd>
                  </div>
                  <div v-if="review.reason">
                    <dt>Reason</dt>
                    <dd>{{ review.reason }}</dd>
                  </div>
                </dl>
                <RouterLink
                  v-if="review.originalTransactionId"
                  :to="'/admin/transactions/' + review.originalTransactionId"
                  class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                >
                  Open Transaction
                  <ArrowRight :size="18" />
                </RouterLink>
              </div>
            </article>
            <div
              v-if="!reviewQueue.length"
              class="operations-empty w3-center w3-padding-32"
            >
              <CheckCircle :size="27" />
              <h3 class="w3-large w3-sans-serif">No Matching Reviews</h3>
              <p>New reviews will appear here with their latest status.</p>
            </div>
          </section>
        </div>
      </div>
      <section
        v-if="tab === 'reports'"
        class="surface-panel operations-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      >
        <div class="account-section-heading w3-bar w3-section">
          <div>
            <h2 class="w3-sans-serif w3-large">Dispute Reconciliation</h2>
            <p>
              {{ reconciliation?.unbalancedJournals ?? '—' }}
              Unbalanced Journals ·
              {{ reconciliation?.positionDifferences.length ?? '—' }}
              Balance Differences
            </p>
          </div>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            @click="load"
          >
            Refresh Reports
          </button>
        </div>
        <div class="reconciliation-balances">
          <article
            v-for="b in reportBalances"
            :key="b.currency"
          >
            <span>{{ b.currency }} · {{ b.accounts }} Accounts</span>
            <strong>{{ money(b.balance, b.currency) }}</strong>
          </article>
        </div>
        <h3 class="w3-large w3-sans-serif">Payment Outcomes</h3>
        <div
          v-for="p in reportPayments"
          :key="p.currency + p.state"
          class="review-queue-row w3-section"
        >
          <span>
            <strong>{{ p.state }}</strong>
            <small class="w3-small">{{ p.currency }} · {{ p.count }} Transactions</small>
          </span>
          <strong>{{ money(p.volume, p.currency) }}</strong>
        </div>
        <p
          v-if="!reportPayments.length"
          class="muted w3-text-grey"
        >
          No matching outcomes.
        </p>
      </section>
      <form
        v-if="tab === 'countries' && draft"
        class="operations-content-grid country-settings-grid w3-row-padding"
        @submit.prevent="save"
      >
        <div class="w3-col s12 m6 w3-margin-bottom">
          <section
            class="surface-panel operations-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <h2 class="w3-sans-serif w3-large">Country Policies</h2>
            <CountryPicker
              id="admin-country"
              v-model="selected"
              :countries="filteredCountries"
              label="Country"
            />
            <template v-if="country">
              <div
                class="country-metadata-summary w3-panel w3-light-grey w3-round-large w3-padding"
              >
                <strong>{{ country.name }}</strong>
                <span>{{ country.code }} · {{ country.dialCode }} · {{ country.locale }}</span>
              </div>
              <label class="checkbox-field w3-block w3-margin-top w3-text-dark-grey w3-medium">
                <input
                  v-model="country.active"
                  class="w3-check"
                  type="checkbox"
                />
                Available To Customers
              </label>
              <p class="country-default-currency w3-panel w3-light-grey w3-round-large w3-padding">
                <span>Account Currency</span>
                <strong>
                  {{ country.defaultCurrency }} ·
                  {{ draft.currencies.find((c) => c.code === country?.defaultCurrency)?.name }}
                </strong>
              </p>
              <DisclosurePanel class="operations-disclosure">
                <template #heading>Identity Requirements</template>
                <p>
                  Document options follow the selected issuing country. Customers choose from these
                  configured requirements.
                </p>
                <article
                  v-for="doc in country.documents"
                  :key="doc.key"
                  class="document-policy-card w3-white w3-border w3-round-large w3-container w3-padding-large"
                >
                  <strong>{{ doc.label }}</strong>
                  <small class="w3-small">
                    {{ country.name }} ·
                    {{
                      doc.validator === 'south-african-id'
                        ? 'National ID Validation'
                        : 'Configured Document Validation'
                    }}
                  </small>
                  <DisclosurePanel>
                    <template #heading>Advanced Validation</template>
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Number Format
                      <input
                        v-model.trim="doc.pattern"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        required
                        maxlength="200"
                      />
                    </label>
                  </DisclosurePanel>
                </article>
              </DisclosurePanel>
              <DisclosurePanel class="operations-disclosure">
                <template #heading>Regions And Address</template>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Region Label
                  <input
                    v-model.trim="country.regionLabel"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    required
                  />
                </label>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Postal Label
                  <input
                    v-model.trim="country.postalLabel"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    required
                  />
                </label>
                <label class="checkbox-field w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  <input
                    v-model="country.postalRequired"
                    class="w3-check"
                    type="checkbox"
                  />
                  Postal Code Required
                </label>
                <div
                  v-for="region in country.regions"
                  :key="region.code"
                  class="admin-row w3-section"
                >
                  <strong>{{ region.name }}</strong>
                  <small class="w3-small">{{ region.code }}</small>
                </div>
              </DisclosurePanel>
              <DisclosurePanel class="operations-disclosure">
                <template #heading>Limits And Fees</template>
                <fieldset
                  v-for="rule in rules"
                  :key="rule.currency"
                >
                  <legend>{{ rule.currency }}</legend>
                  <div class="field-pair w3-row-padding">
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Minimum
                      <input
                        v-model.number="rule.minimum"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        type="number"
                        step="any"
                        required
                      />
                    </label>
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Maximum
                      <input
                        v-model.number="rule.maximum"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        type="number"
                        step="any"
                        required
                      />
                    </label>
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Daily Limit
                      <input
                        v-model.number="rule.dailyLimit"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        type="number"
                        step="any"
                        required
                      />
                    </label>
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Fixed Fee
                      <input
                        v-model.number="rule.fixedFee"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        type="number"
                        step="any"
                        min="0"
                        required
                      />
                    </label>
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Percentage Fee
                      <input
                        v-model.number="rule.percentageFee"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        type="number"
                        step="any"
                        min="0"
                        max="100"
                        required
                      />
                    </label>
                  </div>
                </fieldset>
              </DisclosurePanel>
              <DisclosurePanel class="operations-disclosure">
                <template #heading>Regional Preferences</template>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Time Zone
                  <select
                    v-model="country.timeZone"
                    class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  >
                    <option :value="country.timeZone">{{ country.timeZone }}</option>
                    <option
                      v-for="zone in zones.filter((z) => z !== country?.timeZone)"
                      :key="zone"
                    >
                      {{ zone }}
                    </option>
                  </select>
                </label>
                <p>Country name, dialling code and locale are filled from the country catalog.</p>
              </DisclosurePanel>
            </template>
          </section>
        </div>
        <div class="w3-col s12 m6 w3-margin-bottom">
          <aside class="operations-settings-aside">
            <section
              class="surface-panel operations-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
            >
              <h2 class="w3-sans-serif w3-large">Add A Country</h2>
              <p>
                Select a country to fill its name, currency, dialling code, locale and time zone
                automatically.
              </p>
              <CountryPicker
                id="new-country"
                v-model="newCountry"
                :countries="availableCountries"
                label="New Country"
              />
              <button
                type="button"
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                :disabled="!newCountry"
                @click="addCountry"
              >
                Add Country
              </button>
              <small class="w3-small">
                New countries start disabled until their policies are reviewed.
              </small>
            </section>
            <section
              class="surface-panel operations-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
            >
              <h2 class="w3-sans-serif w3-large">Operating Country</h2>
              <CountryPicker
                id="operating-country"
                v-model="draft.operatingCountry"
                :countries="draft.countries.filter((c) => c.active)"
                label="Business Country"
              />
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Reason For Change
                <textarea
                  v-model.trim="reason"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                  minlength="10"
                  maxlength="500"
                  rows="3"
                />
              </label>
              <button
                class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
                :disabled="busy"
              >
                {{
                  busy
                    ? 'Saving…'
                    : settingsPreview
                      ? 'Publish New Configuration Version'
                      : 'Preview New Configuration Version'
                }}
              </button>
            </section>
          </aside>
        </div>
      </form>
    </div>
    <button
      v-if="tab === 'countries' && draft"
      class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      :disabled="busy"
      @click="refreshSettingsToken"
    >
      Refresh History And Keep My Input
    </button>
    <section
      v-if="settingsPreview"
      class="surface-panel ops-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    >
      <h3 class="w3-large w3-sans-serif">Before And After</h3>
      <SettingsComparison
        :before="platform.settings"
        :after="settingsPreview"
        changes-only
      />
      <p>
        Version {{ draft?.version }} → Version {{ settingsPreview.version }}. Country:
        {{ platform.settings?.operatingCountry }} → {{ settingsPreview.operatingCountry }}. Maximum
        Accounts: {{ platform.settings?.maximumAccounts }} → {{ settingsPreview.maximumAccounts }}.
        All settings are published as one complete version.
      </p>
    </section>
    <SettingsVersionHistory
      v-if="tab === 'countries'"
      @published="load"
    />
  </AppShell>
</template>
