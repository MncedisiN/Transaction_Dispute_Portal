import { computed, onUnmounted, ref, type Ref } from 'vue'
import { api } from '../api'
import { clientOption } from '../utils/portalContent'
import type { StaffTransaction } from '../staffTransactions'
import { useLiveRefresh } from './useLiveRefresh'

export interface Review {
  id: string
  kind?: string
  status: string
  dueAt?: string
  decisionCode?: string
  originalTransactionId?: string
  createdAt?: string
  reason?: string
  outcomeMessage?: string
}

export function useRefundReviews(
  query: Ref<string>,
  reviewStatus: Ref<string>,
  busy: Ref<boolean>,
  error: Ref<string>,
  notice: Ref<string>,
  reload: () => Promise<void>
) {
  const schedules = ref<Review[]>([])
  const reviews = ref<Review[]>([])
  const transactionSearch = ref(''),
    found = ref<StaffTransaction[]>([]),
    original = ref<StaffTransaction | null>(null),
    searching = ref(false),
    searched = ref(false),
    reviewAt = ref(''),
    reviewReason = ref(''),
    reviewKind = ref('Refund')
  const reviewQueue = computed(() => {
    const combined = new Map(reviews.value.map((review) => [review.id, review]))
    for (const schedule of schedules.value) {
      const check = combined.get(schedule.id)
      combined.set(schedule.id, {
        ...check,
        ...schedule,
        decisionCode: schedule.decisionCode || check?.decisionCode,
        outcomeMessage:
          ['Scheduled', 'Dispatched'].includes(schedule.status) && check
            ? check.outcomeMessage
            : schedule.outcomeMessage
      })
    }
    return [...combined.values()]
      .filter((review) => !reviewStatus.value || review.status === reviewStatus.value)
      .filter((review) =>
        [
          review.kind,
          review.id,
          review.originalTransactionId,
          review.status,
          review.reason,
          review.outcomeMessage
        ]
          .join(' ')
          .toLowerCase()
          .includes(query.value.trim().toLowerCase())
      )
      .sort(
        (a, b) =>
          Date.parse(b.createdAt || b.dueAt || '') - Date.parse(a.createdAt || a.dueAt || '') ||
          a.id.localeCompare(b.id)
      )
  })
  async function refreshReviews(signal: AbortSignal) {
    const [jobs, checks] = await Promise.all([
      api.get<Review[]>('/api/v1/platform/reversals', signal),
      api.get<Review[]>('/api/v1/platform/debt/reviews', signal)
    ])
    schedules.value = jobs
    reviews.value = checks
  }
  useLiveRefresh(refreshReviews, ['banking', 'notifications'])
  let searchRequest: AbortController | undefined
  onUnmounted(() => searchRequest?.abort())
  async function searchTransactions() {
    if (searching.value) return
    searching.value = true
    searched.value = false
    found.value = []
    original.value = null
    searchRequest?.abort()
    const controller = new AbortController()
    searchRequest = controller
    const timer = setTimeout(() => controller.abort(), clientOption('requestTimeoutMilliseconds'))
    error.value = ''
    try {
      const page = await api.page<StaffTransaction>(
        '/api/v1/admin/transactions',
        {
          page: 1,
          pageSize: clientOption('reviewTransactionPageSize'),
          search: transactionSearch.value,
          status: 'Posted'
        },
        controller.signal
      )
      found.value = page.items.filter((t) => t.direction === 'Debit' || t.type === 'Refund')
      searched.value = true
    } catch (e) {
      error.value = controller.signal.aborted
        ? 'Transaction search timed out. Please try again.'
        : (e as Error).message
    } finally {
      clearTimeout(timer)
      searching.value = false
    }
  }
  function selectTransaction(t: StaffTransaction) {
    original.value = t
    reviewKind.value = t.type === 'Refund' ? 'Recall' : 'Refund'
    found.value = []
  }
  async function schedule() {
    if (!original.value || busy.value) return
    busy.value = true
    error.value = ''
    try {
      await api.post('/api/v1/platform/reversals', {
        requestId: crypto.randomUUID(),
        originalTransactionId: original.value.id,
        kind: reviewKind.value,
        dueAt: reviewAt.value ? new Date(reviewAt.value).toISOString() : new Date().toISOString(),
        reason: reviewReason.value
      })
      notice.value = 'The review is scheduled. Eligibility is checked before any financial outcome.'
      original.value = null
      reviewReason.value = ''
      await reload()
    } catch (e) {
      error.value = (e as Error).message
    } finally {
      busy.value = false
    }
  }

  return {
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
  }
}
