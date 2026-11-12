import { expect, test } from '@playwright/test'
import { mockScreens, screenTransaction } from './fixtures/screens'

for (const width of [375, 390, 1440]) {
  test(`transfer balance guidance and detail alignment at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 844 })
    await mockScreens(page)
    await page.goto('/personal')
    await page.getByRole('button', { name: 'Transfer', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Amount (ZAR)', { exact: true }).fill('500000')
    await expect(dialog.getByRole('status')).toContainText('Insufficient funds')
    await expect(
      dialog.getByRole('button', { name: 'Review Transfer', exact: true })
    ).toBeDisabled()
    await dialog.getByLabel('Amount (ZAR)', { exact: true }).fill('10')
    await page.route('**/api/v1/platform/payments/quote', (route) =>
      route.fulfill({
        status: 400,
        json: { detail: 'Use an amount within configured limits and currency precision.' }
      })
    )
    await dialog.getByRole('button', { name: 'Review Transfer', exact: true }).click()
    await expect(
      page
        .getByText(
          'We cannot transfer this amount. Try a smaller amount and check the decimal places.'
        )
        .first()
    ).toBeVisible()
    await expect(
      page.getByText('Use an amount within configured limits and currency precision.')
    ).toHaveCount(0)
    await page.goto('/transactions/payment')
    const amount = await page.locator('.detail-amount-block > strong').boundingBox()
    const status = await page.locator('.detail-status').boundingBox()
    expect(status!.x).toBeGreaterThan(amount!.x + amount!.width - 1)
    expect(Math.abs(status!.y + status!.height / 2 - amount!.y - amount!.height / 2)).toBeLessThan(
      12
    )
    await page.screenshot({ path: info.outputPath('transaction-detail.png') })
  })

  test(`transaction pagination uses the row width at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 844 })
    await mockScreens(page)
    await page.route('**/api/v1/transactions?*', (route) =>
      route.fulfill({ json: { items: [screenTransaction], page: 1, pageSize: 20, total: 40 } })
    )
    await page.goto('/transactions')
    const pager = page.locator('.pagination')
    await expect(pager).toBeVisible()
    const label = (await pager.locator('span').boundingBox())!
    const buttons = (await pager.locator('div').boundingBox())!
    expect(buttons.x).toBeGreaterThan(label.x + label.width)
    expect(Math.abs(buttons.y - label.y)).toBeLessThan(28)
    for (const button of await pager.getByRole('button').all())
      expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(44)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath('transaction-pagination.png') })
  })

  test(`profile sign-in rows and verification buttons at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 844 })
    await mockScreens(page)
    await page.route('**/api/v1/security/sign-ins', (route) =>
      route.fulfill({
        json: [
          {
            id: 'sign-in',
            deviceType: 'Phone',
            browser: 'Safari',
            eventType: 'SignIn',
            outcome: 'Success',
            occurredAt: '2026-11-10T05:00:00Z',
            location: 'South Africa'
          }
        ]
      })
    )
    await page.goto('/profile?section=security')
    const button = page.getByRole('switch', { name: 'Turn Off Two-Step Verification' })
    await expect(button).toBeVisible()
    expect((await button.boundingBox())!.width).toBeGreaterThan(
      (await page.locator('.verification-setting').boundingBox())!.width - 2
    )
    const row = page.locator('.signin-row').first()
    await expect(row).toBeVisible()
    const icon = (await row.locator('.signin-icon').boundingBox())!
    const copy = (await row.locator('.signin-copy').boundingBox())!
    expect(copy.x).toBeGreaterThan(icon.x + icon.width)
    expect(Math.abs(copy.y + copy.height / 2 - icon.y - icon.height / 2)).toBeLessThan(5)
    await button.click()
    await expect(
      page.getByRole('heading', { name: 'Turn Off Two-Step Verification?' })
    ).toBeVisible()
    await page.screenshot({ path: info.outputPath('profile-security.png') })
  })

  test(`policy history and availability choices fit at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 844 })
    await mockScreens(page, 'staff')
    await page.goto('/admin/disputes')
    await page.getByRole('button', { name: /^Your Availability:/ }).click()
    const close = page.getByRole('dialog').getByRole('button', { name: 'Close', exact: true })
    const box = (await close.boundingBox())!
    expect(Math.abs(box.width - box.height)).toBeLessThan(2)
    const selected = page.getByRole('dialog').locator('.choice-option.selected')
    const copy = (await selected.locator('.choice-option-copy').boundingBox())!
    const check = (await selected.locator('svg').boundingBox())!
    expect(check.x).toBeGreaterThan(copy.x + copy.width - 1)
    expect(Math.abs(check.y + check.height / 2 - copy.y - copy.height / 2)).toBeLessThan(4)
    await page.screenshot({ path: info.outputPath('availability.png') })
    await close.click()
    await page.goto('/admin/dispute-rules')
    const history = page.locator('.mobile-history-table').first()
    await expect(history).toBeVisible()
    if (width < 600) {
      expect(await history.evaluate((e) => getComputedStyle(e).display)).toBe('block')
      expect((await history.boundingBox())!.width).toBeGreaterThan(width - 100)
      for (const button of await history.getByRole('button').all()) {
        const bounds = (await button.boundingBox())!
        expect(bounds.x).toBeGreaterThanOrEqual(0)
        expect(bounds.x + bounds.width).toBeLessThanOrEqual(width)
      }
    }
    await history.scrollIntoViewIfNeeded()
    await page.screenshot({ path: info.outputPath('policy-history.png') })
  })
}

test('transfer notification preserves account details and opens the correct transaction', async ({
  page,
  request
}) => {
  await mockScreens(page)
  await page.goto('/personal')
  const code = await (await request.get('/push-events.js')).text()
  const result = await page.evaluate(async (source) => {
    const listeners: Record<string, (event: any) => void> = {}
    const shown: any[] = []
    const messages: any[] = []
    const paths: string[] = []
    let focused = false
    const worker = {
      location: { origin: location.origin },
      navigator: {},
      registration: {
        showNotification: async (title: string, options: any) => shown.push({ title, ...options })
      },
      clients: {
        matchAll: async () => [
          {
            url: location.href,
            postMessage: (data: any) => messages.push(data),
            navigate: async (path: string) => {
              paths.push(path)
              return {
                focus: async () => {
                  focused = true
                }
              }
            }
          }
        ],
        openWindow: async (path: string) => paths.push(path)
      },
      addEventListener: (name: string, callback: any) => {
        listeners[name] = callback
      }
    }
    new Function('self', source)(worker)
    const payload = {
      title: 'Dispute Portal',
      body: 'R 500.00 transferred from Cheque ••••4821 to Savings ••••7103.',
      url: '/transactions/transfer-one',
      tag: 'transfer-one'
    }
    let pending = Promise.resolve()
    listeners.push!({
      data: { json: () => payload },
      waitUntil: (task: Promise<void>) => {
        pending = task
      }
    })
    await pending
    listeners.notificationclick!({
      notification: { data: shown[0].data, close() {} },
      waitUntil: (task: Promise<void>) => {
        pending = task
      }
    })
    await pending
    return { shown, messages, paths, focused }
  }, code)
  expect(result.shown[0].body).toContain('Cheque')
  expect(result.shown[0].body).toContain('Savings')
  expect(result.shown[0].icon).toContain('shield')
  expect(result.shown[0].title).toBe('New update')
  expect(result.messages[0].topics).toContain('banking')
  expect(result.paths[0]).toContain('/transactions/transfer-one')
  expect(result.focused).toBe(true)
})

test('granted browser permission enables push by default without an application toggle', async ({
  page,
  context,
  browserName
}) => {
  test.skip(
    browserName !== 'chromium',
    'Push subscription simulation uses Chromium notification permissions.'
  )
  await context.grantPermissions(['notifications'])
  await mockScreens(page)
  let subscribed = false
  let saves = 0
  const key = Buffer.from([4, ...Array(64).fill(1)]).toString('base64url')
  await page.route('**/api/v1/notification-settings', (route) =>
    route.fulfill({
      json: { channel: 'inapp', pushAvailable: true, pushSubscribed: subscribed, publicKey: key }
    })
  )
  await page.route('**/api/v1/notification-settings/push', (route) => {
    saves++
    subscribed = true
    return route.fulfill({ status: 204 })
  })
  await page.route('**/api/v1/notification-settings/push/disable', (route) => {
    subscribed = false
    return route.fulfill({ status: 204 })
  })
  await page.addInitScript(() => {
    let subscription: any = null
    const registration = {
      active: {},
      pushManager: {
        getSubscription: async () => subscription,
        subscribe: async (options: any) => {
          subscription = {
            options,
            toJSON: () => ({
              endpoint: 'https://fcm.googleapis.com/fcm/send/example',
              keys: { p256dh: 'browser-key', auth: 'browser-auth' }
            }),
            unsubscribe: async () => {
              subscription = null
              return true
            }
          }
          return subscription
        }
      }
    }
    Object.defineProperty(navigator.serviceWorker, 'getRegistration', {
      value: async () => registration
    })
  })
  await page.goto('/profile?section=alerts')
  await expect.poll(() => saves).toBeGreaterThan(0)
  await expect(page.locator('.notification-preferences')).toContainText(
    'updates are delivered to this device'
  )
  await expect(page.getByRole('switch', { name: 'Push Notifications' })).toHaveCount(0)
  await expect(page.locator('.push-state')).toHaveCount(0)
  await expect(
    page.getByRole('button', { name: /Turn On|Turn Off|Allow Notifications/ })
  ).toHaveCount(0)
  await expect(page.locator('.important-message-preference')).toContainText(
    'Always available in the app.'
  )
  await page.reload()
  await expect(page.locator('.notification-preferences')).toContainText(
    'updates are delivered to this device'
  )
  expect(subscribed).toBe(true)
})

test('chat confirmation, customer notes, evidence actions and review queue remain readable on phone', async ({
  page
}, info) => {
  await page.setViewportSize({ width: 390, height: 844 })
  const state = await mockScreens(page, 'staff')
  await page.goto('/admin/disputes/case?section=activity')
  const detail = await page.evaluate(
    async () => await (await fetch('/api/v1/admin/disputes/case')).json()
  )
  await page.route('**/api/v1/admin/disputes/case', (route) =>
    route.fulfill({
      json: {
        ...detail,
        notes: [
          {
            createdAt: '2026-11-10T05:00:00Z',
            visibility: 'Customer',
            authorName: 'Alex Administrator',
            actorIdentity: { displayName: 'Alex Administrator', roles: ['super_admin'] },
            content: 'We have checked the evidence and requested a specialist review.'
          }
        ]
      }
    })
  )
  await page.reload()
  const note = page.locator('.staff-note').first()
  await expect(note).toContainText('CUSTOMER UPDATE')
  expect((await note.boundingBox())!.height).toBeLessThan(210)
  await note.scrollIntoViewIfNeeded()
  await page.screenshot({ path: info.outputPath('customer-update.png') })
  await page.getByRole('button', { name: 'Evidence', exact: true }).click()
  const actions = page.locator('.evidence-file-actions').first()
  await expect(actions).toBeVisible()
  const view = actions.getByRole('button', { name: 'View', exact: true })
  expect((await view.boundingBox())!.width).toBeGreaterThan(
    (await actions.boundingBox())!.width - 2
  )
  await page.route('**/api/v1/platform/reversals', (route) =>
    route.fulfill({
      json: [
        {
          id: 'review',
          kind: 'Refund',
          status: 'Skipped',
          reason: 'The original payment has already been refunded.',
          outcomeMessage: 'No additional refund is needed.',
          originalTransactionId: 'payment'
        }
      ]
    })
  )
  await page.goto('/admin/dispute-operations')
  const review = page.getByRole('button', { name: 'Refund Review Skipped' })
  await review.click()
  await expect(page.locator('.review-metadata dd')).toHaveText(
    'The original payment has already been refunded.'
  )
  await expect(page.getByText('Date unavailable', { exact: true })).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({ path: info.outputPath('review-queue.png') })
  await page.getByRole('button', { name: 'Country Settings', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Country Policies', exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.route('**/api/v1/admin/portal-settings', (route) =>
    route.fulfill({
      json: {
        version: 4,
        settings: { customerSearchEnabled: false, customerAccountOpeningEnabled: false }
      }
    })
  )
  await page.getByRole('button', { name: 'Portal Settings', exact: true }).click()
  const save = page.getByRole('button', { name: 'Save Customer Settings', exact: true })
  await expect(save).toBeVisible()
  expect((await save.boundingBox())!.width).toBeGreaterThan(290)
  expect(state.requests.filter((x) => x.path === '/api/v1/support')).toHaveLength(0)
})

test('guided enquiry leaves a readable confirmation reminder and sends only on confirmation', async ({
  page
}, info) => {
  await page.setViewportSize({ width: 390, height: 844 })
  const state = await mockScreens(page)
  await page.goto('/support?new=1')
  const footer = page.locator('.guided-footer')
  await expect(footer).toHaveText('Your enquiry is sent only after you confirm.')
  for (const choice of await page.locator('.guided-choice').all())
    expect((await choice.boundingBox())!.height).toBeLessThan(90)
  await page.screenshot({ path: info.outputPath('guided-enquiry.png') })
  await page.getByRole('button', { name: 'Something Else', exact: true }).click()
  await page
    .getByLabel('Your Message', { exact: true })
    .fill('Please help me understand the next steps.')
  await page.getByRole('button', { name: 'Continue', exact: true }).click()
  if (await page.getByRole('button', { name: 'Skip', exact: true }).isVisible())
    await page.getByRole('button', { name: 'Skip', exact: true }).click()
  else if (await page.getByLabel('Your Message', { exact: true }).isVisible())
    await page.getByRole('button', { name: 'Continue', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Submit Enquiry', exact: true })).toBeVisible()
  expect(state.requests.filter((x) => x.path === '/api/v1/support')).toHaveLength(0)
  await page.getByRole('button', { name: 'Submit Enquiry', exact: true }).click()
  await expect(page.getByRole('log', { name: 'Conversation Messages' })).toBeVisible()
  expect(state.requests.filter((x) => x.path === '/api/v1/support')).toHaveLength(1)
})

for (const permission of ['default', 'granted', 'denied', 'unavailable', 'opted-out'] as const) {
  test(`sign-in restores push with ${permission} permission without blocking login`, async ({
    page
  }) => {
    await mockScreens(page)
    let signedIn = false
    let saves = 0
    const key = Buffer.from([4, ...Array(64).fill(1)]).toString('base64url')
    await page.route('**/api/v1/auth/me', (route) =>
      signedIn
        ? route.fulfill({
            json: {
              id: 'customer',
              displayName: 'Alex Customer',
              roles: ['customer'],
              permissions: []
            }
          })
        : route.fulfill({ status: 401, json: { title: 'Sign In Required' } })
    )
    await page.route('**/api/v1/auth/sign-in', (route) => {
      signedIn = true
      return route.fulfill({ json: { authenticated: true } })
    })
    await page.route('**/api/v1/notification-settings', (route) =>
      route.fulfill({ json: { channel: 'inapp', pushAvailable: true, publicKey: key } })
    )
    await page.route('**/api/v1/notification-settings/push', (route) => {
      expect(signedIn).toBe(true)
      saves++
      return route.fulfill({ status: 204 })
    })
    await page.addInitScript((initial) => {
      let value =
        initial === 'opted-out' ? 'granted' : initial === 'unavailable' ? 'default' : initial
      let requests = 0
      let activated = false
      let subscription: any = null
      if (initial === 'opted-out') localStorage.setItem('portal.push-off:customer', '1')
      Object.defineProperty(Notification, 'permission', { get: () => value })
      Object.defineProperty(Notification, 'requestPermission', {
        value: () => {
          requests++
          activated = navigator.userActivation.isActive
          if (initial === 'unavailable') return Promise.reject(new Error('Permission unavailable'))
          return new Promise((resolve) =>
            setTimeout(() => {
              value = 'granted'
              resolve('granted')
            }, 300)
          )
        }
      })
      Object.defineProperty(window, 'pushPermissionResult', {
        get: () => ({ requests, activated })
      })
      const registration = {
        active: {},
        pushManager: {
          getSubscription: async () => subscription,
          subscribe: async (options: any) => {
            subscription = {
              options,
              toJSON: () => ({
                endpoint: 'https://fcm.googleapis.com/fcm/send/example',
                keys: { p256dh: 'browser-key', auth: 'browser-auth' }
              }),
              unsubscribe: async () => true
            }
            return subscription
          }
        }
      }
      Object.defineProperty(navigator.serviceWorker, 'getRegistration', {
        value: async () => registration
      })
    }, permission)
    await page.goto('/sign-in')
    await page.getByLabel('Email Or Mobile Number', { exact: true }).fill('alex@example.test')
    await page.getByLabel('Password', { exact: true }).fill('ExamplePassword!123')
    await page.getByRole('button', { name: 'Sign In Securely', exact: true }).click()
    await expect(page).toHaveURL(/\/personal$/)
    if (permission === 'default' || permission === 'granted') {
      await expect.poll(() => saves).toBeGreaterThan(0)
    } else {
      await page.getByRole('link', { name: 'Transactions', exact: true }).first().click()
      expect(saves).toBe(0)
    }
    const result = await page.evaluate(() => (window as any).pushPermissionResult)
    expect(result.requests).toBe(permission === 'default' || permission === 'unavailable' ? 1 : 0)
    if (result.requests) expect(result.activated).toBe(true)
  })
}
