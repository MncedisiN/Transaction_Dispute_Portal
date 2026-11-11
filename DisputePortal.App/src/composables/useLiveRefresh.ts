import { onMounted, onUnmounted } from 'vue'
import { onLiveChange, resumeLiveUpdates, type LiveTopic } from '../liveUpdates'

/** Refreshes an authorized server snapshot after a live event, never on an interval. */
export function useLiveRefresh(
  action: (signal: AbortSignal) => Promise<void> | void,
  topics: readonly LiveTopic[] = ['disputes']
): void {
  let disposed = false
  let running = false
  let queued = false
  let request: AbortController | undefined
  let unsubscribe: (() => void) | undefined
  const available = () => document.visibilityState !== 'hidden' && navigator.onLine
  async function refresh(): Promise<void> {
    if (disposed || !available()) {
      queued = true
      return
    }
    if (running) {
      queued = true
      return
    }
    queued = false
    running = true
    request = new AbortController()
    try {
      await action(request.signal)
    } catch {
      /* Keep the last authorized snapshot until the next event or explicit refresh. */
    } finally {
      running = false
      if (!disposed && queued && available()) void refresh()
    }
  }
  function visible(): void {
    if (document.visibilityState !== 'hidden') {
      resumeLiveUpdates()
      void refresh()
    } else request?.abort()
  }
  onMounted(() => {
    unsubscribe = onLiveChange((change) => {
      if (change.topics.some((topic) => topics.includes(topic))) void refresh()
    })
    document.addEventListener('visibilitychange', visible)
    window.addEventListener('online', visible)
    window.addEventListener('pageshow', visible)
    void refresh()
  })
  onUnmounted(() => {
    disposed = true
    queued = false
    request?.abort()
    unsubscribe?.()
    document.removeEventListener('visibilitychange', visible)
    window.removeEventListener('online', visible)
    window.removeEventListener('pageshow', visible)
  })
}
