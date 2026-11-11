const recentActivityMilliseconds = 5_000
const wheelListenerOptions = { passive: true, capture: true } as const
let lastActivityAt: number | null = null
let initialNavigationConsidered = false
let trackingUsers = 0
let wasForeground = false

function isForeground(): boolean {
  return (
    typeof document !== 'undefined' && document.visibilityState === 'visible' && document.hasFocus()
  )
}

function interaction(event: Event): void {
  if (!event.isTrusted || !isForeground()) return
  lastActivityAt = Date.now()
  wasForeground = true
}

function foregroundChanged(event: Event): void {
  if (!event.isTrusted) return
  if ((event.type === 'focus' || event.type === 'blur') && event.target !== window) return
  const foreground = event.type !== 'blur' && event.type !== 'pagehide' && isForeground()
  if (foreground && !wasForeground) lastActivityAt = Date.now()
  wasForeground = foreground
}

/** Track browser interaction without keeping an idle session alive on a timer. */
export function startActivityTracking(): () => void {
  if (typeof window === 'undefined' || typeof document === 'undefined') return () => undefined
  trackingUsers++
  if (trackingUsers === 1) {
    wasForeground = isForeground()
    if (!initialNavigationConsidered) {
      initialNavigationConsidered = true
      if (wasForeground) lastActivityAt = Date.now()
    }
    document.addEventListener('pointerdown', interaction, true)
    document.addEventListener('keydown', interaction, true)
    document.addEventListener('click', interaction, true)
    document.addEventListener('wheel', interaction, wheelListenerOptions)
    document.addEventListener('visibilitychange', foregroundChanged)
    window.addEventListener('focus', foregroundChanged)
    window.addEventListener('blur', foregroundChanged)
    window.addEventListener('pageshow', foregroundChanged)
    window.addEventListener('pagehide', foregroundChanged)
  }
  let stopped = false
  return () => {
    if (stopped) return
    stopped = true
    if (--trackingUsers !== 0) return
    document.removeEventListener('pointerdown', interaction, true)
    document.removeEventListener('keydown', interaction, true)
    document.removeEventListener('click', interaction, true)
    document.removeEventListener('wheel', interaction, wheelListenerOptions)
    document.removeEventListener('visibilitychange', foregroundChanged)
    window.removeEventListener('focus', foregroundChanged)
    window.removeEventListener('blur', foregroundChanged)
    window.removeEventListener('pageshow', foregroundChanged)
    window.removeEventListener('pagehide', foregroundChanged)
    wasForeground = false
  }
}

export function hasRecentUserActivity(): boolean {
  if (!trackingUsers || lastActivityAt === null || !isForeground()) return false
  const age = Date.now() - lastActivityAt
  return age >= 0 && age < recentActivityMilliseconds
}
