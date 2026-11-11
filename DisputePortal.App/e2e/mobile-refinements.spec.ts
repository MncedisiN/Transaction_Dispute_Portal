import { test, expect, type Page } from '@playwright/test'
import reference from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
async function mocks(page: Page, closed = true, staff = false) {
  const writes: { path: string; body: any }[] = []
  const thread = {
    id: 'chat',
    subject: 'City Fuel',
    customerId: 'customer',
    customerName: 'Portal Customer',
    status: closed ? 'Closed' : 'Open',
    version: 1,
    canReply: !closed,
    canClose: !closed,
    canAssign: false,
    canEscalate: false,
    relatedDisputeId: 'case',
    transactionId: 'payment',
    reference: 'DP-2026-195791',
    amount: 249.99,
    currency: 'ZAR',
    createdAt: '2026-11-06T17:00:00Z',
    lastMessageAt: '2026-11-06T18:00:00Z',
    concernLabel: 'Transaction Dispute'
  }
  await page.addInitScript(() => {
    const v = Object.assign(new EventTarget(), {
      height: 844,
      width: 390,
      offsetTop: 0,
      offsetLeft: 0,
      scale: 1
    })
    Object.defineProperty(window, 'visualViewport', { value: v, configurable: true })
  })
  await page.routeWebSocket('**/api/v1/realtime**', (socket) => socket.close())
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname
    if (request.method() !== 'GET') {
      writes.push({ path, body: request.postDataJSON() })
      if (path.endsWith('/verification/start'))
        return route.fulfill({
          json: { id: 'challenge', maskedDestination: '***4567', expiresAt: '2099-01-01T00:00:00Z' }
        })
      return route.fulfill({ json: { ...thread, id: 'chat' } })
    }
    if (path === '/api/v1/auth/me')
      return route.fulfill({
        json: {
          id: 'customer',
          displayName: 'Portal Customer',
          roles: staff ? ['super_admin'] : ['customer'],
          permissions: staff ? ['disputes.read', 'transactions.read', 'platform.manage'] : []
        }
      })
    if (path === '/api/v1/auth/devices')
      return route.fulfill({
        json: {
          canRememberDevice: false,
          requiresTwoStep: true,
          rememberedDeviceDays: 30,
          devices: []
        }
      })
    if (path === '/api/v1/auth/csrf')
      return route.fulfill({ json: { requestToken: 'isolated-test' } })
    if (path === '/api/v1/portal/reference') return route.fulfill({ json: reference })
    if (path === '/api/v1/platform/settings') return route.fulfill({ json: platformSettings() })
    if (path === '/api/v1/platform/status') return route.fulfill({ json: { enabled: true } })
    if (path === '/api/v1/profile')
      return route.fulfill({
        json: {
          id: 'customer',
          firstName: 'Portal',
          lastName: 'Customer',
          email: 'old@example.test',
          phone: '+27821234567',
          registeredAt: '2026-01-01T00:00:00Z'
        }
      })
    if (path === '/api/v1/identity/channels' || path === '/api/v1/profile/verification/channels')
      return route.fulfill({ json: { channels: ['email', 'sms', 'whatsapp'] } })
    if (path === '/api/v1/support/last-opened') return route.fulfill({ json: { threadId: null } })
    if (path === '/api/v1/support')
      return route.fulfill({
        json: [thread],
        headers: { 'X-Total-Count': '1', 'X-Page': '1', 'X-Page-Size': '20' }
      })
    if (path === '/api/v1/support/chat') return route.fulfill({ json: thread })
    if (path.endsWith('/messages'))
      return route.fulfill({
        json: [
          {
            id: 'm1',
            sequence: 1,
            authorId: 'staff',
            authorName: 'Support',
            authorRole: 'dispute_agent',
            body: 'Your conversation stays readable.',
            createdAt: '2026-11-06T18:00:00Z'
          }
        ]
      })
    if (path === '/api/v1/notifications')
      return route.fulfill({
        json: [],
        headers: { 'X-Total-Count': '0', 'X-Page': '1', 'X-Page-Size': '20' }
      })
    if (path === '/api/v1/platform/reversals')
      return route.fulfill({
        json: [
          {
            id: 'review',
            kind: 'Refund',
            status: 'Ineligible',
            decisionCode: 'already_refunded',
            outcomeMessage:
              'The original payment has already been refunded. A second refund is not allowed.',
            originalTransactionId: 'payment',
            dueAt: '2026-11-06T17:00:00Z',
            createdAt: '2026-11-06T17:00:00Z',
            reason: 'Review merchant evidence'
          }
        ]
      })
    if (path === '/api/v1/platform/debt/reviews')
      return route.fulfill({
        json: [{ id: 'review', status: 'Ineligible', decisionCode: 'already_refunded' }]
      })
    if (path === '/api/v1/platform/reports')
      return route.fulfill({ json: { journals: 0, balances: [], payments: [] } })
    if (path === '/api/v1/platform/reconciliation')
      return route.fulfill({ json: { unbalancedJournals: 0, positionDifferences: [] } })
    return route.fulfill({ json: [] })
  })
  return writes
}
for (const width of [390, 1440])
  test(`closed chat links fit and new conversation fills the panel at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 844 })
    await mocks(page)
    await page.goto('/support?thread=chat')
    await expect(page.getByRole('log', { name: 'Conversation Messages' })).toContainText(
      'Your conversation stays readable.'
    )
    const links = page.getByRole('navigation', { name: 'Linked Records' })
    await expect(links.getByRole('link')).toHaveCount(2)
    const bounds = await links.getByRole('link').evaluateAll((els) =>
      els.map((el) => {
        const r = el.getBoundingClientRect()
        return { left: r.left, right: r.right, width: r.width }
      })
    )
    expect(bounds[0]!.right).toBeLessThanOrEqual(bounds[1]!.left)
    expect(bounds[1]!.right).toBeLessThanOrEqual(width)
    const button = page.getByRole('link', { name: 'Start New Conversation' })
    await expect(button).toBeVisible()
    const widths = await button.evaluate((el) => ({
      button: el.getBoundingClientRect().width,
      parent: el.parentElement!.clientWidth,
      padding:
        parseFloat(getComputedStyle(el.parentElement!).paddingLeft) +
        parseFloat(getComputedStyle(el.parentElement!).paddingRight)
    }))
    expect(Math.abs(widths.button - (widths.parent - widths.padding))).toBeLessThan(2)
    await page.screenshot({ path: info.outputPath('closed-chat.png'), fullPage: true })
  })
test('mobile contact change submits a canonical number and keeps focused controls above the keyboard', async ({
  page
}, info) => {
  await page.setViewportSize({ width: 390, height: 844 })
  const writes = await mocks(page)
  await page.goto('/profile')
  const disclosure = page
    .locator('details')
    .filter({ has: page.locator('summary').filter({ hasText: 'Change Email Or Mobile' }) })
  await disclosure.locator('summary').click()
  const form = disclosure.locator('form')
  await form.locator('select').first().selectOption('phone')
  await expect(page.getByText('Country Calling Code', { exact: true })).toBeVisible()
  await form.locator('input[type=tel]').fill('821234567')
  const password = form.locator('input[type=password]')
  await password.fill('CurrentPass!2026')
  await page.evaluate(() => {
    const v = window.visualViewport as any
    v.height = 410
    v.dispatchEvent(new Event('resize'))
  })
  await expect(page.locator('html')).toHaveClass(/keyboard-open/)
  await expect
    .poll(async () => {
      const r = await password.boundingBox()
      return !!r && r.y >= 0 && r.y + r.height <= 410
    })
    .toBe(true)
  expect(await password.evaluate((el) => getComputedStyle(el).fontSize)).toBe('16px')
  await page.screenshot({ path: info.outputPath('contact-keyboard.png') })
  await page.evaluate(() => {
    ;(document.activeElement as HTMLElement)?.blur()
    const v = window.visualViewport as any
    v.height = 844
    v.dispatchEvent(new Event('resize'))
  })
  await form.getByRole('button', { name: 'Send Verification Code' }).click()
  await expect
    .poll(() => writes.find((w) => w.path.endsWith('/verification/start'))?.body)
    .toMatchObject({ field: 'phone', phoneCountry: 'ZA', destination: '+27821234567' })
})
test('mobile conversation closure dialog remains inside the visible keyboard viewport', async ({
  page
}, info) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript(() => sessionStorage.setItem('portal.workspace:customer', 'staff'))
  await mocks(page, false, true)
  await page.goto('/support?thread=chat')
  await page.getByRole('button', { name: 'Conversation Actions' }).click()
  await page.getByRole('button', { name: 'Close Conversation', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.locator('textarea').fill('The customer enquiry is now complete.')
  await page.evaluate(() => {
    const v = window.visualViewport as any
    v.height = 410
    v.dispatchEvent(new Event('resize'))
  })
  await expect
    .poll(async () => {
      const r = await dialog.boundingBox()
      return !!r && r.y >= 0 && r.y + r.height <= 410
    })
    .toBe(true)
  const field = dialog.locator('textarea')
  expect(await field.evaluate((el) => getComputedStyle(el).fontSize)).toBe('16px')
  const r = await field.boundingBox()
  expect(r!.y + r!.height).toBeLessThanOrEqual(410)
  await page.screenshot({ path: info.outputPath('closure-keyboard.png') })
})
test('review queue merges eligibility and schedule rows and links the original transaction', async ({
  page
}, info) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mocks(page, true, true)
  await page.goto('/admin/dispute-operations?review=review')
  await expect(page.locator('.review-queue-item')).toHaveCount(1)
  await expect(page.locator('.review-outcome-detail')).toContainText('already been refunded')
  await expect(page.getByRole('link', { name: 'Open Transaction', exact: false })).toHaveAttribute(
    'href',
    '/admin/transactions/payment'
  )
  await page.locator('.review-queue-item').scrollIntoViewIfNeeded()
  await expect(page.locator('.review-outcome-detail')).toBeVisible()
  await page.screenshot({ path: info.outputPath('review-outcome.png'), fullPage: true })
})
