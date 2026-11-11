import { createApp, watch } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import './vendor/w3.css'
import './vendor/w3-theme-blue.css'
import './styles/portal.css'
import { startActivityTracking } from './userActivity'
import { onUnauthorized } from './api'
import { useAuthStore } from './stores/auth'
import { registerSW } from 'virtual:pwa-register'
import { setPushIdentity, synchronizePush } from './pushNotifications'
import { disposeLiveUpdates, notifyPushUpdate, resumeLiveUpdates, setLiveUser } from './liveUpdates'

import { trackVisibleViewport } from './utils/viewport'
const stopViewportTracking = trackVisibleViewport()
const stopActivityTracking = startActivityTracking()
const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(router)
app.mount('#app')

const auth = useAuthStore(pinia)
const stopLiveWatch = watch(
  [() => auth.user?.id ?? null, () => auth.generation] as const,
  ([user, version]) => {
    setPushIdentity(user ? user + ':' + version : null)
    void setLiveUser(user ? user + ':' + version : null)
    if (user) void synchronizePush()
  },
  { immediate: true, flush: 'sync' }
)
function reconnectUpdates() {
  resumeLiveUpdates()
  if (auth.user) void synchronizePush()
}
window.addEventListener('online', reconnectUpdates)
function clearExpiredSession(force = false) {
  const current = router.currentRoute.value
  if (!force && !auth.user && !current.meta.requiresAuth) return
  auth.clearSession()
  if (current.meta.requiresAuth)
    void router.replace({ name: 'sign-in', query: { redirect: current.fullPath } })
}
onUnauthorized(clearExpiredSession)
const sessionChannel =
  typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel('portal-session')
async function refreshSharedSession() {
  const previous = router.currentRoute.value
  const previousUser = auth.user?.id
  auth.clearSession()
  const expected = auth.generation
  if (previous.meta.requiresAuth)
    await router.replace({ name: 'sign-in', query: { redirect: previous.fullPath } })
  if (auth.generation !== expected) return
  auth.initialized = false
  await auth.initialize()
  if (auth.generation !== expected || !auth.user) return
  const destination =
    auth.user.id === previousUser && previous.meta.requiresAuth
      ? previous.fullPath
      : auth.isStaffWorkspace
        ? '/admin/disputes'
        : '/'
  await router.replace(destination)
}
if (sessionChannel)
  sessionChannel.onmessage = (event) => {
    if (event.data === 'signed-out') clearExpiredSession(true)
    else if (event.data === 'session-changed') void refreshSharedSession()
  }
const publishLogout = () => sessionChannel?.postMessage('signed-out')
const publishSignIn = () => sessionChannel?.postMessage('session-changed')
const checkVisibleSession = () => {
  if (document.visibilityState === 'visible')
    void auth.refreshSession().then(() => {
      if (auth.user) void synchronizePush()
    })
}
const sessionEnded = () => clearExpiredSession(true)
const pushMessage = (event: MessageEvent) => {
  if (event.data?.type === 'resolve:push' && auth.user) notifyPushUpdate()
}
window.addEventListener('portal:session-check', checkVisibleSession)
window.addEventListener('portal:session-ended', sessionEnded)
navigator.serviceWorker?.addEventListener('message', pushMessage)
window.addEventListener('portal:signed-out', publishLogout)
window.addEventListener('portal:signed-in', publishSignIn)
document.addEventListener('visibilitychange', checkVisibleSession)
window.addEventListener('pageshow', checkVisibleSession)
if (import.meta.hot)
  import.meta.hot.dispose(() => {
    stopViewportTracking()
    stopActivityTracking()
    window.removeEventListener('portal:session-check', checkVisibleSession)
    window.removeEventListener('portal:session-ended', sessionEnded)
    navigator.serviceWorker?.removeEventListener('message', pushMessage)
    setPushIdentity(null)
    stopLiveWatch()
    void disposeLiveUpdates()
    window.removeEventListener('online', reconnectUpdates)
    onUnauthorized(undefined)
    sessionChannel?.close()
    window.removeEventListener('portal:signed-out', publishLogout)
    window.removeEventListener('portal:signed-in', publishSignIn)
    document.removeEventListener('visibilitychange', checkVisibleSession)
    window.removeEventListener('pageshow', checkVisibleSession)
  })

registerSW({ immediate: true })
