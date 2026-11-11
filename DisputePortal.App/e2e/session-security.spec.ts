import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { test, expect, type Page } from '@playwright/test'

test.use({ channel: process.env.E2E_BROWSER_CHANNEL })

test.beforeAll(async ({ request }) => {
  await expect
    .poll(async () => (await request.get('/api/v1/auth/csrf')).status(), { timeout: 30000 })
    .toBe(200)
})

test('sign-in rotates CSRF, mutation validation works, logout revokes the session', async ({
  page,
  playwright,
  baseURL
}) => {
  await page.goto('/sign-in')
  await page.getByLabel('Email, Mobile Or Username').fill('customer@demo.local')
  await page.getByLabel('Password', { exact: true }).fill('DemoPass!2026')
  await page.getByRole('button', { name: 'Sign In Securely' }).click()
  await expect(page.getByRole('link', { name: /Open profile for/ })).toBeVisible({ timeout: 30000 })
  const savedCookies = await page.context().cookies()
  expect(savedCookies.some((cookie) => cookie.name === 'portal.auth')).toBe(true)
  await submitValidationProbe(page)
  await page.goto('/profile')
  const signedOut = page.waitForResponse(
    (response) =>
      response.url().endsWith('/api/v1/auth/sign-out') && response.request().method() === 'POST'
  )
  await page.getByRole('button', { name: 'Sign out', exact: true }).click()
  expect((await signedOut).status()).toBe(204)
  await expect(page).toHaveURL(/\/sign-in/)
  await expect
    .poll(async () =>
      (await page.context().cookies()).some((cookie) => cookie.name === 'portal.auth')
    )
    .toBe(false)
  // Replaying the old cookie must fail, proving server-side session revocation.
  const replay = await playwright.request.newContext({
    baseURL,
    extraHTTPHeaders: {
      Cookie: savedCookies.map((cookie) => cookie.name + '=' + cookie.value).join('; ')
    }
  })
  try {
    expect((await replay.get('/api/v1/auth/me')).status()).toBe(401)
  } finally {
    await replay.dispose()
  }
})

test('API rejects anonymous and missing tokens after sign-in and the client recovers a stale token', async ({
  page,
  request
}) => {
  const anonymousToken = (await (await request.get('/api/v1/auth/csrf')).json()).requestToken
  expect(
    (
      await request.post('/api/v1/auth/sign-in', {
        headers: { 'X-CSRF-TOKEN': anonymousToken },
        data: { loginIdentifier: 'customer@demo.local', password: 'DemoPass!2026' }
      })
    ).status()
  ).toBe(200)
  for (const headers of [{}, { 'X-CSRF-TOKEN': anonymousToken }]) {
    const denied = await request.post('/api/v1/disputes', { headers, data: {} })
    expect(denied.status()).toBe(400)
    expect((await denied.json()).code).toBe('csrf_validation_failed')
  }
  const currentToken = (await (await request.get('/api/v1/auth/csrf')).json()).requestToken
  expect(
    (
      await request.post('/api/v1/auth/sign-out', { headers: { 'X-CSRF-TOKEN': currentToken } })
    ).status()
  ).toBe(204)
  await page.goto('/sign-in')
  await page.getByLabel('Email, Mobile Or Username').fill('customer@demo.local')
  await page.getByLabel('Password', { exact: true }).fill('DemoPass!2026')
  await page.getByRole('button', { name: 'Sign In Securely' }).click()
  await expect(page.getByRole('link', { name: /Open profile for/ })).toBeVisible({ timeout: 30000 })
  await submitValidationProbe(page, true)
  await page.goto('/profile')
  await page.getByRole('button', { name: 'Sign out', exact: true }).click()
  await expect(page).toHaveURL(/\/sign-in/)
})

// Exercise the real form without persisting a synthetic dispute. Only the request
// body is replaced; token handling, API validation and logout remain real.
async function submitValidationProbe(page: Page, staleFirstToken = false) {
  const keys: string[] = []
  let attempts = 0
  await page.route('**/api/v1/disputes', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })

    if (route.request().method() !== 'POST') return route.continue()
    attempts++
    const headers = { ...route.request().headers() }
    keys.push(headers['idempotency-key'])
    if (staleFirstToken && attempts === 1) headers['x-csrf-token'] = 'invalid-regression-token'
    await route.continue({ headers, postData: '{}' })
  })
  // Keep this validation-only probe independent of the refund and camera cases.
  await page.goto('/disputes/new/30000000-0000-0000-0000-000000000014')
  await page.getByRole('radio', { name: 'I don’t recognise this transaction', exact: true }).check()
  await page.getByLabel('Add A Few Details').fill('Security regression check using synthetic data.')
  await page.getByRole('button', { name: 'Review Your Dispute' }).click()
  const validated = page.waitForResponse(
    async (response) =>
      response.url().endsWith('/api/v1/disputes') &&
      response.request().method() === 'POST' &&
      Boolean((await response.json()).errors)
  )
  await page.getByRole('button', { name: 'Submit Dispute' }).click()
  const validation = await validated
  expect(validation.status()).toBe(400)
  expect(Object.keys((await validation.json()).errors).length).toBeGreaterThan(0)
  expect(attempts).toBe(staleFirstToken ? 2 : 1)
  expect(keys[0]).toBeTruthy()
  if (staleFirstToken) expect(keys[1]).toBe(keys[0])
}
