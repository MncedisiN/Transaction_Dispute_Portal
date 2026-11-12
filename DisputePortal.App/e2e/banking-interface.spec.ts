import { expect, test } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

for (const width of [375, 1440]) {
  test(`dispute rows retain readable metadata at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page)
    await page.goto('/disputes')
    const row = page.locator('.case-list-item').first()
    await expect(row).toBeVisible()
    expect(await row.evaluate((element) => getComputedStyle(element).textDecorationLine)).toBe(
      'none'
    )
    const reference = row.getByText('DP-2026-123456', { exact: true })
    const date = row.locator('time')
    await expect(reference).toBeVisible()
    await expect(date).toBeVisible()
    expect((await date.boundingBox())!.y).toBeGreaterThan((await reference.boundingBox())!.y)
    await row.click()
    await expect(page).toHaveURL(/\/disputes\/case$/)
    await expect(page.getByRole('list', { name: 'Case Progress' })).toBeVisible()
    const amount = page.locator('.case-focus-amount strong')
    const caption = page.locator('.case-focus-amount > span')
    expect((await amount.boundingBox())!.y).toBeGreaterThan((await caption.boundingBox())!.y)
  })

  test(`evidence uses the intended picker buttons at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page)
    await page.goto('/disputes/case')
    await page.getByRole('button', { name: 'Evidence', exact: true }).click()
    const files = page.locator('input[type="file"]')
    await expect(files).toHaveCount(2)
    for (const picker of await files.all()) await expect(picker).toBeHidden()
    await expect(page.getByRole('button', { name: 'Attach Files', exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Take Photo', exact: true })).toBeVisible()
  })
}

for (const mode of ['customer', 'staff'] as const) {
  test(`${mode} navigation remains in the same SPA document`, async ({ page }) => {
    await mockScreens(page, mode)
    await page.goto(mode === 'staff' ? '/admin/disputes' : '/personal')
    await expect(page.locator('.primary-nav')).toBeVisible()
    const initialDocument = await page.evaluate(() => {
      const marker = crypto.randomUUID()
      document.documentElement.dataset.spaDocument = marker
      return marker
    })
    let documentRequests = 0
    page.on('request', (request) => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documentRequests++
    })
    for (const label of ['Transactions', 'Support', mode === 'staff' ? 'Cases' : 'Disputes']) {
      await page.locator('.primary-nav').getByRole('link', { name: label, exact: true }).click()
      await expect(
        page.locator('.primary-nav').getByRole('link', { name: label, exact: true })
      ).toHaveAttribute('aria-current', 'page')
      expect(await page.evaluate(() => document.documentElement.dataset.spaDocument)).toBe(
        initialDocument
      )
    }
    await page.goBack()
    await expect(page).toHaveURL(/\/support$/)
    expect(await page.evaluate(() => document.documentElement.dataset.spaDocument)).toBe(
      initialDocument
    )
    expect(documentRequests).toBe(0)
  })
}

test('profile summary avoids a nested horizontal scrollbar', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockScreens(page)
  await page.goto('/profile')
  const details = page.locator('.profile-grid > div').first()
  await expect(details.locator('dl')).toBeVisible()
  expect(await details.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true)
  expect((await details.boundingBox())!.width).toBeGreaterThan(400)
})

test('PWA serves its manifest, red shield icons, offline page and push worker', async ({
  request
}) => {
  const response = await request.get('/manifest.webmanifest')
  expect(response.ok()).toBe(true)
  const manifest = await response.json()
  expect(manifest.name).toBe('Dispute Portal')
  expect(manifest.display).toBe('standalone')
  expect(manifest.scope).toBe('/')
  expect(manifest.start_url).toBe('/')
  const shell = await request.get('/')
  expect(await shell.text()).not.toContain('viewport-fit=cover')
  expect(manifest.icons.some((icon: { purpose: string }) => icon.purpose === 'maskable')).toBe(true)
  for (const icon of manifest.icons) {
    const asset = await request.get(icon.src)
    expect(asset.ok()).toBe(true)
    expect(asset.headers()['content-type']).toContain('image/png')
  }
  const worker = await request.get('/sw.js')
  expect(worker.ok()).toBe(true)
  const workerSource = await worker.text()
  expect(workerSource).toContain('push-events.js')
  expect(workerSource).not.toContain('private-api-never-cache')
  const push = await request.get('/push-events.js')
  expect(push.ok()).toBe(true)
  expect(await push.text()).toContain('showNotification')
  const offline = await request.get('/offline.html')
  expect(offline.ok()).toBe(true)
  expect(await offline.text()).not.toMatch(/<style\b|\bstyle=/)
  expect(await offline.text()).toContain('/w3-theme-blue.css')
  const theme = await request.get('/w3-theme-blue.css')
  expect(theme.ok()).toBe(true)
  expect(await theme.text()).toContain('.w3-theme-d5')
})

for (const width of [375, 390]) {
  test(`original blue mobile controls and chat stay aligned at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page)
    await page.goto('/personal')
    const account = page.getByRole('link', { name: 'Open Account Details', exact: true })
    const transactions = page.getByRole('link', { name: 'View Transactions', exact: true })
    await expect(account).toBeVisible()
    const accountBounds = await account.boundingBox()
    expect(accountBounds!.width).toBeGreaterThan(width - 64)
    expect(Math.abs((await transactions.boundingBox())!.width - accountBounds!.width)).toBeLessThan(
      2
    )
    await page.screenshot({ path: info.outputPath('blue-accounts.png') })
    await page.goto('/disputes')
    const header = page.locator('.topbar')
    const action = page.getByRole('link', { name: 'New Dispute', exact: true })
    await expect(action).toBeVisible()
    const control = await action.boundingBox()
    expect(control!.width).toBeGreaterThan(width - 65)
    expect(control!.y).toBeGreaterThanOrEqual((await header.boundingBox())!.height)
    const blue = await action.evaluate((element) => {
      const [r, g, b] = getComputedStyle(element).backgroundColor.match(/\d+/g)!.map(Number)
      return b > r * 2 && g > r * 2
    })
    expect(blue).toBe(true)
    const navigation = page.getByRole('navigation', { name: 'Mobile Navigation' })
    const clippedLabels = await navigation.locator('a').evaluateAll((links) =>
      links
        .filter((link) => {
          const bounds = link.getBoundingClientRect()
          return [...link.childNodes].some((node) => {
            if (node.nodeType !== Node.TEXT_NODE || !node.textContent?.trim()) return false
            const range = document.createRange()
            range.selectNodeContents(node)
            const text = range.getBoundingClientRect()
            return text.left < bounds.left - 1 || text.right > bounds.right + 1
          })
        })
        .map((link) => link.textContent?.trim())
    )
    expect(clippedLabels).toEqual([])
    await page.screenshot({ path: info.outputPath('blue-disputes.png') })
    await page.goto('/support?thread=open-thread')
    const input = page.getByLabel('Your Message', { exact: true })
    await input.fill('Hi')
    await page.getByRole('button', { name: 'Send Message', exact: true }).click()
    const bubble = page.locator('.chat-bubble').filter({ hasText: 'Hi' }).last()
    await expect(bubble).toBeVisible()
    const bounds = await bubble.boundingBox()
    const parent = await bubble.locator('..').boundingBox()
    expect(bounds!.width).toBeLessThan(parent!.width - 10)
    expect(Math.abs(bounds!.x + bounds!.width - parent!.x - parent!.width)).toBeLessThan(2)
    expect(bounds!.width).toBeLessThan(width * 0.85)
    await page.screenshot({ path: info.outputPath('blue-chat.png') })
  })
}

test('PWA registers and opens its cached shell offline without caching API data', async ({
  browser,
  baseURL
}) => {
  const context = await browser.newContext({ baseURL, serviceWorkers: 'allow' })
  try {
    const page = await context.newPage()
    await mockScreens(page, 'anonymous')
    await page.goto('/sign-in')
    await page.waitForFunction(() => Boolean(navigator.serviceWorker.controller))
    const cachedUrls = await page.evaluate(async () => {
      const requests = await Promise.all(
        (await caches.keys()).map(async (name) => (await caches.open(name)).keys())
      )
      return requests.flat().map((request) => new URL(request.url).pathname)
    })
    expect(cachedUrls).toContain('/index.html')
    expect(cachedUrls.filter((url) => url.startsWith('/api/'))).toEqual([])
    await context.setOffline(true)
    await page.goto('/sign-in?offline-check=1')
    await expect(page.getByRole('status')).toContainText("You're offline")
    await expect(page.getByRole('button', { name: 'Try Again', exact: true })).toBeVisible()
    await expect(
      page.getByText('Portal settings are temporarily unavailable. Please try again.', {
        exact: true
      })
    ).toBeVisible()
  } finally {
    await context.setOffline(false)
    await context.close()
  }
})
