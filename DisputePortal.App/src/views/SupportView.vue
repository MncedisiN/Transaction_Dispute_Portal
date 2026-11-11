<script setup lang="ts">
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { computed, onUnmounted, ref, watch } from 'vue'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { useRoute, useRouter } from 'vue-router'
import { Plus, MessageCircle, ChevronRight, FileClock, Search } from '@lucide/vue'
import { api } from '../api'
import { useAuthStore } from '../stores/auth'
import { conversationTitle, caseDate, isClosedStatus } from '../utils/casePresentation'
import type { SupportThread, SupportCaseOption } from '../operations'
import type { DisputeDetail, Transaction } from '../types'
import { formatMoney } from '../utils/format'
import { previousDestination } from '../utils/backNavigation'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import SupportConversation from '../components/SupportConversation.vue'
import GuidedEnquiry from '../components/GuidedEnquiry.vue'
import CaseHistoryFilters from '../components/CaseHistoryFilters.vue'
import {
  emptyHistoryFilters,
  historyFilterQuery,
  type HistoryFilters
} from '../utils/historyFilters'
const auth = useAuthStore(),
  route = useRoute(),
  router = useRouter()
const queueTab = ref<'all' | 'unread' | 'assigned' | 'closed'>('all')
const threads = ref<SupportThread[]>([]),
  relatedCase = ref<SupportCaseOption | null>(null),
  linked = ref(''),
  error = ref(''),
  loading = ref(true),
  context = ref<DisputeDetail | null>(null)
const page = ref(1),
  pageSize = ref(0),
  total = ref(0),
  listLoading = ref(true)
const pages = computed(() =>
  Math.max(1, pageSize.value ? Math.ceil(total.value / pageSize.value) : 1)
)
const transactionContext = ref<Transaction | null>(null)
const linkedTransaction = computed(
  () =>
    active.value?.transactionId ||
    transactionContext.value?.id ||
    context.value?.dispute.transactionId
)
const selected = computed(() => (typeof route.query.thread === 'string' ? route.query.thread : ''))
const enquiryFor = computed(() =>
  typeof route.query.enquiryFor === 'string' ? route.query.enquiryFor : ''
)
const enquiryContext = ref<DisputeDetail | null>(null)
const enquiryDescription = computed(() =>
  enquiryContext.value
    ? [
        enquiryContext.value.dispute.reference,
        enquiryContext.value.merchantName || enquiryContext.value.dispute.transactionDescription,
        formatMoney(enquiryContext.value.dispute.amount, enquiryContext.value.dispute.currency),
        enquiryContext.value.dispute.status
      ].join(' · ')
    : undefined
)
const initialEnquiryCase = computed<SupportCaseOption | null>(() => {
  const detail = enquiryContext.value ?? context.value
  return detail
    ? {
        id: detail.dispute.id,
        reference: detail.dispute.reference,
        status: detail.dispute.status,
        transactionDescription: detail.dispute.transactionDescription,
        merchantName: detail.merchantName,
        createdAt: detail.dispute.createdAt
      }
    : relatedCase.value
})
const creating = computed(() => route.query.new === '1' || !!enquiryFor.value)
const selectedThread = ref<SupportThread | null>(null)
const search = ref(typeof route.query.search === 'string' ? route.query.search : ''),
  appliedSearch = ref(search.value)
const filters = ref<HistoryFilters>({
  ...emptyHistoryFilters(),
  status: typeof route.query.status === 'string' ? route.query.status : '',
  from: typeof route.query.from === 'string' ? route.query.from : '',
  to: typeof route.query.to === 'string' ? route.query.to : '',
  unreadOnly: route.query.unreadOnly === 'true'
})
async function applyFilters(value: HistoryFilters) {
  filters.value = value
  await searchThreads()
}
const active = computed(
  () =>
    threads.value.find((t) => t.id === selected.value) ||
    (selectedThread.value?.id === selected.value ? selectedThread.value : undefined)
)
async function clearSearch() {
  search.value = ''
  await searchThreads()
}
async function searchThreads() {
  appliedSearch.value = search.value.trim()
  await router.replace({
    path: '/support',
    query: { ...historyFilterQuery(filters.value), search: appliedSearch.value || undefined }
  })
  await load(1)
}
const returnTo = computed(() =>
  previousDestination(route.query.returnTo, route.fullPath, '/support')
)
const originQuery = computed(() => ({
  ...historyFilterQuery(filters.value),
  returnTo: returnTo.value || undefined,
  search: appliedSearch.value || undefined
}))
const backTarget = computed(
  () =>
    returnTo.value ||
    router.resolve({
      path: '/support',
      query: { ...historyFilterQuery(filters.value), search: appliedSearch.value || undefined }
    }).fullPath
)
let disposed = false,
  request: AbortController | undefined,
  generation = 0
async function load(targetPage = page.value) {
  const current = ++generation
  listLoading.value = true
  request?.abort()
  request = new AbortController()
  try {
    const result = await api.collectionPage<SupportThread>(
      '/api/v1/support',
      {
        ...historyFilterQuery(filters.value),
        status: queueTab.value === 'closed' ? 'closed' : filters.value.status,
        unreadOnly:
          queueTab.value === 'unread' ? 'true' : filters.value.unreadOnly ? 'true' : undefined,
        assignedOnly: queueTab.value === 'assigned' ? 'true' : undefined,
        ownOnly: !auth.isStaffWorkspace ? 'true' : undefined,
        search: appliedSearch.value,
        page: targetPage
      },
      request.signal
    )
    if (disposed || current !== generation) return
    const lastPage = Math.max(1, Math.ceil(result.total / result.pageSize))
    if (result.page > lastPage) {
      await load(lastPage)
      return
    }
    if (selected.value && !result.items.some((t) => t.id === selected.value)) {
      const thread = await api.get<SupportThread>(
        '/api/v1/support/' + selected.value,
        request.signal
      )
      if (!disposed && current === generation) selectedThread.value = thread
    }
    if (!disposed && current === generation) {
      threads.value = result.items
      page.value = result.page
      pageSize.value = result.pageSize
      total.value = result.total
      error.value = ''
    }
  } catch {
    if (!disposed && current === generation)
      error.value = 'Conversations are unavailable. Please try again.'
  } finally {
    if (!disposed && current === generation) listLoading.value = false
  }
}
async function changePage(next: number) {
  if (!listLoading.value && next >= 1 && next <= pages.value) await load(next)
}
async function open(thread: SupportThread, replace = false) {
  error.value = ''
  await router[replace ? 'replace' : 'push']({
    path: '/support',
    query: { ...originQuery.value, thread: thread.id }
  })
}
async function create() {
  linked.value = ''
  relatedCase.value = null
  context.value = null
  transactionContext.value = null
  enquiryContext.value = null
  await router.push({ path: '/support', query: { ...originQuery.value, new: '1' } })
}
async function sent(id: string) {
  if (selected.value === id) {
    await load()
    return
  }
  await router.replace({ path: '/support', query: { ...originQuery.value, thread: id } })
  await load()
}
async function openCase(id: string) {
  try {
    const found = (await api.get<SupportThread[]>('/api/v1/support?disputeId=' + id))
      .filter((t) => t.disputeId === id)
      .sort((a, b) => Number(a.status === 'Closed') - Number(b.status === 'Closed'))[0]
    if (disposed) return
    if (found) {
      selectedThread.value = found
      await open(found, true)
    } else {
      context.value = await api.get<DisputeDetail>(
        (auth.isStaffWorkspace ? '/api/v1/admin/disputes/' : '/api/v1/disputes/') + id
      )
      linked.value = id
      await router.replace({
        path: '/support',
        query: { ...originQuery.value, new: '1', dispute: id }
      })
    }
  } catch {
    error.value = 'This case conversation is unavailable.'
  }
}
async function openEnquiry(id: string) {
  linked.value = ''
  relatedCase.value = null
  context.value = null
  transactionContext.value = null
  enquiryContext.value = null
  try {
    const detail = await api.get<DisputeDetail>('/api/v1/disputes/' + encodeURIComponent(id))
    if (disposed) return
    if (!isClosedStatus(detail.dispute.status)) {
      await openCase(id)
      return
    }
    enquiryContext.value = detail
  } catch {
    error.value = 'This enquiry cannot be opened. Return to your dispute and try again.'
  }
}
async function openTransaction(id: string) {
  try {
    transactionContext.value = await api.get<Transaction>(
      '/api/v1/transactions/' + encodeURIComponent(id)
    )
    if (disposed) return
    if (transactionContext.value.disputeId) {
      await openCase(transactionContext.value.disputeId)
      return
    }
    const found = (
      await api.get<SupportThread[]>('/api/v1/support?transactionId=' + encodeURIComponent(id))
    ).find((item) => item.transactionId === id)
    if (disposed) return
    if (found) {
      selectedThread.value = found
      await open(found, true)
    } else
      await router.replace({
        path: '/support',
        query: { ...originQuery.value, new: '1', transaction: id }
      })
  } catch {
    error.value = 'This transaction conversation is unavailable.'
  }
}
useLiveRefresh(async () => {
  await load()
  if (loading.value) {
    if (enquiryFor.value) await openEnquiry(enquiryFor.value)
    else if (typeof route.query.transaction === 'string')
      await openTransaction(route.query.transaction)
    else if (typeof route.query.dispute === 'string') await openCase(route.query.dispute)
    loading.value = false
  } else if (linked.value && (context.value || relatedCase.value)) {
    const id = linked.value
    const updated = await api.get<DisputeDetail>(
      (auth.isStaffWorkspace ? '/api/v1/admin/disputes/' : '/api/v1/disputes/') + id
    )
    if (!disposed && linked.value === id) {
      if (context.value) context.value = updated
      else if (relatedCase.value)
        relatedCase.value = { ...relatedCase.value, status: updated.dispute.status }
    }
  }
}, ['support', 'disputes'])
watch(
  () => auth.workspace,
  async () => {
    selectedThread.value = null
    await router.replace({ path: '/support' })
    await load(1)
  }
)
watch(queueTab, () => {
  void load(1)
})
watch(selected, () => {
  if (selected.value && !active.value) void load()
})
watch(enquiryFor, (value) => {
  if (value && !loading.value) void openEnquiry(value)
})
onUnmounted(() => {
  disposed = true
  generation++
  request?.abort()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <AppShell :conversation="Boolean(selected || creating)">
    <div
      class="messages-page"
      :class="{ 'showing-conversation': selected || creating }"
    >
      <PageHeading
        v-if="!selected && !creating"
        :title="auth.isStaffWorkspace ? 'Message Queue' : 'Messages'"
      >
        <button
          v-if="!auth.isStaffWorkspace"
          class="text-button heading-inline-action w3-button w3-theme-d5 w3-round-xlarge w3-padding w3-block"
          @click="create"
        >
          <Plus
            class="w3-margin-right"
            :size="16"
          />
          New Enquiry
        </button>
      </PageHeading>
      <div
        v-if="!selected && !creating"
        class="message-queue-summary w3-bar w3-section w3-white w3-round-xlarge w3-margin-bottom w3-padding-small"
        aria-label="Message Queue Filters"
      >
        <button
          :class="{ 'w3-theme-l4 w3-text-black': (queueTab === 'closed') === true }"
          class="w3-button w3-round-large w3-padding w3-small"
          :aria-pressed="queueTab === 'closed'"
          @click="queueTab = 'closed'"
        >
          Closed
        </button>
        <button
          :class="{ 'w3-theme-l4 w3-text-black': (queueTab === 'all') === true }"
          class="w3-button w3-round-large w3-padding w3-small"
          :aria-pressed="queueTab === 'all'"
          @click="queueTab = 'all'"
        >
          All Conversations
        </button>
        <button
          :class="{ 'w3-theme-l4 w3-text-black': (queueTab === 'unread') === true }"
          class="w3-button w3-round-large w3-padding w3-small"
          :aria-pressed="queueTab === 'unread'"
          @click="queueTab = 'unread'"
        >
          Unread
        </button>
        <button
          v-if="auth.isStaffWorkspace"
          :class="{ 'w3-theme-l4 w3-text-black': (queueTab === 'assigned') === true }"
          class="w3-button w3-round-large w3-padding w3-small"
          :aria-pressed="queueTab === 'assigned'"
          @click="queueTab = 'assigned'"
        >
          Case Conversations
        </button>
      </div>
      <p
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
        <button
          class="text-button w3-button w3-text-black w3-round-large"
          @click="load()"
        >
          Retry
        </button>
      </p>
      <div
        class="support-workspace w3-row-padding"
        :class="{ 'conversation-open': selected || creating }"
      >
        <aside
          v-if="!selected && !creating"
          class="surface-panel inbox-panel w3-container w3-margin-bottom w3-col l5 m12 s12 w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
        >
          <form
            v-if="auth.isStaffWorkspace || reference.customerSearchEnabled"
            class="support-search filter-bar compact-transaction-toolbar w3-section w3-flex"
            role="search"
            @submit.prevent="searchThreads"
          >
            <div class="w3-block w3-rest">
              <label class="search-field w3-block w3-text-dark-grey w3-medium">
                <input
                  v-model="search"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  type="search"
                  maxlength="100"
                  aria-label="Search Conversations"
                  placeholder="Search Messages"
                  @search="!search &amp;&amp; clearSearch()"
                />
              </label>
            </div>
            <div class="w3-padding-small">
              <CaseHistoryFilters
                :value="filters"
                kind="messages"
                @apply="applyFilters"
              />
            </div>
            <div class="w3-padding-small">
              <button
                class="icon-button search-submit w3-button w3-round-large w3-block w3-border w3-white w3-large w3-padding-small"
                type="submit"
                aria-label="Find Conversations"
              >
                <Search :size="18" />
              </button>
            </div>
          </form>
          <div class="inbox-heading w3-cell-row">
            <h2 class="w3-cell w3-cell-middle w3-sans-serif w3-large">Conversations</h2>
            <span class="muted w3-text-grey w3-cell w3-cell-middle w3-right-align">
              {{ total }}
            </span>
          </div>
          <p
            v-if="loading || listLoading"
            class="muted w3-text-grey"
          >
            Loading messages…
          </p>
          <button
            v-for="thread in threads"
            :key="thread.id"
            class="inbox-thread w3-button w3-white w3-block w3-left-align w3-border-bottom w3-padding-16 w3-round-large"
            :class="{ 'thread-selected w3-pale-blue': selected === thread.id }"
            @click="open(thread)"
          >
            <span class="thread-avatar w3-margin-right w3-text-blue-grey w3-left w3-padding-small">
              <FileClock
                v-if="thread.disputeId"
                :size="19"
              />
              <MessageCircle
                v-else
                :size="19"
              />
            </span>
            <div class="thread-copy w3-rest w3-padding-small">
              <strong class="w3-block">{{ conversationTitle(thread) }}</strong>
              <span class="w3-block">
                {{
                  thread.concernLabel ||
                  thread.reference ||
                  (thread.relatedDisputeId
                    ? 'Closed-Case Enquiry'
                    : thread.transactionId
                      ? 'Transaction Enquiry'
                      : 'General Support')
                }}
              </span>
              <small class="w3-small w3-block w3-text-grey">
                Last Message ·
                <time :datetime="thread.lastMessageAt || thread.createdAt">
                  {{ caseDate(thread.lastMessageAt || thread.createdAt) }}
                </time>
              </small>
            </div>
            <span
              v-if="thread.unreadCount"
              class="unread-count w3-badge w3-theme-d5 w3-right"
              :aria-label="thread.unreadCount + ' unread messages'"
            >
              {{ thread.unreadCount }}
            </span>
            <ChevronRight
              v-else
              class="w3-right"
              :size="15"
            />
          </button>
          <div
            v-if="!loading && !listLoading && !threads.length"
            class="empty-state w3-container w3-center w3-padding-32"
          >
            <MessageCircle :size="26" />
            <h3 class="w3-large w3-sans-serif">
              {{ appliedSearch ? 'No Matching Conversations' : 'No Messages Yet' }}
            </h3>
            <p>
              {{
                appliedSearch
                  ? 'Try a case reference, merchant or subject.'
                  : 'Message support from a case, or start a general enquiry.'
              }}
            </p>
          </div>
          <nav
            v-if="pages > 1"
            class="pagination w3-container w3-section w3-center"
            aria-label="Conversation Pages"
          >
            <span>Page {{ page }} of {{ pages }}</span>
            <div>
              <button
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                :disabled="page <= 1 || listLoading"
                @click="changePage(page - 1)"
              >
                Previous
              </button>
              <button
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                :disabled="page >= pages || listLoading"
                @click="changePage(page + 1)"
              >
                Next
              </button>
            </div>
          </nav>
        </aside>
        <div
          class="conversation-pane w3-col s12 m12 w3-content"
          :class="selected || creating ? 'l12' : 'l7'"
        >
          <GuidedEnquiry
            v-if="creating && !auth.isStaffWorkspace && (!enquiryFor || enquiryContext)"
            :key="'guided-' + (enquiryFor || linked || linkedTransaction || 'new')"
            :back-to="backTarget"
            :initial-case="initialEnquiryCase"
            :initial-transaction="transactionContext"
            @sent="sent"
          />
          <SupportConversation
            v-else-if="
              (selected && active) ||
              (creating &&
                auth.isStaffWorkspace &&
                (linked || linkedTransaction) &&
                (!enquiryFor || enquiryContext))
            "
            :key="
              selected ||
              (enquiryContext
                ? 'enquiry-' + enquiryContext.dispute.id
                : linked || linkedTransaction || 'new')
            "
            fullscreen
            :back-to="backTarget"
            :thread-id="selected || undefined"
            :dispute-id="selected ? active?.disputeId || undefined : linked || undefined"
            :related-dispute-id="
              active?.relatedDisputeId || (creating ? enquiryContext?.dispute.id : undefined)
            "
            :related-case-context="
              active?.relatedCaseContext || (creating ? enquiryDescription : undefined)
            "
            :subject="
              active?.subject ||
              (enquiryContext ? 'Enquiry about ' + enquiryContext.dispute.reference : undefined)
            "
            :transaction-id="
              selected
                ? active?.transactionId || undefined
                : enquiryContext
                  ? undefined
                  : linkedTransaction || undefined
            "
            :amount="active?.amount ?? transactionContext?.amount ?? context?.dispute.amount"
            :currency="
              active?.currency || transactionContext?.currency || context?.dispute.currency
            "
            :account-number="
              active?.accountNumber || transactionContext?.accountNumber || undefined
            "
            :transaction-at="
              active?.transactionAt || transactionContext?.transactionAt || context?.transactionAt
            "
            :title="
              active
                ? conversationTitle(active)
                : transactionContext?.merchantName ||
                  context?.merchantName ||
                  relatedCase?.merchantName
            "
            :reference="active?.reference || context?.dispute.reference || relatedCase?.reference"
            :status="
              (selected ? active?.disputeId : linked)
                ? active?.caseStatus || context?.dispute.status || relatedCase?.status
                : undefined
            "
            :case-date="
              active?.caseCreatedAt || context?.dispute.createdAt || relatedCase?.createdAt
            "
            :last-read-sequence="active?.lastReadSequence"
            @changed="
              async (lost) => {
                if (lost) await router.replace({ path: '/support' })
                await load()
              }
            "
            @new="create"
            @sent="sent"
            @read="load()"
          />
          <section
            v-else
            class="surface-panel empty-state conversation-empty w3-container w3-margin-bottom w3-center w3-padding-32 w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
          >
            <MessageCircle :size="34" />
            <h2 class="w3-sans-serif w3-large">Your Conversations, Together</h2>
            <p>Select a conversation to view its case context, messages and latest updates.</p>
            <RouterLink
              :to="auth.isStaffWorkspace ? '/admin/disputes' : '/disputes'"
              class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            >
              {{ auth.isStaffWorkspace ? 'Open Case Queue' : 'View Your Disputes' }}
            </RouterLink>
          </section>
        </div>
      </div>
    </div>
  </AppShell>
</template>
