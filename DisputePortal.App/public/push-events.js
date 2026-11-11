function notificationTarget(value) {
  try {
    const url = new URL(typeof value === 'string' ? value : '/notifications', self.location.origin)
    return url.origin === self.location.origin &&
      /^\/(notifications(?:\/|$)|disputes(?:\/|$)|support(?:\/|$)|admin\/disputes(?:\/|$)|accounts(?:\/|$)|transactions(?:\/|$))/.test(
        url.pathname
      )
      ? url.pathname + url.search
      : '/notifications'
  } catch {
    return '/notifications'
  }
}
self.addEventListener('push', (event) => {
  let payload = {}
  try {
    const value = event.data ? event.data.json() : {}
    payload = value && typeof value === 'object' && !Array.isArray(value) ? value : {}
  } catch {
    /* Keep lock-screen content private. */
  }
  const title = typeof payload.title === 'string' ? payload.title.slice(0, 80) : 'Dispute Portal'
  const tag = typeof payload.tag === 'string' ? payload.tag.slice(0, 80) : 'dispute-update'
  const options = {
    body:
      typeof payload.body === 'string' ? payload.body.slice(0, 220) : 'You have a secure update.',
    icon: '/icons/dispute-shield-v7-192.png',
    badge: '/icons/dispute-shield-v7-badge.png',
    tag,
    data: { url: notificationTarget(payload.url) },
    renotify: false
  }
  event.waitUntil(
    Promise.all([
      self.registration.showNotification(title, options),
      (async () => {
        try {
          const count = payload.unreadCount
          if (Number.isSafeInteger(count) && count >= 0) {
            if (count === 0) await self.navigator.clearAppBadge?.()
            else await self.navigator.setAppBadge?.(count)
          }
        } catch {
          /* Badging depends on user consent and platform support. */
        }
      })(),
      self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
        for (const client of clients)
          client.postMessage({
            type: 'resolve:push',
            topics: ['notifications', 'disputes', 'banking', 'support']
          })
      })
    ])
  )
})
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = notificationTarget(event.notification.data && event.notification.data.url)
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(async (clients) => {
      const target = clients.find((client) => new URL(client.url).origin === self.location.origin)
      if (target) {
        try {
          const navigated = await target.navigate(new URL(url, self.location.origin).href)
          return await (navigated || target).focus()
        } catch {
          /* A suspended phone window may no longer be navigable. */
        }
      }
      return self.clients.openWindow(url)
    })
  )
})
