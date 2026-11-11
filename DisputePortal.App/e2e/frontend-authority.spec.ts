import { expect, test, type Page } from '@playwright/test'
import referenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'

const account = {
  id: 'source',
  userId: 'customer',
  number: '1234560001',
  type: 'Cheque',
  countryCode: 'ZA',
  currency: 'ZAR',
  status: 'Active',
  ledgerBalance: 1000,
  availableBalance: 1000,
  reservedBalance: 0,
  version: 1,
  createdAt: '2026-11-05T10:00:00Z'
}
const target = { ...account, id: 'target', type: 'Savings', number: '1234560002' }
async function mock(
  page: Page,
  options: { unavailable?: boolean; configurationDown?: boolean; unauthenticated?: boolean } = {}
) {
  let unavailable = options.unavailable ?? false
  const reference = structuredClone(referenceFixture)
  reference.statuses.UnderReview.customerLabel = 'Specialist Review'
  const requests: { path: string; body: Record<string, unknown> }[] = []
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    let data: unknown = []
    if (path.endsWith('/portal/reference')) {
      if (unavailable)
        return route.fulfill({ status: 503, json: { title: 'Temporarily unavailable' } })
      data = reference
    } else if (path.endsWith('/platform/status')) data = { enabled: true }
    else if (path.endsWith('/platform/settings')) {
      if (options.configurationDown)
        return route.fulfill({ status: 503, json: { title: 'Unavailable' } })
      data = platformSettings()
    } else if (path.endsWith('/auth/me') && options.unauthenticated)
      return route.fulfill({ status: 401, json: { title: 'Sign In Required' } })
    else if (path.endsWith('/auth/me'))
      data = { id: 'customer', displayName: 'Test Customer', roles: ['customer'], permissions: [] }
    else if (path.endsWith('/auth/csrf')) data = { requestToken: 'test-token' }
    else if (path.endsWith('/platform/accounts')) data = [account, target]
    else if (path.endsWith('/transfer-targets')) data = [target]
    else if (path.endsWith('/payments/quote')) {
      requests.push({ path, body: route.request().postDataJSON() })
      data = {
        id: 'quote-1',
        kind: 'Transfer',
        accountId: 'source',
        recipientAccountId: 'target',
        amount: 10,
        amountExact: '10.00',
        fee: 7.25,
        feeExact: '7.25',
        totalDebitExact: '17.25',
        currency: 'ZAR',
        expiresAt: '2099-01-01T00:00:00Z'
      }
    } else if (path.endsWith('/platform/payments')) {
      requests.push({ path, body: route.request().postDataJSON() })
      data = { status: 'Processing' }
    } else if (path.endsWith('/notifications'))
      data = { items: [], total: 0, page: 1, pageSize: 20 }
    else if (path.endsWith('/support/last-opened')) data = { threadId: null }
    else if (path.endsWith('/disputes'))
      data = {
        items: [
          {
            id: 'case',
            reference: 'DP-2026-123456',
            transactionId: 'payment',
            transactionDescription: 'Card Purchase',
            merchantName: 'Market',
            amount: 10,
            currency: 'ZAR',
            category: 'Other',
            status: 'UnderReview',
            priority: 3,
            version: 1,
            createdAt: '2026-11-05T10:00:00Z'
          }
        ],
        total: 1,
        page: 1,
        pageSize: 20
      }
    return route.fulfill({ json: data })
  })
  return {
    requests,
    recover: () => {
      unavailable = false
    }
  }
}

test('unavailable reference settings keep actions unavailable and can be retried', async ({
  page
}) => {
  const api = await mock(page, { unavailable: true })
  await page.goto('/')
  await expect(
    page.getByText('Portal settings are temporarily unavailable. Please try again.')
  ).toBeVisible()
  await expect(page.getByRole('button', { name: 'Transfer', exact: true })).toHaveCount(0)
  api.recover()
  await page.getByRole('button', { name: 'Try Again', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Transfer', exact: true })).toBeVisible()
})

for (const viewport of [
  { width: 390, height: 844 },
  { width: 1440, height: 1000 }
]) {
  test(`server transfer targets and exact quoted fees at ${viewport.width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize(viewport)
    const api = await mock(page)
    await page.goto('/')
    await page.getByRole('button', { name: 'Transfer', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByLabel('To Account')).toHaveValue('target')
    await expect(dialog.getByLabel('To Account').locator('option')).toHaveCount(2)
    await dialog.getByLabel('Amount (ZAR)').fill('10.00')
    await dialog.getByRole('button', { name: 'Review Transfer', exact: true }).click()
    await expect(dialog).toContainText('17.25 ZAR')
    await page.screenshot({ path: info.outputPath('transfer-review.png'), fullPage: true })
    await dialog.getByRole('button', { name: 'Confirm Transfer', exact: true }).click()
    await expect(dialog).not.toBeVisible()
    expect(api.requests.find((request) => request.path.endsWith('/quote'))?.body).toMatchObject({
      amount: '10.00',
      recipientAccountId: 'target'
    })
    expect(api.requests.find((request) => request.path.endsWith('/payments'))?.body).toMatchObject({
      amount: '10.00',
      quoteId: 'quote-1',
      recipientAccountId: 'target'
    })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  })
}

test('case labels are rendered from API reference data', async ({ page }) => {
  await mock(page)
  await page.goto('/disputes')
  await expect(page.getByText('Specialist Review', { exact: true })).toBeVisible()
  await expect(page.getByText('Card Purchase', { exact: true }).first()).toBeVisible()
})

test('financial service availability does not block secure sign-in', async ({ page }) => {
  await mock(page, { configurationDown: true, unauthenticated: true })
  await page.goto('/sign-in')
  await expect(page.getByRole('button', { name: 'Sign In Securely', exact: true })).toBeVisible()
  await expect(
    page.getByText('Portal settings are temporarily unavailable. Please try again.')
  ).toHaveCount(0)
})

test('sign-in displays the expiry supplied by the API', async ({ page }) => {
  await mock(page, { unauthenticated: true })
  await page.route('**/api/v1/auth/sign-in', (route) =>
    route.fulfill({
      json: {
        challengeId: 'a65e011a-56ed-4585-a37b-a98399ab3a11',
        requiresVerification: true,
        channel: 'email',
        maskedDestination: 'r•••@example.test',
        expiresAt: '2099-01-01T12:13:00Z'
      }
    })
  )
  await page.goto('/sign-in')
  await page.getByLabel('Email Or Mobile Number').fill('reviewer@example.test')
  await page.getByLabel('Password', { exact: true }).fill('Example!Password42')
  await page.getByRole('button', { name: 'Sign In Securely', exact: true }).click()
  await expect(page.getByLabel('Verification Code')).toBeVisible()
  await expect(page.locator('.auth-hint')).toContainText(/14:13|2:13/)
  await expect(page.locator('.auth-hint')).not.toContainText('5 minutes')
})
