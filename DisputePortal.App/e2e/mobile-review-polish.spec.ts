import { expect, test, type Page, type BrowserContext } from '@playwright/test'
test.use({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block', trace: 'off' })
const caseId = '40000000-0000-0000-0000-000000000001'
async function login(page: Page, email = 'customer@demo.local') {
  await page.goto('/sign-in')
  await page.getByLabel('Email, Mobile Or Username').fill(email)
  await page.getByLabel('Password', { exact: true }).fill('DemoPass!2026')
  await page.getByRole('button', { name: 'Sign In Securely' }).click()
  await expect(page.getByRole('link', { name: /Open profile for/ })).toBeVisible({ timeout: 30000 })
}
async function csrf(context: BrowserContext) {
  return (await (await context.request.get('/api/v1/auth/csrf')).json()).requestToken as string
}
async function fits(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
}
test('mobile profile, history and shared filters stay bounded and readable', async ({
  page
}, info) => {
  test.setTimeout(180000)
  await login(page)
  await page.goto('/profile?section=security')
  await expect(page.locator('.profile-card')).toBeVisible()
  await expect(page.locator('.signin-panel')).toBeVisible()
  const password = await page.locator('.profile-card').boundingBox(),
    signins = await page.locator('.signin-panel').boundingBox()
  expect(signins!.y).toBeGreaterThanOrEqual(password!.y + password!.height + 12)
  for (const field of await page.locator('.profile-card .password-control input').all()) {
    expect((await field.boundingBox())!.height).toBeGreaterThanOrEqual(44)
    expect(await field.evaluate((e) => getComputedStyle(e).fontSize)).toBe('16px')
  }
  await fits(page)
  await page.screenshot({
    path: info.outputPath('profile-security-mobile.png'),
    fullPage: true,
    animations: 'disabled'
  })
  await page.goto('/disputes')
  await page.getByRole('button', { name: 'Filters', exact: true }).click()
  const sheet = page.getByRole('dialog', { name: 'Dispute filters', exact: true })
  const status = await sheet.getByLabel('Case Status').boundingBox(),
    account = await sheet.getByLabel('Dispute Account').boundingBox()
  expect(Math.abs(status!.width - account!.width)).toBeLessThan(2)
  await sheet.getByLabel('From Date').fill('2026-10-01')
  await sheet.getByLabel('To Date').fill('2026-11-02')
  const from = await sheet.getByLabel('From Date').boundingBox(),
    to = await sheet.getByLabel('To Date').boundingBox()
  expect(to!.x >= from!.x + from!.width + 8 || to!.y >= from!.y + from!.height + 8).toBe(true)
  await page.screenshot({
    path: info.outputPath('dispute-filter-mobile.png'),
    fullPage: true,
    animations: 'disabled'
  })
  await sheet.getByRole('button', { name: 'Show disputes' }).click()
  await expect(page).toHaveURL(/from=2026-10-01/)
  // Deliberately long public timeline fixture verifies wrapping; database authorisation is tested separately.
  await page.route('**/api/v1/finance/disputes/' + caseId + '/activity', (route) =>
    route.fulfill({
      json: [
        {
          id: 'layout-edge',
          action: 'CustomerUpdate',
          actor: 'Specialist' + 'x'.repeat(180),
          occurredAt: '2026-10-31T10:12:00Z',
          detail: 'Reviewed ' + 'evidence'.repeat(150)
        }
      ]
    })
  )
  await page.goto('/disputes/' + caseId)
  await expect(page.getByRole('heading', { name: 'Case history', exact: true })).toBeVisible()
  await page.locator('.case-activity').scrollIntoViewIfNeeded()
  await fits(page)
  await page.screenshot({
    path: info.outputPath('case-history-mobile.png'),
    fullPage: true,
    animations: 'disabled'
  })
  await page.getByRole('link', { name: 'Case messages', exact: true }).click()
  await page.getByLabel('Your Message').fill('Please review ' + 'supporting'.repeat(35))
  await page.getByRole('button', { name: 'Send Message' }).click()
  await expect(page.locator('.chat-bubble').last()).toContainText('Please review')
  await expect(page.locator('.chat-transaction-context')).toHaveCount(0)
  await fits(page)
  await page.getByRole('button', { name: 'Back To Dispute', exact: true }).click()
  await expect(page).toHaveURL(new RegExp('/disputes/' + caseId))
  await page.goto('/support')
  await page.getByRole('button', { name: 'Filters', exact: true }).click()
  const messages = page.getByRole('dialog', { name: 'Message filters', exact: true })
  await messages.getByLabel('Message Visibility').selectOption('true')
  await messages.getByLabel('From Date').fill('2026-10-01')
  await messages.getByLabel('To Date').fill('2026-11-02')
  await messages.getByRole('button', { name: 'Show messages' }).click()
  await expect(page).toHaveURL(/unreadOnly=true/)
  await expect(page).toHaveURL(/from=2026-10-01/)
  await fits(page)
})
test('Super Admin can view scanned evidence and use the compact review filters', async ({
  browser,
  baseURL
}, info) => {
  test.setTimeout(180000)
  const owner = await browser.newContext({ baseURL, serviceWorkers: 'block' }),
    staff = await browser.newContext({
      baseURL,
      viewport: { width: 390, height: 844 },
      serviceWorkers: 'block'
    })
  try {
    const customer = await owner.newPage()
    await login(customer)
    const bytes = await customer.evaluate(() => {
      const c = document.createElement('canvas')
      c.width = 32
      c.height = 24
      const x = c.getContext('2d')!
      x.fillStyle = '#005780'
      x.fillRect(0, 0, 32, 24)
      return c.toDataURL('image/png').split(',')[1]!
    })
    const upload = await owner.request.post('/api/v1/disputes/' + caseId + '/evidence', {
      headers: { 'X-CSRF-TOKEN': await csrf(owner) },
      multipart: {
        category: 'IdentityDocument',
        file: {
          name: 'identity-layout.png',
          mimeType: 'image/png',
          buffer: Buffer.from(bytes, 'base64')
        }
      }
    })
    expect(upload.status()).toBe(201)
    const page = await staff.newPage()
    await login(page, 'superadmin@demo.local')
    await expect(page.getByRole('link', { name: 'Audit history', exact: true })).toHaveCount(0)
    await page.goto('/admin/disputes')
    await page.getByRole('button', { name: 'Filters', exact: true }).click()
    const filters = page.getByRole('dialog', { name: 'Dispute filters', exact: true })
    await expect(filters.getByLabel('Case Priority')).toBeVisible()
    await filters.getByLabel('From Date').fill('2026-10-01')
    await filters.getByLabel('To Date').fill('2026-11-02')
    await filters.getByRole('button', { name: 'Show disputes' }).click()
    await expect(page).toHaveURL(/from=2026-10-01/)
    await fits(page)
    await page.goto('/admin/disputes/' + caseId + '?section=evidence')
    const row = page.locator('.evidence-file-row').filter({ hasText: 'identity-layout.png' })
    await expect(row.getByRole('button', { name: 'View', exact: true })).toBeVisible()
    const preview = page.waitForResponse((r) => r.url().endsWith('/preview'))
    await row.getByRole('button', { name: 'View', exact: true }).click()
    expect((await preview).status()).toBe(200)
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.locator('canvas')).toBeVisible()
    await page.screenshot({
      path: info.outputPath('super-admin-evidence-mobile.png'),
      fullPage: true,
      animations: 'disabled'
    })
  } finally {
    await owner.close()
    await staff.close()
  }
})
