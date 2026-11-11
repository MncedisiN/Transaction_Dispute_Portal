import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { expect, test, type Page } from '@playwright/test'

const accountId = '20000000-0000-0000-0000-000000000011'
const transactionId = '30000000-0000-0000-0000-000000000011'
const fullAccountNumber = '1234562599'
const transaction = {
  id: transactionId,
  accountId,
  accountNumber: fullAccountNumber,
  accountType: 'Cheque',
  reference: 'FIXTURE-REVIEW-11',
  transactionAt: '2026-10-07T18:32:00Z',
  description: 'Debit order payment',
  merchantName: 'Metro Grocer Community Market',
  amount: 12077.41,
  currency: 'ZAR',
  type: 'DebitOrder',
  direction: 'Debit',
  status: 'Posted',
  disputeEligible: true,
  hasActiveDispute: false,
  disputeId: null
}

async function fixtures(page: Page) {
  const unexpected: string[] = [],
    mutations: string[] = []
  await page.route('**/api/v1/**', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })

    const request = route.request(),
      path = new URL(request.url()).pathname
    if (path.startsWith('/api/v1/realtime')) return route.abort('blockedbyclient')
    if (request.method() !== 'GET') {
      mutations.push(request.method() + ' ' + path)
      return route.fulfill({ status: 405, json: {} })
    }
    let data: unknown
    if (path === '/api/v1/auth/csrf') data = { requestToken: 'isolated-review-fixture' }
    else if (path === '/api/v1/auth/me')
      data = {
        id: '10000000-0000-0000-0000-000000000011',
        displayName: 'Review Fixture',
        roles: ['customer'],
        permissions: []
      }
    else if (path === '/api/v1/transactions/' + transactionId) data = transaction
    else if (path === '/api/v1/transactions/' + transactionId + '/dispute-check')
      data = { allowed: true, requiresVerification: false, assistedReview: false, message: '' }
    else if (path === '/api/v1/accounts')
      data = [
        {
          id: accountId,
          accountNumber: fullAccountNumber,
          accountType: 'Cheque',
          currency: 'ZAR',
          maskedAccountNumber: '••••2599',
          status: 'Active',
          registeredAt: transaction.transactionAt
        }
      ]
    else if (path === '/api/v1/notifications/unread-count') data = { count: 0 }
    else if (path === '/api/v1/notifications') data = { items: [], page: 1, pageSize: 20, total: 0 }
    else {
      unexpected.push(path)
      return route.fulfill({ status: 404, json: {} })
    }
    return route.fulfill({ json: data })
  })
  return { unexpected, mutations }
}

async function viewport(page: Page) {
  return await page.evaluate(() => ({
    scale: visualViewport?.scale ?? 1,
    width: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    contentWidth: document.querySelector('.content-area')?.clientWidth ?? 0,
    contentScrollWidth: document.querySelector('.content-area')?.scrollWidth ?? 0
  }))
}

async function stableViewport(page: Page, scale: number) {
  const actual = await viewport(page)
  expect(actual.scrollWidth).toBeLessThanOrEqual(actual.width + 1)
  expect(actual.contentScrollWidth).toBeLessThanOrEqual(actual.contentWidth + 1)
  expect(Math.abs(actual.scale - scale)).toBeLessThan(0.01)
}

for (const width of [375, 390])
  for (const theme of ['light', 'dark'] as const)
    test.describe(`dispute focus and review ${width} ${theme}`, () => {
      test.use({
        viewport: { width, height: 844 },
        isMobile: true,
        hasTouch: true,
        deviceScaleFactor: 3,
        colorScheme: theme,
        serviceWorkers: 'block'
      })
      test('keeps focus, review and return navigation within the mobile viewport', async ({
        page
      }, info) => {
        const fixture = await fixtures(page),
          errors: string[] = []
        page.on('pageerror', (error) => errors.push(error.message))
        await page.goto('/transactions/' + transactionId)
        await expect(page.getByRole('heading', { name: 'Transaction', exact: true })).toBeVisible()
        await page.getByRole('link', { name: 'Start A Dispute', exact: true }).click()
        await expect(
          page.getByRole('heading', { name: 'Dispute transaction', exact: true })
        ).toBeVisible()
        const initial = await viewport(page)
        const viewportPolicy = await page.locator('meta[name="viewport"]').getAttribute('content')
        expect(viewportPolicy).not.toMatch(/user-scalable\s*=\s*(?:no|0)|maximum-scale\s*=/i)
        await page.getByRole('radio', { name: 'I was charged more than once', exact: true }).check()
        const details = page.getByLabel('Add A Few Details', { exact: true })
        expect(
          await details.evaluate((element) => parseFloat(getComputedStyle(element).fontSize))
        ).toBeGreaterThanOrEqual(16)
        await details.click()
        const description = 'Please review both payments. Reference ' + 'X'.repeat(96)
        await details.fill(description)
        await stableViewport(page, initial.scale)
        await page.screenshot({
          path: info.outputPath(`focused-${width}-${theme}.png`),
          animations: 'disabled'
        })
        await page.getByRole('button', { name: 'Review Your Dispute', exact: true }).click()
        await expect(page.locator('.review-account')).toContainText('Cheque account')
        await expect(page.locator('.review-account-number')).toHaveText(fullAccountNumber)
        await expect(page.locator('.review-disputed-amount')).toContainText('12 077,41')
        const centered = await page.locator('.review-transaction-heading b').evaluate((element) => {
          const range = document.createRange()
          range.selectNodeContents(element)
          const text = range.getBoundingClientRect(),
            card = element.closest('.review-transaction')!.getBoundingClientRect()
          return Math.abs(text.left + text.width / 2 - card.left - card.width / 2)
        })
        expect(centered).toBeLessThanOrEqual(2)
        const columns = await page
          .locator('.review-transaction-facts > div')
          .evaluateAll((elements) =>
            elements.map((element) => element.getBoundingClientRect().width)
          )
        expect(Math.abs(columns[0]! - columns[1]!)).toBeLessThanOrEqual(1)
        const actions = page.locator('.review-actions'),
          buttons = actions.locator(':scope > button')
        const available = await actions.evaluate((element) => element.clientWidth)
        const first = await buttons.nth(0).boundingBox(),
          second = await buttons.nth(1).boundingBox()
        expect(Math.abs(first!.width - available)).toBeLessThanOrEqual(1)
        expect(Math.abs(second!.width - available)).toBeLessThanOrEqual(1)
        expect(second!.y).toBeGreaterThanOrEqual(first!.y + first!.height + 8)
        await stableViewport(page, initial.scale)
        await page.locator('.review-transaction').scrollIntoViewIfNeeded()
        await page.screenshot({
          path: info.outputPath(`review-${width}-${theme}.png`),
          animations: 'disabled'
        })
        await page.getByRole('button', { name: 'Edit Details', exact: true }).click()
        await expect(details).toHaveValue(description)
        await details.focus()
        await stableViewport(page, initial.scale)
        await page.getByRole('button', { name: 'Review Your Dispute', exact: true }).click()
        await page.getByRole('button', { name: 'Back To Transaction', exact: true }).click()
        await expect(page.getByRole('heading', { name: 'Transaction', exact: true })).toBeVisible()
        await stableViewport(page, initial.scale)
        expect(fixture.unexpected).toEqual([])
        expect(fixture.mutations).toEqual([])
        expect(errors).toEqual([])
      })
    })
