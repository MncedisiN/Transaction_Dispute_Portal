import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { test, expect } from '@playwright/test'
test.use({ viewport: { width: 375, height: 812 }, serviceWorkers: 'block', trace: 'off' })
test('audit identifies the person and recorded role with compact stored details at phone width', async ({
  page
}, info) => {
  const userId = '11000000-0000-0000-0000-000000000003',
    eventId = '61000000-0000-0000-0000-000000000001',
    caseId = '41000000-0000-0000-0000-000000000001'
  const actor = {
    userId,
    firstName: 'Thandi',
    middleName: null,
    lastName: 'Dlamini',
    fullName: 'Thandi Dlamini',
    accountName: 'Thandi Dlamini',
    roles: ['fraud_agent'],
    kind: 'User',
    provenance: 'Recorded',
    loginIdentifier: 'thandi@example.test',
    label: 'Thandi Dlamini'
  }
  await page.route('**/api/v1/**', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })

    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/auth/csrf'))
      return route.fulfill({ json: { requestToken: 'fixture-csrf' } })
    if (path.endsWith('/auth/me'))
      return route.fulfill({
        json: {
          id: 'admin',
          displayName: 'Supervisor',
          roles: ['super_admin'],
          permissions: ['disputes.read', 'audit.read', 'users.manage']
        }
      })
    if (path.endsWith('/notifications')) return route.fulfill({ json: { items: [], total: 0 } })
    if (path.endsWith('/admin/audit-logs'))
      return route.fulfill({
        json: {
          page: 1,
          pageSize: 20,
          total: 1,
          items: [
            {
              id: eventId,
              source: 'Audit',
              actorName: actor.fullName,
              actorIdentity: actor,
              action: 'DisputeStatusChanged',
              entityType: 'dispute',
              entityId: caseId,
              occurredAt: '2026-11-01T10:15:00Z',
              correlationId: 'stored-correlation',
              safeMetadata: '{}',
              outcome: 'Success'
            }
          ]
        }
      })
    return route.fulfill({ status: 404, json: {} })
  })
  await page.goto('/admin/audit')
  await expect(page.getByRole('heading', { name: 'Audit history', exact: true })).toBeVisible()
  await expect(page.locator('.action-actor')).toContainText('Thandi Dlamini')
  await expect(page.locator('.action-actor')).toContainText('Fraud specialist')
  await page.getByText('Person details', { exact: true }).click()
  await expect(page.getByText(userId, { exact: true })).toBeVisible()
  await expect(page.getByText('thandi@example.test', { exact: true })).toBeVisible()
  await page.getByText('View details', { exact: true }).click()
  await expect(page.getByText(caseId, { exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({
    path: info.outputPath('audit-person-mobile.png'),
    fullPage: true,
    animations: 'disabled'
  })
})
