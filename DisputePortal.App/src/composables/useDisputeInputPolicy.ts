import { onBeforeUnmount, onMounted, ref } from 'vue'
import { api } from '../api'
import type { DisputeInputPolicy } from '../types'
import { useFeedback } from './useFeedback'

export function useDisputeInputPolicy() {
  const policy = ref<DisputeInputPolicy | null>(null)
  const loading = ref(true)
  const error = ref('')
  const abort = new AbortController()
  onMounted(async () => {
    try {
      policy.value = await api.get<DisputeInputPolicy>('/api/v1/dispute-input', abort.signal)
    } catch (cause) {
      if (!abort.signal.aborted)
        error.value =
          cause instanceof Error
            ? cause.message
            : 'Dispute options are temporarily unavailable. Please try again.'
    } finally {
      loading.value = false
    }
  })
  onBeforeUnmount(() => abort.abort())
  useFeedback(error)
  return { policy, loading, error }
}
