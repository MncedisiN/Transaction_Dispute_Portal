import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { test, expect, type Page } from '@playwright/test'
import { disputeInputFixture } from './fixtures/screens'
test.use({ viewport: { width: 375, height: 812 }, serviceWorkers: 'block' })
test.setTimeout(120000)
const now = '2026-10-31T08:00:00Z',
  txId = '30000000-0000-0000-0000-000000000001',
  caseId = '40000000-0000-0000-0000-000000000001'
const transaction = {
  id: txId,
  accountId: 'account',
  accountNumber: '1234566948',
  accountType: 'Cheque',
  transactionAt: now,
  description: 'Monthly subscription',
  merchantName: 'Stream Studio',
  amount: 14104.69,
  currency: 'ZAR',
  type: 'DebitOrder',
  direction: 'Debit',
  status: 'Posted',
  disputeEligible: true,
  hasActiveDispute: false
}
const customer = {
  id: 'customer',
  displayName: 'Test Customer',
  roles: ['customer'],
  permissions: []
}
async function mock(
  page: Page,
  handler?: (path: string, method: string, body: Record<string, unknown>, url: URL) => unknown
) {
  await page.route('**/api/v1/**', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname
    const body = request.method() === 'POST' ? (request.postDataJSON() ?? {}) : {}
    const custom = handler?.(path, request.method(), body, url)
    let data: unknown = custom
    if (custom === undefined) {
      if (path === '/api/v1/dispute-input') data = disputeInputFixture
      else if (path === '/api/v1/platform/settings')
        data = {
          version: 1,
          operatingCountry: 'ZA',
          branding: { name: 'Dispute Portal', accentColor: '#005780', logoColor: '#ed332a' },
          registration: { requireVerification: true },
          currencies: [{ code: 'ZAR', name: 'South African Rand', symbol: 'R', decimals: 2 }],
          countries: [
            {
              code: 'ZA',
              name: 'South Africa',
              flag: 'ZA',
              dialCode: '+27',
              locale: 'en-ZA',
              timeZone: 'Africa/Johannesburg',
              defaultCurrency: 'ZAR',
              currencies: ['ZAR'],
              documents: [],
              regions: [],
              active: true
            }
          ],
          accountTypes: ['Cheque', 'Savings'],
          maximumAccounts: 5
        }
      else if (path.endsWith('/auth/me')) data = customer
      else if (path.endsWith('/auth/csrf')) data = { requestToken: 'fixture' }
      else if (path.endsWith('/notifications')) data = { items: [], total: 0 }
      else if (path === '/api/v1/accounts')
        data = [
          {
            id: 'account',
            accountType: 'Cheque',
            currency: 'ZAR',
            maskedAccountNumber: '••••6948',
            accountNumber: '1234566948',
            status: 'Active',
            registeredAt: now
          }
        ]
      else if (path === '/api/v1/transactions/types')
        data = [{ typeKey: 'DebitOrder', label: 'Debit order' }]
      else if (path === '/api/v1/transactions/' + txId) data = transaction
      else if (path.endsWith('/dispute-check'))
        data = { allowed: true, requiresVerification: false, assistedReview: false, message: '' }
      else if (path === '/api/v1/transactions')
        data = { items: [transaction], page: 1, pageSize: 8, total: 1 }
      else if (path.endsWith('/evidence')) data = []
      else if (path.endsWith('/connection'))
        data = { connected: false, canRefresh: false, lastSyncedAt: null }
      else if (path === '/api/v1/profile')
        data = {
          ...customer,
          firstName: 'Test',
          lastName: 'Customer',
          middleName: null,
          email: 'customer@example.test',
          phone: null,
          registeredAt: now,
          address: null,
          maskedIdentityNumber: null,
          dateOfBirth: null
        }
      else if (path.endsWith('/verification/channels'))
        data = { channels: ['email', 'sms', 'whatsapp'] }
      else data = []
    }
    return route.fulfill({ json: data })
  })
}

test('date range cannot be reversed and HTTP phone submission has a secure request ID', async ({
  page
}, info) => {
  let reads = 0,
    posted: Record<string, unknown> | undefined
  await page.addInitScript(() =>
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true })
  )
  await mock(page, (path, method, body, url) => {
    if (path === '/api/v1/transactions') {
      reads++
      const from = url.searchParams.get('from'),
        to = url.searchParams.get('to')
      expect(!from || !to || from <= to).toBe(true)
      return { items: [transaction], total: 1, page: 1, pageSize: 8 }
    }
    if (path === '/api/v1/disputes' && method === 'POST') {
      posted = body
      return { id: caseId, reference: 'DP-2026-123456' }
    }
  })
  await page.goto('/transactions')
  await page.getByRole('button', { name: 'Filters', exact: true }).click()
  const filters = page.getByRole('dialog', { name: 'Transaction Filters', exact: true })
  const baseline = reads
  await filters.getByLabel('From Date').fill('2026-10-31')
  await filters.getByLabel('To Date').fill('2026-08-05')
  await expect(filters.getByRole('alert')).toContainText('End date must be on or after start date.')
  await expect(filters.getByRole('button', { name: 'Show transactions' })).toBeDisabled()
  expect(reads).toBe(baseline)
  await filters.getByLabel('To Date').fill('2026-10-31')
  const from = await filters.getByLabel('From Date').boundingBox(),
    to = await filters.getByLabel('To Date').boundingBox()
  expect(to!.y - from!.y - from!.height).toBeGreaterThanOrEqual(10)
  await page.screenshot({ path: info.outputPath('date-range.png') })
  await filters.getByRole('button', { name: 'Show transactions' }).click()
  await expect(filters).not.toBeVisible()
  await page.goto('/disputes/new/' + txId)
  await page.getByRole('radio', { name: 'I was charged more than once', exact: true }).check()
  await page.getByLabel('Add A Few Details').fill('This payment appears twice on my statement.')
  await page.getByRole('button', { name: 'Review Your Dispute' }).click()
  await expect(page.locator('.review-transaction')).toContainText('Stream Studio')
  await expect(page.locator('.review-transaction')).not.toContainText('DEMO-')
  await expect(page.locator('.review-transaction')).toContainText('6948')
  let key = ''
  page.on('request', (request) => {
    if (request.url().endsWith('/api/v1/disputes') && request.method() === 'POST')
      key = request.headers()['idempotency-key'] ?? ''
  })
  await page.getByRole('button', { name: 'Submit Dispute', exact: true }).click()
  await expect(page.getByText('DP-2026-123456', { exact: true })).toBeVisible()
  expect(key).toMatch(/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/)
  expect(posted?.transactionId).toBe(txId)
})

test('blocked debit order starts a tracked specialist case instead of a conversation-only request', async ({
  page
}, info) => {
  let posted: Record<string, unknown> | undefined
  await mock(page, (path, method, body) => {
    if (path.endsWith('/dispute-check'))
      return {
        allowed: false,
        requiresVerification: false,
        assistedReview: true,
        message: 'This collection needs specialist review.'
      }
    if (path === '/api/v1/disputes' && method === 'POST') {
      posted = body
      return { id: caseId, reference: 'DP-2026-246810' }
    }
  })
  await page.goto('/disputes/new/' + txId)
  await expect(page.getByRole('link', { name: 'Contact Support', exact: true })).toHaveCount(0)
  const action = page.getByRole('button', { name: 'Request Specialist Review', exact: true })
  await expect(action).toBeVisible()
  const actionBox = await action.boundingBox(),
    contentWidth = await page.locator('.risk-check').evaluate((element) => {
      const style = getComputedStyle(element)
      return element.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight)
    })
  expect(Math.abs(actionBox!.width - contentWidth)).toBeLessThan(2)
  await action.click()
  await page.getByRole('radio', { name: 'I cancelled this debit order', exact: true }).check()
  await page
    .getByLabel('Add A Few Details')
    .fill('Please investigate this collection against my cancelled mandate.')
  await page.getByRole('button', { name: 'Review Your Dispute' }).click()
  await page.getByRole('button', { name: 'Submit Dispute', exact: true }).click()
  await expect(page.getByText('DP-2026-246810', { exact: true })).toBeVisible()
  expect(posted?.transactionId).toBe(txId)
  expect(posted?.requestSpecialistReview).toBe(true)
  await expect(page.getByRole('link', { name: 'Track Your Dispute', exact: true })).toHaveAttribute(
    'href',
    '/disputes/' + caseId
  )
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.screenshot({ path: info.outputPath('specialist-case-received.png') })
})

test('account numbers stay visible and closure follows verified admin review', async ({
  page
}, info) => {
  let administrator = false
  const requests: {
    id: string
    reference: string
    status: string
    version: number
    customerName: string
    email: string
    reason: string
    createdAt: string
  }[] = []
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true })
    document.execCommand = () => true
  })
  await mock(page, (path, method, body) => {
    if (path.endsWith('/auth/me') && administrator)
      return {
        id: 'admin',
        displayName: 'Super Admin',
        roles: ['super_admin'],
        permissions: ['users.manage', 'disputes.read', 'audit.read']
      }
    if (path.endsWith('/accounts/account/number')) return { accountNumber: '1234566948' }
    if (path === '/api/v1/accounts/account')
      return {
        id: 'account',
        accountType: 'Cheque',
        currency: 'ZAR',
        maskedAccountNumber: '••••6948',
        accountNumber: '1234566948',
        status: 'Active',
        registeredAt: now
      }
    if (
      path === '/api/v1/account-management/deletion' ||
      path === '/api/v1/account-management/requests'
    )
      return requests
    if (path.endsWith('/deletion/start')) {
      expect(body.currentPassword).toBe('TestPassword!2026')
      return { id: 'challenge', maskedDestination: 'c••••@example.test', expiresAt: now }
    }
    if (path.endsWith('/deletion/confirm')) {
      expect(body.code).toBe('123456')
      requests.push({
        id: 'request',
        reference: 'AR-2026-1234567',
        status: 'Pending',
        version: 1,
        customerName: 'Test Customer',
        email: 'customer@example.test',
        reason: '',
        createdAt: now
      })
      return { id: 'request' }
    }
    if (path.endsWith('/requests/request/review')) {
      expect(body.expectedVersion).toBe(requests[0]!.version)
      requests[0]!.status = String(body.status)
      requests[0]!.version++
      return {}
    }
  })
  await page.goto('/accounts/account')
  await expect(page.getByRole('button', { name: 'Copy Account Details' })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Show account number' })).toHaveCount(0)
  await expect(page.locator('.account-number-line')).toContainText('1234566948')
  await page.getByRole('button', { name: 'Copy Account Details' }).click()
  await expect(page.getByRole('status')).toContainText('copied')
  await expect(page.getByRole('button', { name: 'Hide account number' })).toHaveCount(0)
  await expect(page.locator('.account-number-line')).toContainText('1234566948')
  await page.goto('/profile?section=security')
  await page.getByRole('button', { name: 'Request Account Closure', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Request Account Closure', exact: true })
  await dialog.getByLabel('Current Password', { exact: true }).fill('TestPassword!2026')
  await dialog.getByRole('button', { name: 'Continue Securely' }).click()
  await dialog.getByLabel('Verification Code', { exact: true }).fill('123456')
  await dialog.getByRole('button', { name: 'Submit Closure Request' }).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.getByText('AR-2026-1234567', { exact: true })).toBeVisible()
  administrator = true
  await page.goto('/admin/account-requests')
  await page.getByRole('button', { name: 'Review Request', exact: true }).click()
  await page.getByLabel('Review Note').fill('Identity and pending cases will be reviewed.')
  await page.getByRole('button', { name: 'Save Decision' }).click()
  await expect(page.locator('.account-request-card .request-status')).toHaveText('Under Review')
  await page.getByRole('button', { name: 'Review Request', exact: true }).click()
  await page
    .getByLabel('Review Note')
    .fill('All linked cases are closed and retention is confirmed.')
  await page.getByRole('button', { name: 'Save Decision' }).click()
  await expect(page.locator('.account-request-card .request-status')).toHaveText('Completed')
  await page.screenshot({ path: info.outputPath('account-requests.png') })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
