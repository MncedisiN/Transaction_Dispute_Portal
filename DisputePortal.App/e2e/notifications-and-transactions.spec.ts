import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { expect, test } from '@playwright/test'
test.use({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' })
test.setTimeout(120000)
test('notifications stay full width in both themes and link a customer case to its original transaction', async ({
  page
}, info) => {
  const caseId = '40000000-0000-0000-0000-000000000001',
    transactionId = '30000000-0000-0000-0000-000000000002'
  const items = [
    {
      id: 'aa000000-0000-4000-8000-000000000001',
      disputeId: caseId,
      type: 'DisputeCompleted',
      title: 'Your case has an update',
      body: 'Your specialist has reviewed the payment. ' + 'Reference'.repeat(30),
      isRead: false,
      createdAt: new Date().toISOString()
    },
    {
      id: 'aa000000-0000-4000-8000-000000000002',
      disputeId: caseId,
      type: 'DisputeStarted',
      title: 'Your case is being reviewed',
      body: 'Track progress and contact your specialist from the case.',
      isRead: true,
      createdAt: new Date(Date.now() - 86400000).toISOString()
    }
  ]
  let reads = 0
  await page.route('**/api/v1/notifications**', (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })
    const request = route.request(),
      path = new URL(request.url()).pathname
    if (request.method() === 'POST') {
      const item = items.find((item) => path.endsWith('/' + item.id + '/read'))
      expect(item).toBeTruthy()
      item!.isRead = true
      reads++
      return route.fulfill({ status: 204 })
    }
    return route.fulfill({ json: { items, page: 1, pageSize: 20, total: items.length } })
  })
  await page.goto('/sign-in')
  await page.getByLabel('Email, Mobile Or Username').fill('customer@demo.local')
  await page.getByLabel('Password', { exact: true }).fill('DemoPass!2026')
  await page.getByRole('button', { name: 'Sign In Securely' }).click()
  await expect(page.getByRole('link', { name: /Open profile for/ })).toBeVisible()
  await page.getByRole('link', { name: 'Notifications', exact: true }).click()
  await expect(page).toHaveURL(new RegExp('/notifications'))
  await expect(page.getByRole('heading', { name: 'Notifications', exact: true })).toBeVisible()
  await expect(page.locator('.notification-card')).toHaveCount(2)
  for (const mode of ['Light', 'Dark']) {
    await page.getByRole('button', { name: 'Appearance', exact: true }).click()
    const sheet = page.getByRole('dialog', { name: 'Appearance', exact: true })
    await sheet.getByRole('button', { name: mode, exact: true }).click()
    await sheet.getByRole('button', { name: 'Close Appearance' }).click()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    const feed = await page.locator('.notification-feed').boundingBox()
    expect(feed!.width).toBeGreaterThan(340)
    await page.screenshot({ path: info.outputPath('notifications-' + mode.toLowerCase() + '.png') })
  }
  await page
    .getByRole('button', { name: 'Mark Your case has an update as read', exact: true })
    .click()
  await expect(page.locator('.notification-read')).toHaveCount(0)
  expect(reads).toBe(1)
  await page.locator('.notification-card').first().getByRole('button').click()
  await expect(page).toHaveURL(new RegExp('/disputes/' + caseId))
  await page.getByRole('link', { name: /^View original transaction/ }).click()
  await expect(page).toHaveURL(new RegExp('/transactions/' + transactionId))
  await expect(page.locator('.transaction-reference-line')).toContainText('TX-DEMO-0002')
  await expect(
    page.getByRole('button', { name: 'Copy Transaction Reference', exact: true })
  ).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.locator('.back-link').click()
  await expect(page).toHaveURL(new RegExp('/disputes/' + caseId))
})
