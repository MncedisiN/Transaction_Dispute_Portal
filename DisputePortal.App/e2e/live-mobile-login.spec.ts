import { expect, test, devices } from '@playwright/test'
import { readFileSync } from 'node:fs'

const android = process.env.E2E_MOBILE_ENGINE === 'android'
test.skip(!process.env.E2E_MOBILE_LOGIN_FIXTURE, 'Requires a synthetic live customer fixture')
test.use({
  ...devices[android ? 'Pixel 7' : 'iPhone 13'],
  browserName: android ? 'chromium' : 'webkit',
  channel: android ? 'chrome' : undefined,
  serviceWorkers: 'allow',
  ignoreHTTPSErrors: false
})
test(`${android ? 'Android Chromium' : 'iPhone WebKit'} real login, refresh, sign-out and protected-page redirect`, async ({
  page,
  context
}, info) => {
  test.setTimeout(180000)
  const fixture = JSON.parse(readFileSync(process.env.E2E_MOBILE_LOGIN_FIXTURE!, 'utf8')) as {
    id: string
    login: string
    password: string
  }
  await page.goto('/sign-in')
  await page.waitForFunction(() => Boolean(navigator.serviceWorker.controller), null, { timeout: 45000 })
  await page.getByLabel('Email Or Mobile Number').fill(fixture.login)
  await page.getByLabel('Password', { exact: true }).fill('Incorrect-fixture-password!42')
  await page.getByRole('button', { name: 'Sign In Securely', exact: true }).click()
  await expect(
    page.locator('form').getByText('Check your sign-in details and try again.')
  ).toBeVisible({ timeout: 30000 })
  await page.getByLabel('Password', { exact: true }).fill(fixture.password)
  await page.getByRole('button', { name: 'Sign In Securely', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Profile Menu', exact: true })).toBeVisible({
    timeout: 45000
  })
  const identity = await page.evaluate(async () => {
    const response = await fetch('/api/v1/auth/me', {
      credentials: 'same-origin',
      cache: 'no-store'
    })
    return { status: response.status, user: response.ok ? await response.json() : null }
  })
  expect(identity.status).toBe(200)
  expect(identity.user.id.toLowerCase()).toBe(fixture.id.toLowerCase())
  const cookie = (await context.cookies()).find((cookie) => cookie.name === 'portal.auth')
  expect(cookie?.httpOnly).toBe(true)
  expect(cookie?.secure).toBe(true)
  expect(cookie?.sameSite).toBe('Lax')
  await page.reload()
  await expect(page.getByRole('button', { name: 'Profile Menu', exact: true })).toBeVisible({
    timeout: 45000
  })
  await page.screenshot({ path: info.outputPath('mobile-authenticated.png'), fullPage: true })
  await page.goto('/profile')
  await page.getByRole('button', { name: 'Sign Out', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Sign In Securely', exact: true })).toBeVisible({
    timeout: 30000
  })
  expect(
    await page.evaluate(
      async () =>
        (await fetch('/api/v1/auth/me', { credentials: 'same-origin', cache: 'no-store' })).status
    )
  ).toBe(401)
  await page.goto('/disputes')
  await expect(page).toHaveURL(/\/sign-in\?redirect=/)
  await expect(page.getByRole('button', { name: 'Sign In Securely', exact: true })).toBeVisible()
})
