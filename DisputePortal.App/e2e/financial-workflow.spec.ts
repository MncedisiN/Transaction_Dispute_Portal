import { expect, test, type Page, type BrowserContext } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { writeFile } from 'node:fs/promises'
import { createCanvas } from '@napi-rs/canvas'
// Evidence bodies and password entries must not be persisted in browser trace archives.
test.use({ trace: 'off' })
async function signIn(page: Page, email: string, password: string) {
  await page.goto('/sign-in')
  await page.getByLabel('Email, Mobile Or Username').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign In Securely' }).click()
  await expect(page.getByRole('link', { name: /Open profile for/ })).toBeVisible({ timeout: 30000 })
}
async function token(context: BrowserContext) {
  return (await (await context.request.get('/api/v1/auth/csrf')).json()).requestToken as string
}

test('profile password change verifies the current hash and revokes every device', async ({
  browser,
  baseURL
}) => {
  const first = await browser.newContext({
      baseURL,
      viewport: { width: 390, height: 844 },
      serviceWorkers: 'block'
    }),
    second = await browser.newContext({ baseURL, serviceWorkers: 'block' })
  try {
    const page = await first.newPage(),
      other = await second.newPage()
    await signIn(page, 'other@demo.local', 'OtherPass!2026')
    await signIn(other, 'other@demo.local', 'OtherPass!2026')
    const csrf = await token(first),
      replacement = 'N7!qL4sZ9uT2vC'
    const same = await first.request.post('/api/v1/profile/password', {
      headers: { 'X-CSRF-TOKEN': csrf },
      data: {
        currentPassword: 'OtherPass!2026',
        newPassword: 'OtherPass!2026',
        confirmPassword: 'OtherPass!2026'
      }
    })
    expect(same.status()).toBe(422)
    const wrong = await first.request.post('/api/v1/profile/password', {
      headers: { 'X-CSRF-TOKEN': csrf },
      data: {
        currentPassword: 'Incorrect!2026',
        newPassword: replacement,
        confirmPassword: replacement
      }
    })
    expect(wrong.status()).toBe(422)
    expect((await second.request.get('/api/v1/auth/me')).status()).toBe(200)
    await page.goto('/profile?section=security')
    const form = page
      .locator('.profile-card')
      .filter({ has: page.getByRole('heading', { name: 'Change password', exact: true }) })
    await form.getByLabel('Current Password', { exact: true }).fill('OtherPass!2026')
    await form.getByLabel('New Password', { exact: true }).fill(replacement)
    await form.getByLabel('Confirm New Password', { exact: true }).fill(replacement + 'x')
    await form.getByRole('button', { name: 'Update Password', exact: true }).click()
    await expect(form.getByRole('alert')).toHaveText('Passwords must match.')
    await form
      .locator('.password-control')
      .nth(1)
      .getByRole('button', { name: 'Show password', exact: true })
      .click()
    await expect(form.getByLabel('New Password', { exact: true })).toHaveAttribute('type', 'text')
    await expect(form.getByLabel('Current Password', { exact: true })).toHaveAttribute(
      'type',
      'password'
    )
    await expect(form.getByLabel('Confirm New Password', { exact: true })).toHaveAttribute(
      'type',
      'password'
    )
    await form.getByLabel('Confirm New Password', { exact: true }).fill(replacement)
    const updated = page.waitForResponse(
      (response) =>
        response.url().endsWith('/profile/password') && response.request().method() === 'POST'
    )
    await form.getByRole('button', { name: 'Update Password', exact: true }).click()
    expect((await updated).status()).toBe(204)
    await expect(page).toHaveURL(/\/sign-in\?reset=1/)
    expect((await second.request.get('/api/v1/auth/me')).status()).toBe(401)
    expect((await first.request.get('/api/v1/auth/me')).status()).toBe(401)
    await signIn(page, 'other@demo.local', replacement)
  } finally {
    await first.close()
    await second.close()
  }
})

test('mobile balances and one duplicate-safe financial refund stay consistent', async ({
  browser,
  baseURL
}, testInfo) => {
  test.setTimeout(360000)
  const customer = await browser.newContext({
      baseURL,
      viewport: { width: 390, height: 844 },
      serviceWorkers: 'block'
    }),
    staff = await browser.newContext({
      baseURL,
      viewport: { width: 1440, height: 1000 },
      serviceWorkers: 'block'
    })
  try {
    const page = await customer.newPage(),
      review = await staff.newPage()
    await signIn(page, 'customer@demo.local', 'DemoPass!2026')
    await signIn(review, 'manager@demo.local', 'DemoPass!2026')
    const customerToken = await token(customer),
      staffToken = await token(staff)
    await page.goto('/transactions')
    await page.getByRole('link', { name: 'Dispute overview', exact: true }).click()
    await expect(page).toHaveURL(baseURL + '/')
    await expect(page.locator('.balance-panel')).toContainText('Main Balance')
    await expect(page.locator('.home-account-balance')).toHaveCount(2)
    await expect(page.locator('.balance-panel')).toHaveAttribute('aria-busy', 'false')
    await page.screenshot({
      path: testInfo.outputPath('overview-mobile-light.png'),
      fullPage: true,
      animations: 'disabled'
    })
    const before = (await (await customer.request.get('/api/v1/finance/balances')).json()) as {
      accountId: string
      currentBalance: number
      balanceKnown: boolean
    }[]
    const transaction = '30000000-0000-0000-0000-000000000003',
      account = '20000000-0000-0000-0000-000000000001'
    await page.goto('/accounts/' + account)
    await expect(page.locator('.balance-panel')).toContainText('Available')
    await expect(page.locator('.balance-panel')).toHaveAttribute('aria-busy', 'false')
    const initialBalance = await page.locator('.balance-value').innerText()
    const created = await customer.request.post('/api/v1/disputes', {
      headers: {
        'X-CSRF-TOKEN': customerToken,
        'Idempotency-Key': 'financial-e2e-' + randomUUID()
      },
      data: {
        transactionId: transaction,
        category: 'IncorrectAmount',
        reason: 'The amount is incorrect',
        description: 'My receipt supports a refund for this local assessment payment.'
      }
    })
    expect(created.status()).toBe(201)
    const dispute = (await created.json()) as { id: string; reference: string }
    await review.goto('/admin/disputes')
    await review
      .locator('.case-management-card')
      .filter({ hasText: dispute.reference })
      .getByRole('link', { name: 'Manage case', exact: true })
      .click()
    await expect(review).toHaveURL(new RegExp('/admin/disputes/' + dispute.id))
    await expect(review.getByRole('button', { name: 'Overview', exact: true })).toHaveAttribute(
      'aria-pressed',
      'true'
    )
    const current = await (await staff.request.get('/api/v1/admin/disputes/' + dispute.id)).json()
    const transition = await staff.request.post(
      '/api/v1/admin/disputes/' + dispute.id + '/transitions',
      {
        headers: { 'X-CSRF-TOKEN': staffToken },
        data: {
          expectedVersion: current.dispute.version,
          status: 'UnderReview',
          comment: 'Original payment and receipt are being checked.'
        }
      }
    )
    expect(transition.status()).toBe(200)
    await review.reload()
    await review.getByRole('button', { name: 'Approve Refund', exact: true }).click()
    await review
      .getByLabel('Approval Reason')
      .fill('Receipt verified. Approve the full eligible original payment.')
    const posted = review.waitForResponse(
      (r) => r.url().endsWith('/refund') && r.request().method() === 'POST'
    )
    await review.getByRole('button', { name: 'Confirm Refund' }).click()
    const postedResponse = await posted
    expect(postedResponse.status()).toBe(200)
    const intent = postedResponse.request().postDataJSON(),
      refund = await postedResponse.json()
    expect(refund.amount).toBe(850)
    await expect(review.locator('.financial-outcome')).toContainText('Refund approved')
    const replay = await staff.request.post('/api/v1/finance/disputes/' + dispute.id + '/refund', {
      headers: { 'X-CSRF-TOKEN': staffToken },
      data: intent
    })
    expect(replay.status()).toBe(200)
    expect((await replay.json()).creditTransactionId).toBe(refund.creditTransactionId)
    const after = (await (
      await customer.request.get('/api/v1/finance/balances')
    ).json()) as typeof before
    expect(after.find((x) => x.accountId === account)!.currentBalance).toBe(
      before.find((x) => x.accountId === account)!.currentBalance + 850
    )
    const financial = await (
      await customer.request.get('/api/v1/finance/disputes/' + dispute.id)
    ).json()
    expect(financial.refund.creditTransactionId).toBe(refund.creditTransactionId)
    await expect(page.locator('.balance-value')).not.toHaveText(initialBalance, { timeout: 35000 })
    await expect(page.locator('.balance-panel')).not.toContainText('Awaiting settlement')
    await page.screenshot({
      path: testInfo.outputPath('account-balance-mobile-light.png'),
      fullPage: true,
      animations: 'disabled'
    })
    await page.getByTitle('Light, dark or system appearance').click()
    await page
      .getByRole('dialog', { name: 'Appearance', exact: true })
      .getByRole('button', { name: 'Dark', exact: true })
      .click()
    await page.getByRole('button', { name: 'Close Appearance', exact: true }).click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
    await page.screenshot({
      path: testInfo.outputPath('account-balance-mobile-dark.png'),
      fullPage: true,
      animations: 'disabled'
    })
  } finally {
    await customer.close()
    await staff.close()
  }
})

test('large camera originals retain exact bytes and linked case messages', async ({
  browser,
  baseURL
}, testInfo) => {
  test.setTimeout(360000)
  const customer = await browser.newContext({
      baseURL,
      viewport: { width: 390, height: 844 },
      serviceWorkers: 'block'
    }),
    staff = await browser.newContext({
      baseURL,
      viewport: { width: 1440, height: 1000 },
      serviceWorkers: 'block'
    })
  try {
    const page = await customer.newPage(),
      review = await staff.newPage()
    await signIn(page, 'customer@demo.local', 'DemoPass!2026')
    await signIn(review, 'manager@demo.local', 'DemoPass!2026')
    const customerToken = await token(customer)
    const evidenceCase = await customer.request.post('/api/v1/disputes', {
      headers: { 'X-CSRF-TOKEN': customerToken, 'Idempotency-Key': 'camera-e2e-' + randomUUID() },
      data: {
        transactionId: '30000000-0000-0000-0000-000000000006',
        category: 'GoodsOrServices',
        reason: 'I did not receive the goods or service',
        description: 'The attached photograph supports the delivery issue for this payment.'
      }
    })
    expect(evidenceCase.status()).toBe(201)
    const photoCase = await evidenceCase.json()
    await page.goto('/disputes/' + photoCase.id)
    await page.getByRole('button', { name: 'Evidence', exact: true }).click()
    await page.getByRole('button', { name: /Document category:/ }).click()
    await page
      .getByRole('dialog')
      .getByRole('button', { name: 'Photo or screenshot', exact: true })
      .click()
    const imagePath = testInfo.outputPath('camera-capture.jpg')
    function cameraBytes() {
      const canvas = createCanvas(4000, 3200),
        ctx = canvas.getContext('2d'),
        pixels = ctx.createImageData(canvas.width, canvas.height)
      let seed = 1234567
      for (let i = 0; i < pixels.data.length; i += 4) {
        seed = (seed * 1664525 + 1013904223) >>> 0
        pixels.data[i] = seed & 255
        pixels.data[i + 1] = (seed >>> 8) & 255
        pixels.data[i + 2] = (seed >>> 16) & 255
        pixels.data[i + 3] = 255
      }
      ctx.putImageData(pixels, 0, 0)
      let encoded: Buffer | undefined
      for (const quality of [100, 98, 96, 94]) {
        encoded = canvas.encodeSync('jpeg', quality)
        if (encoded.length < 20 * 1024 * 1024) break
      }
      canvas.width = 1
      canvas.height = 1
      return encoded!
    }
    const sourceBytes = cameraBytes()
    expect(sourceBytes.length).toBeGreaterThan(5 * 1024 * 1024)
    expect(sourceBytes.length).toBeLessThan(20 * 1024 * 1024)
    await writeFile(imagePath, sourceBytes)
    await page.getByLabel('Take Evidence Photo').setInputFiles(imagePath)
    await expect(page.locator('.pending-evidence')).toContainText('original retained', {
      timeout: 30000
    })
    const upload = page.waitForResponse(
      (r) => r.url().endsWith('/evidence') && r.request().method() === 'POST',
      { timeout: 120000 }
    )
    await page.getByRole('button', { name: /Upload 1 file/ }).click()
    expect((await upload).status()).toBe(201)
    await expect(page.getByRole('status').filter({ hasText: 'Evidence added.' })).toBeVisible({
      timeout: 30000
    })
    const files = await (
      await customer.request.get('/api/v1/disputes/' + photoCase.id + '/evidence')
    ).json()
    expect(files).toHaveLength(1)
    expect(files[0].hasOriginal).toBe(true)
    expect(files[0].sizeBytes).toBeLessThan(5 * 1024 * 1024)
    const original = await customer.request.get(
      '/api/v1/disputes/' + photoCase.id + '/evidence/' + files[0].id + '/original',
      { timeout: 60000 }
    )
    expect(original.status()).toBe(200)
    expect(Buffer.compare(await original.body(), sourceBytes)).toBe(0)
    await page.getByRole('link', { name: 'Case messages', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Refresh messages' })).toHaveCount(0)
    await expect(page.getByRole('link', { name: 'View transaction', exact: true })).toHaveCount(0)
    await page.getByLabel('Your Message').fill('Please review my retained original photograph.')
    await page.getByRole('button', { name: 'Send Message' }).click()
    await expect(page.locator('.chat-bubble')).toContainText(
      'Please review my retained original photograph.'
    )
    await review.goto('/support?dispute=' + photoCase.id)
    await review
      .getByLabel('Your Message')
      .fill('Your photograph is received and linked to the correct transaction.')
    await review.getByRole('button', { name: 'Send Message' }).click()
    await expect(page.locator('.chat-messages')).toContainText('Your photograph is received', {
      timeout: 35000
    })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect(
      await page.evaluate(() => document.scrollingElement!.scrollHeight <= innerHeight + 2)
    ).toBe(true)
    await page.screenshot({
      path: testInfo.outputPath('case-chat-mobile-dark.png'),
      fullPage: true,
      animations: 'disabled'
    })
    await page.getByRole('button', { name: 'Back To Dispute', exact: true }).click()
    await expect(page).toHaveURL(new RegExp('/disputes/' + photoCase.id))
  } finally {
    await customer.close()
    await staff.close()
  }
})
