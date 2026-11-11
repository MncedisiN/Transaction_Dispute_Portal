import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { test, expect } from '@playwright/test'
test.use({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block', trace: 'off' })
const caseId = '40000000-0000-0000-0000-000000000001',
  tx = '30000000-0000-0000-0000-000000000001',
  credit = '30000000-0000-0000-0000-000000000002',
  now = '2026-11-01T12:00:00Z'
test('customer receipts hide internal references and copy the saved refund reference', async ({
  page
}, info) => {
  const transaction = {
    id: tx,
    accountId: 'account',
    accountNumber: '••••1234',
    reference: 'INTERNAL-BANK-REFERENCE',
    transactionAt: now,
    merchantName: 'Shop',
    description: 'Card payment',
    type: 'CardPurchase',
    typeLabel: 'Card purchase',
    amount: 100,
    currency: 'ZAR',
    direction: 'Debit',
    status: 'Posted',
    disputeEligible: false,
    hasActiveDispute: false,
    disputeId: caseId,
    disputeStatus: 'Resolved'
  }
  await page.route('**/api/v1/**', (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })
    const path = new URL(route.request().url()).pathname
    let data: unknown = { items: [], total: 0 }
    if (path.endsWith('/auth/me'))
      data = { id: 'customer', displayName: 'Alex Morgan', roles: ['customer'], permissions: [] }
    else if (path.endsWith('/auth/csrf')) data = { requestToken: 'fixture' }
    else if (path === '/api/v1/disputes/' + caseId)
      data = {
        dispute: {
          id: caseId,
          reference: 'DP-2026-123456',
          transactionId: tx,
          amount: 100,
          currency: 'ZAR',
          category: 'Other',
          status: 'Resolved',
          version: 4,
          createdAt: now,
          resolution: 'Refund approved.'
        },
        merchantName: 'Shop',
        transactionReference: 'INTERNAL-BANK-REFERENCE',
        transactionAt: now,
        history: [],
        notes: []
      }
    else if (path === '/api/v1/transactions/' + tx) data = transaction
    else if (path === '/api/v1/transactions/' + credit)
      data = {
        ...transaction,
        id: credit,
        type: 'Refund',
        typeLabel: 'Refund',
        reference: 'RF-12345678',
        direction: 'Credit'
      }
    else if (path.includes('/operations/cases/'))
      data = {
        id: caseId,
        status: 'Resolved',
        queueType: 'General',
        assignedName: 'Thandi Dlamini',
        version: 4
      }
    else if (path.endsWith('/activity'))
      data = [
        {
          id: 'stored-event',
          action: 'Resolved',
          actor: 'Thandi Dlamini',
          occurredAt: now,
          detail: null,
          actorIdentity: {
            fullName: 'Thandi Dlamini',
            accountName: 'Thandi Dlamini',
            roles: [],
            kind: 'User',
            provenance: 'Recorded'
          }
        }
      ]
    else if (path === '/api/v1/finance/disputes/' + caseId)
      data = {
        refund: {
          disputeId: caseId,
          creditTransactionId: credit,
          customerReference: 'RF-12345678',
          creditReference: 'INTERNAL-REFUND-REFERENCE',
          amount: 100,
          currency: 'ZAR',
          postedAt: now,
          automatic: false
        },
        decision: null
      }
    return route.fulfill({ json: data })
  })
  await page.goto('/disputes/' + caseId)
  await expect(page.getByRole('heading', { name: 'Case History', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: /original transaction/i })).toHaveCount(0)
  await expect(page.getByText('INTERNAL-BANK-REFERENCE', { exact: true })).toHaveCount(0)
  await expect(page.getByText('INTERNAL-REFUND-REFERENCE', { exact: true })).toHaveCount(0)
  await expect(page.getByText('Personal name not recorded')).toHaveCount(0)
  await expect(
    page.getByRole('button', { name: 'Copy Refund Reference', exact: true })
  ).toBeVisible()
  const refBox = await page.locator('.refund-reference>b').boundingBox(),
    copyBox = await page
      .getByRole('button', { name: 'Copy Refund Reference', exact: true })
      .boundingBox()
  expect(copyBox!.x).toBeGreaterThanOrEqual(refBox!.x + refBox!.width)
  await expect(page.getByText('Simulated posting. No external bank transfer.')).toHaveCount(0)
  await page.getByRole('button', { name: 'Details', exact: true }).click()
  await expect(page.getByText('Transaction reference', { exact: true })).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.evaluate(() => window.scrollTo(0, 0))
  await page.screenshot({
    path: info.outputPath('customer-case-clean.png'),
    fullPage: false,
    animations: 'disabled'
  })
  await page.goto('/transactions/' + tx)
  await expect(page.getByText('Completed', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('INTERNAL-BANK-REFERENCE', { exact: true })).toHaveCount(0)
  await page.goto('/transactions/' + credit)
  await expect(
    page.getByRole('button', { name: 'Copy Refund Reference', exact: true })
  ).toBeVisible()
  await expect(page.getByText('RF-12345678', { exact: true })).toBeVisible()
})
