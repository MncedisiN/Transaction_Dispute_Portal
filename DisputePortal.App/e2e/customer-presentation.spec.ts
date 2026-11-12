import { expect, test } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

test('customer labels and appearance stay friendly', async ({ page }, info) => {
  await mockScreens(page, 'customer')
  await page.route('**/api/v1/finance/disputes/case/activity', (route) =>
    route.fulfill({
      json: [
        {
          id: '1',
          action: 'CustomerUpdate',
          actor: 'Portal Administrator',
          occurredAt: '2026-11-11T10:00:00Z',
          detail: 'A service consultant submitted this dispute on behalf of the customer.'
        },
        {
          id: '2',
          action: 'DisputeRefundPosted',
          actor: 'System',
          occurredAt: '2026-11-11T10:00:00Z',
          detail: null
        },
        {
          id: '3',
          action: 'CustomerUpdate',
          actor: 'Customer',
          actorIdentity: {
            kind: 'User',
            userId: null,
            roles: [],
            accountName: 'Alex Customer',
            fullName: 'Alex Customer'
          },
          occurredAt: '2026-11-11T10:00:00Z',
          detail: 'Customer withdrew and stopped this case.'
        }
      ]
    })
  )
  await page.goto('/disputes/case')
  await expect(
    page.getByText('You withdrew this dispute and ended the review.', { exact: true })
  ).toBeVisible()
  await expect(page.locator('.case-activity .actor-name').last()).toHaveText('You')
  await expect(page.getByText('Automatic refund', { exact: true })).toBeVisible()
  await expect(page.getByText('Portal Administrator', { exact: true })).toHaveCount(0)
  await expect(
    page.getByText('An agent submitted this dispute on your behalf.', { exact: true })
  ).toBeVisible()
  await expect(page.getByRole('link', { name: 'Chat with us' })).toBeVisible()
  await expect(page.locator('.appearance-button')).toHaveCount(0)
  await page.getByRole('button', { name: 'Profile Menu' }).click()
  await page.getByRole('button', { name: 'Appearance', exact: true }).click()
  await page.getByRole('button', { name: 'Dark Easy on the eyes' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await page.screenshot({ path: info.outputPath('appearance-dark.png') })
  await page.getByRole('button', { name: 'Light Bright and clear' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await page.getByRole('button', { name: 'Close Appearance' }).click()
  await page.getByRole('button', { name: 'Profile Menu' }).click()
  await expect(page.getByText('Your account', { exact: true })).toBeVisible()
})

test('direct dispute chat never flashes the support inbox', async ({ page }) => {
  const fixture = await mockScreens(page, 'customer')
  await page.route('**/api/v1/support?**', async (route) => {
    const url = new URL(route.request().url())
    if (!url.searchParams.has('disputeId')) return route.fallback()
    await new Promise((resolve) => setTimeout(resolve, 800))
    return route.fulfill({ json: [{ ...fixture.thread, disputeId: 'case' }] })
  })
  await page.goto('/support?dispute=case')
  await expect(page.getByRole('heading', { name: 'Opening your chat' })).toBeVisible()
  await expect(page.locator('.inbox-panel')).toHaveCount(0)
  await expect(page.locator('.topbar')).toHaveCount(0)
  await expect(page.getByRole('log', { name: 'Conversation Messages' })).toBeVisible()
  await expect(page.locator('.inbox-panel')).toHaveCount(0)
})

for (const theme of ['light', 'dark'])
  test(`enquiry details are readable in ${theme} mode`, async ({ page }, info) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
    const fixture = await mockScreens(page, 'customer')
    fixture.messages.push({
      id: 'own',
      sequence: 2,
      authorId: 'customer',
      authorName: 'Alex Customer',
      authorRole: 'customer',
      body: 'Please help with this payment.',
      createdAt: '2026-11-11T10:00:00Z',
      enquiry: { concernLabel: 'General Enquiry' }
    } as (typeof fixture.messages)[number])
    await page.goto('/support?thread=open-thread')
    await page.getByRole('button', { name: 'Enquiry Details' }).click()
    const value = page.locator('.message-own .chat-enquiry-details td')
    await expect(value).toHaveText('General Enquiry')
    await expect(value).toHaveCSS('color', 'rgb(255, 255, 255)')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath(`chat-${theme}.png`), fullPage: true })
  })
import { screenTransaction } from './fixtures/screens'

test('review details sit side by side and confirmation is centred', async ({ page }, info) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await mockScreens(page)
  await page.goto('/disputes/new/payment')
  await page.getByRole('radio', { name: 'I was charged more than once', exact: true }).check()
  await page
    .getByLabel('Add A Few Details', { exact: true })
    .fill('The same payment appears twice.')
  await page.getByRole('button', { name: 'Review Your Dispute', exact: true }).click()
  const transaction = await page.locator('.review-transaction-heading').boundingBox()
  const account = await page.locator('.review-transaction-facts').boundingBox()
  expect(account!.x).toBeGreaterThan(transaction!.x + transaction!.width)
  await page.screenshot({ path: info.outputPath('review-desktop.png'), fullPage: true })
  await page.getByRole('button', { name: 'Submit Dispute', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Dispute Received' })).toBeVisible()
  await expect(page.locator('.confirmation-hero')).toHaveCSS('text-align', 'center')
  await page.screenshot({ path: info.outputPath('dispute-received.png'), fullPage: true })
})

test('customer resolution credits the agent without exposing internal roles', async ({ page }) => {
  await mockScreens(page)
  await page.route('**/api/v1/disputes/case', (route) =>
    route.fulfill({
      json: {
        dispute: {
          id: 'case',
          transactionId: 'payment',
          reference: 'DP-2026-123456',
          amount: 249.99,
          currency: 'ZAR',
          category: 'Duplicate',
          status: 'Resolved',
          priority: 2,
          version: 1,
          createdAt: '2026-11-11T10:00:00Z',
          resolution: 'Your review is complete.',
          transactionDescription: screenTransaction.description
        },
        merchantName: 'City Fuel',
        transactionAt: screenTransaction.transactionAt,
        history: [],
        notes: [],
        resolvedBy: {
          kind: 'User',
          userId: 'admin',
          fullName: 'Portal Administrator',
          label: 'Portal Administrator',
          roles: ['super_admin']
        }
      }
    })
  )
  await page.goto('/disputes/case')
  await expect(page.getByText('Resolved by an agent', { exact: true })).toBeVisible()
  await expect(page.getByText('Portal Administrator')).toHaveCount(0)
})

for (const width of [375, 1440])
  test(`notification count stays inside its control at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page)
    await page.route('**/api/v1/notifications/unread-count', (route) =>
      route.fulfill({ json: { count: 125 } })
    )
    await page.goto('/disputes/case')
    const control = page.getByRole('link', { name: 'Notifications, 125 unread' })
    await expect(control.locator('.notification-count')).toHaveText('99+')
    await expect(page.locator('.appearance-button')).toHaveCount(0)
    const button = await control.boundingBox(),
      badge = await control.locator('.notification-count').boundingBox()
    expect(badge!.x).toBeGreaterThanOrEqual(button!.x)
    expect(badge!.x + badge!.width).toBeLessThanOrEqual(button!.x + button!.width)
    expect(badge!.y).toBeGreaterThanOrEqual(button!.y)
    expect(badge!.y + badge!.height).toBeLessThanOrEqual(button!.y + button!.height)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath(`header-${width}.png`) })
  })
