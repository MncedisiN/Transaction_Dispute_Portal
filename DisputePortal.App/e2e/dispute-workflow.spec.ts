import { expect, test, type Page } from '@playwright/test'

const transactionId = '30000000-0000-0000-0000-000000000001'
// Run against a fresh, synthetic assessment database. This test intentionally creates a case.
async function signIn(page: Page, email: string, password: string) {
  await page.goto('/sign-in')
  await page.getByLabel('Email, Mobile Or Username').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign In Securely' }).click()
  await expect(page.getByRole('link', { name: /Open profile for/ })).toBeVisible({ timeout: 30000 })
}

async function saveStatus(page: Page) {
  // Await the committed API response; a toast alone is not a persistence check.
  const response = page.waitForResponse(
    (result) => result.url().endsWith('/transitions') && result.request().method() === 'POST',
    { timeout: 60000 }
  )
  await page.getByRole('button', { name: 'Save Status Update' }).click()
  expect((await response).status()).toBe(200)
  await expect(page.getByRole('status').filter({ hasText: 'Case updated.' })).toBeVisible()
}

test('customer submits a dispute, staff resolves it, history and ownership remain protected', async ({
  browser,
  baseURL
}) => {
  test.setTimeout(180000)
  const customerContext = await browser.newContext({ baseURL, serviceWorkers: 'block' })
  const staffContext = await browser.newContext({ baseURL, serviceWorkers: 'block' })
  const otherContext = await browser.newContext({ baseURL, serviceWorkers: 'block' })
  try {
    const customer = await customerContext.newPage(),
      staff = await staffContext.newPage(),
      other = await otherContext.newPage()
    await signIn(customer, 'customer@demo.local', 'DemoPass!2026')
    await customer.goto('/transactions')
    await customer.getByRole('button', { name: /Fresh Market/ }).click()
    await expect(customer).toHaveURL(new RegExp('/transactions/' + transactionId))
    await customer.getByRole('link', { name: 'Start A Dispute' }).click()
    await customer.getByRole('radio', { name: 'The amount looks incorrect', exact: true }).check()
    await customer
      .getByLabel('Add A Few Details')
      .fill('The amount on this synthetic purchase differs from my receipt.')
    await customer.getByRole('button', { name: 'Review Your Dispute' }).click()
    await expect(
      customer.getByRole('heading', { name: 'Check everything looks right' })
    ).toBeVisible()
    const response = customer.waitForResponse(
      (r) => r.url().endsWith('/api/v1/disputes') && r.request().method() === 'POST'
    )
    await customer.getByRole('button', { name: 'Submit Dispute' }).click()
    const createdResponse = await response
    expect(createdResponse.status()).toBe(201)
    const created = (await createdResponse.json()) as { id: string; reference: string }
    await expect(customer.getByText(created.reference, { exact: true })).toBeVisible()
    await customer.getByRole('link', { name: 'Track Your Dispute' }).click()
    await expect(customer.locator('.case-page-heading')).toContainText(created.reference)

    const csrf = (await (await customerContext.request.get('/api/v1/auth/csrf')).json()) as {
      requestToken: string
    }
    const duplicate = await customerContext.request.post('/api/v1/disputes', {
      headers: { 'X-CSRF-TOKEN': csrf.requestToken, 'Idempotency-Key': 'assessment-duplicate-01' },
      data: {
        transactionId,
        category: 'IncorrectAmount',
        reason: 'The amount is incorrect',
        description: 'The amount differs from my synthetic receipt.'
      }
    })
    expect(duplicate.status()).toBe(409)
    const invalid = await customerContext.request.post('/api/v1/disputes', {
      headers: { 'X-CSRF-TOKEN': csrf.requestToken, 'Idempotency-Key': 'assessment-invalid-01' },
      data: {
        transactionId,
        category: 'IncorrectAmount',
        reason: 'The amount is incorrect',
        description: 'short'
      }
    })
    expect(invalid.status()).toBe(422)

    await signIn(staff, 'manager@demo.local', 'DemoPass!2026')
    await staff.goto('/admin/disputes')
    await staff.getByRole('link', { name: new RegExp(created.reference) }).click()
    await staff.getByRole('button', { name: 'Workflow', exact: true }).click()
    await staff.getByRole('button', { name: /^Next status:/ }).click()
    await staff
      .getByRole('dialog')
      .getByRole('button', { name: 'Under Review', exact: true })
      .click()
    await staff
      .getByLabel('Customer Update Or Outcome')
      .fill('A specialist is reviewing the transaction.')
    await saveStatus(staff)
    await staff.getByLabel('Who Can See This?').selectOption('Internal')
    await staff
      .getByLabel('Message', { exact: true })
      .fill('Internal reviewer note: synthetic flag for staff only.')
    await staff.getByRole('button', { name: 'Save Note' }).click()
    await expect(
      staff.getByRole('status').filter({ hasText: 'Internal note saved.' })
    ).toBeVisible()
    await customer.reload()
    await expect(
      customer.getByText('A specialist is reviewing the transaction.', { exact: true })
    ).toBeVisible()
    await expect(
      customer.getByText('Internal reviewer note: synthetic flag for staff only.')
    ).toHaveCount(0)

    await staff.getByRole('button', { name: /^Next status:/ }).click()
    await staff
      .getByRole('dialog')
      .getByRole('button', { name: 'Resolve without refund', exact: true })
      .click()
    await staff
      .getByLabel('Customer Update Or Outcome')
      .fill('The synthetic amount discrepancy has been reviewed and resolved.')
    await saveStatus(staff)
    await customer.goto('/disputes?status=closed')
    await expect(customer.getByText(created.reference, { exact: true })).toBeVisible()
    await customer.goto('/disputes/' + created.id)
    await expect(customer.getByText('Review complete', { exact: true })).toBeVisible()
    await expect(
      customer
        .getByText('The synthetic amount discrepancy has been reviewed and resolved.', {
          exact: true
        })
        .first()
    ).toBeVisible()
    const history = await (
      await customerContext.request.get('/api/v1/disputes/' + created.id)
    ).json()
    expect(history.dispute.status).toBe('Resolved')
    expect(JSON.stringify(history)).not.toContain('Internal reviewer note')

    await signIn(other, 'other@demo.local', 'OtherPass!2026')
    expect((await otherContext.request.get('/api/v1/transactions/' + transactionId)).status()).toBe(
      404
    )
    expect((await otherContext.request.get('/api/v1/disputes/' + created.id)).status()).toBe(404)
    expect((await otherContext.request.get('/api/v1/admin/disputes')).status()).toBe(403)
  } finally {
    await customerContext.close()
    await staffContext.close()
    await otherContext.close()
  }
})
