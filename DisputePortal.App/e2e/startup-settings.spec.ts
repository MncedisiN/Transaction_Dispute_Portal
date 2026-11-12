import { expect, test } from '@playwright/test'
import reference from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'

for (const destination of ['/', '/disputes/case'])
  test(`signed-out startup loads settings after the session check at ${destination}`, async ({
    page
  }) => {
    const events: string[] = []
    await page.routeWebSocket('**/api/v1/realtime**', (socket) => socket.close())
    await page.route('**/api/**', async (route) => {
      const path = new URL(route.request().url()).pathname
      if (path === '/api/v1/auth/csrf')
        return route.fulfill({ json: { requestToken: 'startup-fixture-only' } })
      if (path === '/api/v1/auth/me') {
        events.push('session')
        return route.fulfill({ status: 401, json: { title: 'Sign In Required' } })
      }
      if (path === '/api/v1/portal/reference') {
        events.push('reference')
        return route.fulfill({ json: reference })
      }
      if (path === '/api/v1/platform/settings') return route.fulfill({ json: platformSettings() })
      if (path === '/api/v1/platform/status') return route.fulfill({ json: { enabled: true } })
      return route.fulfill({ json: [] })
    })
    await page.goto(destination)
    await expect(page).toHaveURL(/\/sign-in\?redirect=/)
    await expect(page.getByRole('button', { name: 'Sign In Securely', exact: true })).toBeVisible()
    await expect(
      page.getByText('Portal settings are temporarily unavailable. Please try again.')
    ).toHaveCount(0)
    expect(events.indexOf('session')).toBeGreaterThanOrEqual(0)
    expect(events.indexOf('reference')).toBeGreaterThan(events.indexOf('session'))
  })

for (const failure of ['unavailable', 'malformed'])
  test(`sign-in remains available when public settings are ${failure}`, async ({ page }) => {
    await page.route('**/api/**', (route) => {
      const path = new URL(route.request().url()).pathname
      if (path === '/api/v1/auth/csrf')
        return route.fulfill({ json: { requestToken: 'isolated-startup-token' } })
      if (path === '/api/v1/auth/me')
        return route.fulfill({ status: 401, json: { title: 'Sign In Required' } })
      if (path === '/api/v1/portal/reference')
        return failure === 'unavailable'
          ? route.fulfill({ status: 503, json: { title: 'Unavailable' } })
          : route.fulfill({ json: {} })
      if (path === '/api/v1/platform/status') return route.fulfill({ json: { enabled: true } })
      return route.fulfill({ json: [] })
    })
    await page.goto('/sign-in')
    await expect(page.getByLabel('Email Or Mobile Number')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign In Securely', exact: true })).toBeEnabled()
    await expect(page.getByRole('button', { name: 'Try Again', exact: true })).toHaveCount(0)
  })

test('public settings carry no session cookies or private workspace headers', async ({ page }) => {
  await page
    .context()
    .addCookies([{ name: 'portal_cookie_fixture', value: 'private', url: 'http://127.0.0.1:5174' }])
  let referenceHeaders: Record<string, string> | undefined
  await page.route('**/api/**', (route) => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/v1/auth/csrf')
      return route.fulfill({ json: { requestToken: 'isolated-startup-token' } })
    if (path === '/api/v1/auth/me')
      return route.fulfill({ status: 401, json: { title: 'Sign In Required' } })
    if (path === '/api/v1/portal/reference') {
      referenceHeaders = route.request().headers()
      return route.fulfill({ json: reference })
    }
    if (path === '/api/v1/platform/status') return route.fulfill({ json: { enabled: true } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/sign-in')
  await expect.poll(() => referenceHeaders).toBeDefined()
  expect(referenceHeaders?.cookie).toBeUndefined()
  expect(referenceHeaders?.['x-portal-workspace']).toBeUndefined()
  expect(referenceHeaders?.['x-portal-activity']).toBeUndefined()
})
