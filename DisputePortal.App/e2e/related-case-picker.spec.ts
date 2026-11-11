import { fulfillJson } from './fixtures/fulfillJson'
import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { test, expect } from '@playwright/test'

test.use({ viewport: { width: 375, height: 812 }, serviceWorkers: 'block' })
test.setTimeout(120000)

test('related case picker pages recent cases and searches an older reference on a phone', async ({
  page
}, info) => {
  const recent = {
    id: '40000000-0000-0000-0000-000000000001',
    reference: 'DP-2026-RECENT',
    status: 'Submitted',
    transactionDescription: 'Recent payment',
    merchantName: 'Recent Shop',
    createdAt: '2026-11-01T10:00:00Z'
  }
  const next = {
    ...recent,
    id: '40000000-0000-0000-0000-000000000002',
    reference: 'DP-2026-NEXT',
    merchantName: 'Another recent shop'
  }
  const older = {
    ...recent,
    id: '40000000-0000-0000-0000-000000000003',
    reference: 'DP-2024-OLDER',
    merchantName: 'Older Shop',
    status: 'Declined',
    createdAt: '2024-06-01T10:00:00Z'
  }
  const lookups: { page: number; reference: string; pageSize: number }[] = []
  let bulkDisputes = 0
  await page.route('**/api/v1/**', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })

    const url = new URL(route.request().url()),
      path = url.pathname
    let data: unknown = []
    if (path === '/api/v1/auth/me')
      data = { id: 'customer', displayName: 'Test Customer', roles: ['customer'], permissions: [] }
    else if (path === '/api/v1/support/concerns')
      data = [
        {
          publicId: 'concern',
          code: 'CaseQuestion',
          label: 'Follow Up On A Case',
          requiresCase: true,
          contextKind: 'Case'
        }
      ]
    else if (path === '/api/v1/auth/csrf') data = { requestToken: 'fixture' }
    else if (path === '/api/v1/notifications') data = { items: [], page: 1, pageSize: 20, total: 0 }
    else if (path === '/api/v1/support/related-cases') {
      const reference = url.searchParams.get('reference') ?? '',
        requestedPage = Number(url.searchParams.get('page') ?? 1),
        pageSize = Number(url.searchParams.get('pageSize') ?? 20)
      lookups.push({ page: requestedPage, reference, pageSize })
      data = {
        items: reference ? [older] : requestedPage === 1 ? [recent] : [next],
        page: requestedPage,
        pageSize: 20,
        total: reference ? 1 : 21
      }
    } else if (path === '/api/v1/disputes') {
      bulkDisputes++
      data = { items: [], page: 1, pageSize: 20, total: 0 }
    } else if (path === '/api/v1/operations/cases/' + older.id)
      data = { id: older.id, status: 'Declined', assignedTo: null }
    await fulfillJson(route, data)
  })
  await page.goto('/support?new=1')
  await page.getByRole('button', { name: 'Follow Up On A Case', exact: true }).click()
  const picker = page.getByRole('region', { name: 'Select Related Case', exact: true })
  await expect(picker).toBeVisible()
  await expect(picker.getByRole('button', { name: new RegExp(recent.reference) })).toBeVisible()
  await expect(picker.getByText(older.reference, { exact: true })).toHaveCount(0)
  expect(lookups[0]).toEqual({ page: 1, reference: '', pageSize: 20 })
  expect(bulkDisputes).toBe(0)
  await picker.getByRole('button', { name: 'Load More', exact: true }).click()
  await expect(picker.getByRole('button', { name: new RegExp(next.reference) })).toBeVisible()
  expect(lookups.at(-1)?.page).toBe(2)
  const box = await picker.boundingBox()
  expect(box!.x).toBeGreaterThanOrEqual(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(375)
  await page.screenshot({ path: info.outputPath('recent-related-cases.png') })
  await picker.getByRole('searchbox', { name: 'Find A Case', exact: true }).fill('DP-2024-OLDER')
  await picker.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(picker.getByRole('button', { name: new RegExp(older.reference) })).toBeVisible()
  expect(lookups.at(-1)).toEqual({ page: 1, reference: 'DP-2024-OLDER', pageSize: 20 })
  await page.screenshot({ path: info.outputPath('older-reference-search.png') })
  await picker.getByRole('button', { name: new RegExp(older.reference) }).click()
  await expect(picker).not.toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Your Message', exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  expect(bulkDisputes).toBe(0)
})
