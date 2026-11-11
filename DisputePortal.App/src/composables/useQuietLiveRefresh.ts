import { onUnmounted } from 'vue'
import { useLiveRefresh } from './useLiveRefresh'
import type { LiveTopic } from '../liveUpdates'

/** Keep an existing mount/filter load and queue live changes without replacing its UI. */
export function useQuietLiveRefresh(
  refresh: () => Promise<void>,
  busy: () => boolean,
  topics: readonly LiveTopic[]
): () => void {
  let first = true,
    pending = false,
    running = false,
    disposed = false
  async function run(): Promise<void> {
    if (disposed) return
    if (busy() || running) {
      pending = true
      return
    }
    pending = false
    running = true
    try {
      await refresh()
    } catch {
      /* Keep the current authorized snapshot. */
    } finally {
      running = false
      ready()
    }
  }
  function ready(): void {
    if (!disposed && pending && !busy() && !running) void run()
  }
  useLiveRefresh(() => {
    if (first) {
      first = false
      if (busy()) return
    }
    return run()
  }, topics)
  onUnmounted(() => {
    disposed = true
    pending = false
  })
  return ready
}
