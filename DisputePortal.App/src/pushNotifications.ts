import { api } from './api'
export interface NotificationConfiguration {
  channel: string
  pushAvailable: boolean
  publicKey: string | null
  pushSubscribed?: boolean
}
interface PushScope {
  identity: string
  epoch: number
  signal: AbortSignal
}
let identity: string | null = null
let epoch = 0
let lifetime = new AbortController()
let operations: Promise<void> = Promise.resolve()
const optedOut = new Set<string>()
function preferenceKey(scope: PushScope) {
  return 'portal.push-off:' + scope.identity.split(':')[0]
}
function isOptedOut(scope: PushScope) {
  const key = preferenceKey(scope)
  try {
    return optedOut.has(key) || localStorage.getItem(key) === '1'
  } catch {
    return optedOut.has(key)
  }
}
function rememberOptOut(scope: PushScope, off: boolean) {
  const key = preferenceKey(scope)
  if (off) optedOut.add(key)
  else optedOut.delete(key)
  try {
    if (off) localStorage.setItem(key, '1')
    else localStorage.removeItem(key)
  } catch {
    /* The in-memory preference still applies for this page lifetime. */
  }
}

export function setPushIdentity(value: string | null): void {
  if (identity === value) return
  void clearApplicationBadge()
  epoch++
  lifetime.abort()
  lifetime = new AbortController()
  identity = value
}
function capture(): PushScope {
  if (!identity) throw new DOMException('The signed-in session changed.', 'AbortError')
  return { identity, epoch, signal: lifetime.signal }
}
function current(scope: PushScope): void {
  if (scope.signal.aborted || scope.epoch !== epoch || scope.identity !== identity)
    throw new DOMException('The signed-in session changed.', 'AbortError')
}
function serialize<T>(scope: PushScope, action: (context: PushScope) => Promise<T>): Promise<T> {
  const result = operations.then(async () => {
    current(scope)
    if (navigator.locks?.request)
      return await navigator.locks.request(
        'resolve-push-subscription',
        { mode: 'exclusive', signal: scope.signal },
        async () => {
          current(scope)
          return await action(scope)
        }
      )
    return await action(scope)
  })
  operations = result.then(
    () => undefined,
    () => undefined
  )
  return result
}
export function supportsPush(): boolean {
  return (
    typeof window !== 'undefined' &&
    window.isSecureContext &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
  )
}
export async function requestPushPermissionOnSignIn(): Promise<void> {
  if (!supportsPush() || Notification.permission !== 'default') return
  try {
    const permission = await Notification.requestPermission()
    if (permission === 'granted') await synchronizePush()
  } catch {
    return
  }
}

export function decodeApplicationKey(value: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/.test(value))
    throw new Error('Notification setup needs attention. Please try again later.')
  const binary = atob(
    value
      .replace(/-/g, '+')
      .replace(/_/g, '/')
      .padEnd(Math.ceil(value.length / 4) * 4, '=')
  )
  const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0))
  if (bytes.length !== 65 || bytes[0] !== 4)
    throw new Error('Notification setup needs attention. Please try again later.')
  return bytes
}
function usesApplicationKey(subscription: PushSubscription, publicKey: string): boolean {
  const existing = subscription.options.applicationServerKey
  if (!existing) return false
  const expected = decodeApplicationKey(publicKey),
    actual = new Uint8Array(existing)
  return (
    expected.length === actual.length && expected.every((byte, index) => byte === actual[index])
  )
}
async function readyRegistration(scope: PushScope): Promise<ServiceWorkerRegistration> {
  let registration = await navigator.serviceWorker.getRegistration('/')
  current(scope)
  if (!registration)
    registration = await navigator.serviceWorker.register(
      import.meta.env.DEV ? '/push-sw.js' : '/sw.js',
      { scope: '/' }
    )
  current(scope)
  if (registration.active) return registration
  let timeout: ReturnType<typeof setTimeout> | undefined
  try {
    const ready = await Promise.race([
      navigator.serviceWorker.ready,
      new Promise<never>((_, reject) => {
        timeout = setTimeout(
          () => reject(new Error('Notifications are still starting. Please try again.')),
          15_000
        )
      })
    ])
    current(scope)
    return ready
  } finally {
    clearTimeout(timeout)
  }
}
async function saveSubscription(subscription: PushSubscription, scope: PushScope): Promise<void> {
  current(scope)
  const value = subscription.toJSON()
  if (!value.endpoint || !value.keys?.p256dh || !value.keys.auth)
    throw new Error('Please enable notifications again.')
  await api.post(
    '/api/v1/notification-settings/push',
    { endpoint: value.endpoint, p256dh: value.keys.p256dh, auth: value.keys.auth },
    undefined,
    scope.signal
  )
  current(scope)
}
export async function existingPush(publicKey: string): Promise<boolean> {
  if (!supportsPush() || Notification.permission !== 'granted') return false
  const scope = capture()
  if (isOptedOut(scope)) return false
  const registration = await navigator.serviceWorker.getRegistration('/')
  current(scope)
  const subscription = await registration?.pushManager.getSubscription()
  current(scope)
  return !!subscription && usesApplicationKey(subscription, publicKey)
}
/** Rebind a consented browser endpoint only within its current authenticated lifetime. */
export async function synchronizePush(): Promise<void> {
  if (!supportsPush() || Notification.permission !== 'granted' || !identity) return
  const scope = capture()
  try {
    await serialize(scope, async (context) => {
      if (isOptedOut(context)) return
      const config = await api.get<NotificationConfiguration>(
        '/api/v1/notification-settings',
        context.signal
      )
      current(context)
      if (!config.pushAvailable || !config.publicKey) return
      let registration = await navigator.serviceWorker.getRegistration('/')
      current(context)
      let subscription = await registration?.pushManager.getSubscription()
      current(context)
      if (!subscription || !usesApplicationKey(subscription, config.publicKey)) {
        registration = await readyRegistration(context)
        if (subscription) {
          await subscription.unsubscribe()
          current(context)
        }
        subscription = await registration.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: decodeApplicationKey(config.publicKey).buffer as ArrayBuffer
        })
        current(context)
      }
      if (subscription && usesApplicationKey(subscription, config.publicKey))
        await saveSubscription(subscription, context)
    })
  } catch {
    /* Setup must not block sign-in, and a previous account cannot rebind an endpoint. */
  }
}
export async function enablePush(publicKey: string): Promise<void> {
  if (!supportsPush())
    throw new Error('Open Dispute in a secure supported browser to enable notifications.')
  const scope = capture(),
    key = decodeApplicationKey(publicKey)
  // Invoke from the button gesture before awaiting any queued operation (required by Safari).
  const permissionRequest = Notification.requestPermission()
  await serialize(scope, async (context) => {
    const permission = await permissionRequest
    current(context)
    if (permission !== 'granted')
      throw new Error('Allow notifications in your browser settings to continue.')
    const registration = await readyRegistration(context)
    let subscription = await registration.pushManager.getSubscription()
    current(context)
    if (subscription && !usesApplicationKey(subscription, publicKey)) {
      await subscription.unsubscribe()
      current(context)
      subscription = null
    }
    subscription ??= await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: key.buffer as ArrayBuffer
    })
    current(context)
    await saveSubscription(subscription, context)
    rememberOptOut(context, false)
  })
}
export async function disablePush(): Promise<void> {
  const scope = capture()
  await serialize(scope, async (context) => {
    await api.post(
      '/api/v1/notification-settings/push/disable',
      undefined,
      undefined,
      context.signal
    )
    current(context)
    rememberOptOut(context, true)
    // Server removal is authoritative. Retain the opt-out even if browser cleanup
    // cannot finish, so a subsequent sign-in never silently re-enables delivery.
    try {
      const registration = await navigator.serviceWorker.getRegistration('/')
      current(context)
      const subscription = await registration?.pushManager.getSubscription()
      current(context)
      if (subscription) await subscription.unsubscribe()
    } catch (cause) {
      current(context)
      if (cause instanceof DOMException && cause.name === 'AbortError') throw cause
    }
    current(context)
  })
}
interface AppBadgingNavigator {
  setAppBadge?: (count: number) => Promise<void>
  clearAppBadge?: () => Promise<void>
}
export async function clearApplicationBadge(): Promise<void> {
  try {
    await (navigator as unknown as AppBadgingNavigator).clearAppBadge?.()
  } catch {
    /* Badging is optional on unsupported browsers. */
  }
}
export async function updateApplicationBadge(count: number): Promise<void> {
  if (!identity || !Number.isSafeInteger(count) || count < 0) return
  try {
    const badges = navigator as AppBadgingNavigator
    if (count === 0) await badges.clearAppBadge?.()
    else await badges.setAppBadge?.(count)
  } catch {
    /* Permission and platform support never block the portal. */
  }
}
