import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { expect, test } from '@playwright/test'
test.use({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' })
test('case queue stays usable when optional workload reads are rate limited', async ({ page }) => {
  let available = false
  const now = new Date().toISOString(),
    actor = 'staff',
    id = 'case'
  const dispute = {
    id,
    reference: 'DP-2026-998811',
    transactionId: 'transaction',
    transactionDescription: 'Card payment',
    amount: 250,
    currency: 'ZAR',
    category: 'Other',
    status: 'Submitted',
    priority: 3,
    version: 1,
    createdAt: now
  }
  const operations = {
    id,
    reference: dispute.reference,
    status: 'Submitted',
    queueType: 'Card',
    severity: 'Medium',
    priority: 3,
    version: 1,
    assignedTo: null,
    assignedName: null,
    dueAt: new Date(Date.now() + 86400000).toISOString(),
    escalationLevel: 0
  }
  await page.route('**/api/v1/**', (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })
    const path = new URL(route.request().url()).pathname
    let data: unknown = { items: [], total: 0 }
    if (path.endsWith('/auth/me'))
      data = {
        id: actor,
        displayName: 'Operations Manager',
        roles: ['operations_manager'],
        permissions: ['disputes.read', 'disputes.manage', 'operations.manage']
      }
    else if (path.endsWith('/auth/csrf')) data = { requestToken: 'test' }
    else if (path === '/api/v1/admin/disputes')
      data = {
        items: [
          {
            dispute,
            operations,
            customerName: 'Test Customer',
            maskedEmail: 't***@example.test',
            accountNumber: '••••1234',
            transactionReference: 'BANK-001',
            merchantName: 'Card merchant',
            transactionAt: now
          }
        ],
        page: 1,
        pageSize: 20,
        total: 1
      }
    else if (path === '/api/v1/admin/dashboard') {
      if (!available) return route.fulfill({ status: 503, json: { title: 'Unavailable' } })
      data = {
        submitted: 1,
        underReview: 0,
        awaitingCustomer: 0,
        resolvedToday: 0,
        highPriority: 0
      }
    } else if (path === '/api/v1/operations/staff') {
      if (!available) return route.fulfill({ status: 429, json: { title: 'Try shortly' } })
      data = [
        {
          id: actor,
          displayName: 'Operations Manager',
          roles: ['operations_manager'],
          availability: 'Available',
          capacity: 5,
          activeCases: 1
        }
      ]
    } else if (path.includes('/operations/cases/')) data = operations
    return route.fulfill({ json: data })
  })
  await page.goto('/admin/disputes')
  await expect(page.locator('.case-management-card')).toContainText(dispute.reference)
  await expect(page.getByRole('link', { name: 'Manage case', exact: true })).toBeVisible()
  await expect(page.locator('.queue-service-notice')).toContainText('Workload details')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  available = true
  await page.getByRole('button', { name: 'Retry Workload Details', exact: true }).click()
  await expect(page.locator('.queue-service-notice')).toHaveCount(0)
  await expect(page.locator('.case-management-card')).toContainText(dispute.reference)
  await expect(page.locator('.case-workload')).toBeVisible()
})
