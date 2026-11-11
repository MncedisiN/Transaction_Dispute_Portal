import { onUnmounted, ref, watch, type Ref } from 'vue'
import { api } from '../api'
import { clientOption } from '../utils/portalContent'
import type { SupportCaseOption } from '../operations'
import type { PageResult, Transaction } from '../types'

export function useEnquiryLookup(step: Readonly<Ref<string>>, error: Ref<string>) {
  const search = ref('')
  const lookupBusy = ref(false)
  const transactions = ref<Transaction[]>([])
  const cases = ref<SupportCaseOption[]>([])
  const page = ref(1)
  const total = ref(0)
  let request: AbortController | undefined
  let generation = 0
  let disposed = false

  function cancel() {
    generation++
    request?.abort()
    lookupBusy.value = false
  }

  watch(step, cancel, { flush: 'sync' })
  onUnmounted(() => {
    disposed = true
    cancel()
  })

  async function lookup(append = false) {
    if (append && lookupBusy.value) return
    const kind = step.value
    if (kind !== 'transaction' && kind !== 'case') return
    const nextPage = append ? page.value + 1 : 1
    const current = ++generation
    request?.abort()
    const controller = new AbortController()
    request = controller
    const timeout = setTimeout(() => controller.abort(), clientOption('requestTimeoutMilliseconds'))
    lookupBusy.value = true
    error.value = ''
    try {
      const query = new URLSearchParams({ page: String(nextPage) })
      if (kind === 'transaction') {
        query.set('sort', 'date_desc')
        query.set('search', search.value.trim())
        const result = await api.get<PageResult<Transaction>>(
          '/api/v1/transactions?' + query,
          controller.signal
        )
        if (disposed || controller.signal.aborted || current !== generation || step.value !== kind)
          return
        transactions.value = append
          ? [
              ...new Map(
                [...transactions.value, ...result.items].map((item) => [item.id, item])
              ).values()
            ]
          : result.items
        total.value = result.total
      } else {
        query.set('reference', search.value.trim())
        const result = await api.get<PageResult<SupportCaseOption>>(
          '/api/v1/support/related-cases?' + query,
          controller.signal
        )
        if (disposed || controller.signal.aborted || current !== generation || step.value !== kind)
          return
        cases.value = append
          ? [...new Map([...cases.value, ...result.items].map((item) => [item.id, item])).values()]
          : result.items
        total.value = result.total
      }
      // Failed pages remain retryable; only successful responses advance the cursor.
      page.value = nextPage
    } catch {
      if (!disposed && current === generation)
        error.value = 'The selection could not be loaded. Please retry.'
    } finally {
      clearTimeout(timeout)
      if (!disposed && current === generation) lookupBusy.value = false
    }
  }

  return {
    search,
    lookupBusy,
    transactions,
    cases,
    page,
    total,
    lookup,
    loadNextPage: () => lookup(true)
  }
}
