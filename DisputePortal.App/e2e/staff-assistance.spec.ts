import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { expect, test } from '@playwright/test'
test.use({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' })
test('staff can find a customer transaction and create an attributed specialist case', async ({
  page
}, info) => {
  test.setTimeout(120000)
  await page.goto('/sign-in')
  await page.getByLabel('Email, Mobile Or Username').fill('superadmin@demo.local')
  await page.getByLabel('Password', { exact: true }).fill('DemoPass!2026')
  await page.getByRole('button', { name: 'Sign In Securely' }).click()
  await expect(page.getByRole('link', { name: /Open profile for/ })).toBeVisible()
  const response = await page.request.get('/api/v1/admin/transactions?page=1&pageSize=100')
  expect(response.status()).toBe(200)
  const all = await response.json()
  const item = all.items.find(
    (item: { disputeEligible: boolean; disputeId: string | null; type: string }) =>
      item.disputeEligible && !item.disputeId && item.type === 'CardPurchase'
  )
  expect(item).toBeTruthy()
  await page.goto('/admin/transactions')
  await expect(
    page.getByRole('heading', { name: 'Customer transactions', exact: true })
  ).toBeVisible()
  await page.getByLabel('Search Customer Transactions').fill(item.reference)
  await page.getByRole('button', { name: 'Search Transactions', exact: true }).click()
  await page.locator('.staff-transaction-row').first().click()
  await expect(page.locator('.assistance-details')).toContainText(item.customerName)
  await expect(page.locator('.assistance-details')).toContainText(item.id)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({ path: info.outputPath('staff-customer-mobile.png'), fullPage: true })
  await page.getByRole('button', { name: 'Create dispute for customer', exact: true }).click()
  await page.getByRole('radio', { name: 'I was charged more than once', exact: true }).check()
  await page
    .getByLabel('Add A Few Details')
    .fill(
      'The customer reported a duplicate card collection. Please investigate the merchant records.'
    )
  await page.getByRole('button', { name: 'Review Your Dispute' }).click()
  await page.getByRole('button', { name: 'Submit Dispute', exact: true }).click()
  await expect(page).toHaveURL(new RegExp('/admin/disputes/'))
  await expect(page.locator('.assistance-details')).toContainText(item.reference)
  const id = new URL(page.url()).pathname.split('/').pop()
  const created = await (await page.request.get('/api/v1/admin/disputes/' + id)).json()
  expect(created.dispute.transactionId).toBe(item.id)
  expect(
    created.history.some(
      (event: { comment: string; actorName: string }) =>
        event.comment?.includes('on behalf') && event.actorName
    )
  ).toBe(true)
  await page.goto('/admin/disputes')
  await expect(page.getByRole('heading', { name: 'Case management', exact: true })).toBeVisible()
  await expect(
    page.getByRole('button', { name: 'Case status: Active cases', exact: true })
  ).toBeVisible()
  await expect(page.locator('.case-management-card').first()).toBeVisible()
  const selector = await page
      .getByRole('button', { name: 'Case status: Active cases', exact: true })
      .boundingBox(),
    workspace = await page.locator('.case-management-workspace').boundingBox()
  expect(Math.abs(selector!.width - workspace!.width)).toBeLessThan(2)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({ path: info.outputPath('case-management-mobile.png'), fullPage: false })
})
test('completed cases appear as history without active assignment actions', async ({
  page
}, info) => {
  const now = new Date().toISOString()
  const dispute = {
    id: 'closed',
    reference: 'DP-2026-123456',
    transactionId: 'tx',
    transactionDescription: 'Completed collection',
    amount: 500,
    currency: 'ZAR',
    category: 'Other',
    status: 'Resolved',
    priority: 3,
    version: 2,
    createdAt: now
  }
  const operations = {
    ...dispute,
    queueType: 'Card',
    severity: 'Medium',
    assignedTo: 'staff',
    assignedName: 'Case Specialist',
    dueAt: now,
    slaClosedAt: now,
    escalationLevel: 0
  }
  await page.route('**/api/v1/**', (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })
    const path = new URL(route.request().url()).pathname
    const data = path.endsWith('/auth/csrf')
      ? { requestToken: 'test' }
      : path.endsWith('/auth/me')
        ? {
            id: 'staff',
            displayName: 'Operations Manager',
            roles: ['operations_manager'],
            permissions: ['disputes.read', 'disputes.manage', 'operations.manage']
          }
        : path === '/api/v1/admin/disputes'
          ? {
              items: [
                { dispute, operations, customerName: 'Client Name', merchantName: 'Merchant' }
              ],
              page: 1,
              pageSize: 20,
              total: 1
            }
          : path.endsWith('/staff')
            ? []
            : path.endsWith('/dashboard')
              ? {
                  submitted: 0,
                  underReview: 0,
                  awaitingCustomer: 0,
                  resolvedToday: 1,
                  highPriority: 0
                }
              : { items: [], total: 0 }
    return route.fulfill({ json: data })
  })
  await page.goto('/admin/disputes?status=closed')
  await expect(
    page.getByRole('button', { name: 'Case status: Completed cases', exact: true })
  ).toBeVisible()
  await expect(page.locator('.case-management-card')).toContainText('Completed')
  await expect(page.getByRole('link', { name: 'Resolve', exact: true })).toHaveCount(0)
  await expect(page.locator('.case-management-actions')).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({ path: info.outputPath('completed-case-mobile.png'), fullPage: true })
})
