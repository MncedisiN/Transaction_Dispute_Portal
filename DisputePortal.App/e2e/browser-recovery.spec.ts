import { expect, test } from '@playwright/test'
import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import type { AddressInfo } from 'node:net'

test.use({ serviceWorkers: 'allow' })

test('recovery bypasses a failing legacy API worker and preserves sign-in cookies and preferences', async ({
  page,
  context
}) => {
  const paths: string[] = []
  const html = await readFile(new URL('../browser-recovery.html', import.meta.url), 'utf8')
  const script = await readFile(new URL('../browser-recovery.js', import.meta.url), 'utf8')
  const server = createServer((request, response) => {
    const path = new URL(request.url!, 'http://localhost').pathname
    paths.push(path)
    response.setHeader('Cache-Control', 'no-store')
    if (path === '/legacy-worker.js') {
      response.setHeader('Content-Type', 'application/javascript')
      response.end(`
        self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
        self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
        self.addEventListener('fetch', event => {
          if (new URL(event.request.url).pathname.startsWith('/api/'))
            event.respondWith(Promise.reject(new Error('no-response')));
        });
      `)
    } else if (path === '/install/client-recovery') {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(html)
    } else if (path === '/install/client-recovery/recovery.js') {
      response.setHeader('Content-Type', 'application/javascript')
      response.end(script)
    } else {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end('<!doctype html><html><body><h1>Welcome Back</h1></body></html>')
    }
  })
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const origin = `http://127.0.0.1:${(server.address() as AddressInfo).port}`
  try {
    await context.addCookies([
      { name: 'recovery-canary', value: 'keep', url: origin, httpOnly: true, sameSite: 'Lax' }
    ])
    await page.goto(origin)
    await page.evaluate(async () => {
      localStorage.setItem('portal-theme', 'dark')
      await (
        await caches.open('workbox-precache-legacy')
      ).put('/index.html', new Response('old app'))
      await (await caches.open('unrelated-cache')).put('/keep', new Response('keep'))
      await navigator.serviceWorker.register('/legacy-worker.js')
      await navigator.serviceWorker.ready
    })
    await page.waitForFunction(() => Boolean(navigator.serviceWorker.controller))
    await expect(page.goto(origin + '/api/v1/client-recovery')).rejects.toThrow()
    expect(paths).not.toContain('/api/v1/client-recovery')
    await page.goto(origin + '/install/client-recovery')
    await expect(page).toHaveURL(origin + '/sign-in?redirect=%2F&recovered=1')
    await expect(page.getByRole('heading', { name: 'Welcome Back', exact: true })).toBeVisible()
    expect(paths).toContain('/install/client-recovery/recovery.js')
    expect(paths.some((path) => path.startsWith('/api/'))).toBe(false)
    const state = await page.evaluate(async () => ({
      controller: navigator.serviceWorker.controller?.scriptURL ?? null,
      registrations: (await navigator.serviceWorker.getRegistrations()).length,
      caches: await caches.keys(),
      theme: localStorage.getItem('portal-theme')
    }))
    expect(state.controller).toBeNull()
    expect(state.registrations).toBe(0)
    expect(state.caches).toEqual(['unrelated-cache'])
    expect(state.theme).toBe('dark')
    expect(
      (await context.cookies(origin)).find((cookie) => cookie.name === 'recovery-canary')?.value
    ).toBe('keep')
  } finally {
    await new Promise<void>((resolve, reject) =>
      server.close((error) => (error ? reject(error) : resolve()))
    )
  }
})
