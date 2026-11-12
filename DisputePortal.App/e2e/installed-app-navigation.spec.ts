import { expect, test } from '@playwright/test'

test.use({ serviceWorkers: 'allow', ignoreHTTPSErrors: false })

test('installed app keeps online sign-in navigation and API responses available', async ({
  page,
  context
}) => {
  test.setTimeout(90_000)
  await page.goto('/sign-in')
  await expect(page.getByRole('heading', { name: 'Welcome Back', exact: true })).toBeVisible()

  await page.evaluate(async () => {
    let readyTimer: ReturnType<typeof setTimeout> | undefined
    const registration = await Promise.race([
      navigator.serviceWorker.ready,
      new Promise<never>((_, reject) => {
        readyTimer = setTimeout(
          () => reject(new Error('Service worker readiness timed out')),
          45_000
        )
      })
    ]).finally(() => clearTimeout(readyTimer))
    const worker = registration.active
    if (!worker) throw new Error('No active service worker')
    if (worker.state !== 'activated')
      await new Promise<void>((resolve, reject) => {
        let timer: ReturnType<typeof setTimeout> | undefined
        const finish = (error?: Error) => {
          clearTimeout(timer)
          worker.removeEventListener('statechange', changed)
          error ? reject(error) : resolve()
        }
        const changed = () => {
          if (worker.state === 'activated') finish()
          else if (worker.state === 'redundant')
            finish(new Error('Service worker became redundant'))
        }
        worker.addEventListener('statechange', changed)
        timer = setTimeout(() => finish(new Error('Service worker activation timed out')), 15_000)
        changed()
      })
  })

  const reloaded = await page.reload()
  expect(reloaded?.fromServiceWorker()).toBe(true)
  await expect(page.getByRole('heading', { name: 'Welcome Back', exact: true })).toBeVisible()

  const second = await context.newPage()
  const navigated = await second.goto('/sign-in/')
  expect(navigated?.fromServiceWorker()).toBe(true)
  await expect(second.getByRole('heading', { name: 'Welcome Back', exact: true })).toBeVisible()

  const apiResponse = second.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/v1/auth/csrf'
  )
  const state = await second.evaluate(async () => {
    const response = await fetch('/api/v1/auth/csrf', { credentials: 'include', cache: 'no-store' })
    const body: unknown = await response.json()
    const apiCacheEntries: string[] = []
    for (const name of await caches.keys()) {
      for (const request of await (await caches.open(name)).keys()) {
        const path = new URL(request.url).pathname
        if (path.startsWith('/api/')) apiCacheEntries.push(path)
      }
    }
    return {
      online: navigator.onLine,
      secureContext: window.isSecureContext,
      controller: navigator.serviceWorker.controller?.scriptURL,
      expectedWorker: new URL('/sw.js', location.origin).href,
      status: response.status,
      contentType: response.headers.get('content-type'),
      cacheControl: response.headers.get('cache-control'),
      hasRequestToken:
        !!body &&
        typeof body === 'object' &&
        'requestToken' in body &&
        typeof body.requestToken === 'string',
      apiCacheEntries
    }
  })
  expect((await apiResponse).fromServiceWorker()).toBe(false)
  expect(state.online).toBe(true)
  expect(state.secureContext).toBe(true)
  expect(state.controller).toBe(state.expectedWorker)
  expect(state.status).toBe(200)
  expect(state.contentType).toContain('application/json')
  expect(state.cacheControl).toContain('private')
  expect(state.cacheControl).toContain('no-store')
  expect(state.hasRequestToken).toBe(true)
  expect(state.apiCacheEntries).toEqual([])
})
