import { expect, test, type Page } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

async function dialogFits(page: Page) {
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  const box = (await dialog.boundingBox())!,
    size = page.viewportSize()!
  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.y).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(size.width + 1)
  expect(box.y + box.height).toBeLessThanOrEqual(size.height + 1)
  return dialog
}
for (const width of [375, 1440]) {
  test(`workflow descriptions edit in a modal and retain their API contract at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page, 'staff')
    let saved: Record<string, unknown> | undefined
    await page.route('**/api/v1/lookups/case-statuses/*', (route) => {
      saved = route.request().postDataJSON()
      return route.fulfill({ status: 204 })
    })
    await page.goto('/admin/dispute-rules')
    const trigger = page.getByRole('button', { name: 'Update Descriptions', exact: true }).first()
    await trigger.click()
    const dialog = await dialogFits(page)
    await expect(dialog).toHaveAccessibleName(/Update Descriptions/)
    await dialog.getByLabel('Display Name', { exact: true }).fill('Received For Review')
    await dialog.getByLabel('Customer Label', { exact: true }).fill('Received')
    await dialog
      .getByLabel('Reason For Change', { exact: true })
      .fill('Clarify the customer description without changing the workflow.')
    await page.screenshot({ path: info.outputPath('status-editor.png') })
    await dialog.getByRole('button', { name: 'Save Descriptions', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    expect(saved?.displayName).toBe('Received For Review')
    expect(saved?.expectedRevision).toBe(1)
    expect(saved).not.toHaveProperty('closed')
    expect(saved).not.toHaveProperty('workflowTargets')
  })

  test(`policy details, copy and removal are popups with compact actions at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 900 })
    const state = await mockScreens(page, 'staff')
    await page.goto('/admin/dispute-rules')
    const policy = page.locator('.policy-history').first()
    const actions = policy.locator('.history-actions').first()
    await expect(actions.getByRole('button').first()).toBeVisible()
    const buttons = await actions.getByRole('button').all()
    await expect(buttons[0]!).toBeVisible()
    if (width > 992) {
      const boxes = await Promise.all(buttons.map((button) => button.boundingBox()))
      expect(
        Math.max(...boxes.map((b) => b!.y)) - Math.min(...boxes.map((b) => b!.y))
      ).toBeLessThan(2)
      expect(boxes[0]!.height).toBeLessThan(44)
    }
    // Use keyboard activation to verify native focus restoration in both engines.
    await buttons[0]!.focus()
    await page.keyboard.press('Enter')
    let dialog = await dialogFits(page)
    await expect(dialog).toHaveAccessibleName(/Dispute Routing.*Version 1/)
    await expect(dialog.getByText('Read Only', { exact: false }).first()).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(dialog).toHaveCount(0)
    await expect(buttons[0]!).toBeFocused()
    await policy
      .getByRole('button', { name: 'View Version 1 · Dispute Routing', exact: true })
      .click()
    dialog = await dialogFits(page)
    await dialog.getByRole('button', { name: 'Close Details', exact: true }).click()
    await buttons[1]!.click()
    dialog = await dialogFits(page)
    await expect(dialog).toHaveAccessibleName(/Copy As New Version/)
    await expect(
      dialog.getByRole('combobox', { name: 'Feature Availability', exact: true })
    ).toBeDisabled()
    await page.screenshot({ path: info.outputPath('version-popup.png') })
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await buttons[2]!.click()
    dialog = await dialogFits(page)
    await expect(dialog.getByLabel('Removal Reason', { exact: true })).toBeVisible()
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    expect(
      state.requests.filter((r) => /\/policies\/.*(versions|remove)$/.test(r.path))
    ).toHaveLength(0)
  })

  test(`new policy preview and publish stay inside the dialog at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page, 'staff')
    await page.goto('/admin/dispute-rules')
    const history = await page.evaluate(
      async () => await (await fetch('/api/v1/admin/policies/routing/history')).json()
    )
    let publication: Record<string, unknown> | undefined
    let release: () => void = () => {}
    await page.route('**/api/v1/admin/policies/routing/preview', (route) => {
      const body = route.request().postDataJSON()
      const proposed = {
        ...history.items[0],
        ...body,
        version: 2,
        state: 'Effective',
        effectiveFromUtc: new Date().toISOString()
      }
      return route.fulfill({
        json: {
          before: history.effective,
          after: { ...history.effective, selected: proposed },
          proposed,
          consequence: 'The revised policy applies after publication.'
        }
      })
    })
    await page.route('**/api/v1/admin/policies/routing/versions', async (route) => {
      publication = route.request().postDataJSON()
      await new Promise<void>((resolve) => {
        release = resolve
      })
      await route.fulfill({ status: 204 })
    })
    await page
      .locator('.policy-history')
      .first()
      .getByRole('button', { name: 'Create New Version', exact: true })
      .click()
    const dialog = await dialogFits(page)
    await expect(dialog).toHaveAccessibleName('Create New Version · Dispute Routing')
    await dialog
      .getByLabel('Reason', { exact: true })
      .fill('Publish the reviewed policy configuration for the operating country.')
    await dialog.getByRole('button', { name: 'Preview New Version', exact: true }).click()
    const publish = dialog.getByRole('button', { name: 'Publish New Version', exact: true })
    await expect(publish).toBeVisible()
    await dialog.evaluate((element) => {
      element.scrollTop = 200
    })
    const header = (await dialog.locator('.portal-dialog-heading').boundingBox())!
    const popup = (await dialog.boundingBox())!
    expect(header.y - popup.y).toBeLessThan(3)
    await page.screenshot({ path: info.outputPath('new-version-preview.png') })
    await publish.click()
    await expect.poll(() => Boolean(publication)).toBe(true)
    await page.keyboard.press('Escape')
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled()
    expect(publication?.operation).toBe('Create')
    expect(publication?.sourceVersionPublicId).toBeNull()
    expect(publication?.concurrencyToken).toBe(1)
    expect(publication).not.toHaveProperty('createdBy')
    release()
    await expect(dialog).toHaveCount(0)
  })

  test(`configuration history opens retained snapshots in a popup at ${width}px`, async ({
    page
  }) => {
    await page.setViewportSize({ width, height: 900 })
    const state = await mockScreens(page, 'staff')
    await page.goto('/admin/dispute-operations')
    await page.getByRole('button', { name: 'Country Settings', exact: true }).click()
    const history = page.locator('.settings-history')
    await history.getByRole('button', { name: 'View Configuration Version 1', exact: true }).click()
    const dialog = await dialogFits(page)
    await expect(dialog).toHaveAccessibleName('Configuration · Version 1')
    await expect(dialog.getByRole('button', { name: 'Preview Change', exact: true })).toHaveCount(0)
    await page.keyboard.press('Escape')
    await expect(dialog).toHaveCount(0)
    await history.getByRole('button', { name: 'Copy As New Version', exact: true }).click()
    await dialogFits(page)
    await expect(
      page.getByRole('dialog').getByRole('button', { name: 'Preview Change', exact: true })
    ).toBeVisible()
    await page.keyboard.press('Escape')
    expect(state.requests.filter((r) => r.path === '/api/v1/platform/settings')).toHaveLength(0)
  })

  test(`record rows and user access fields align at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page)
    await page.goto('/disputes')
    const row = page.locator('.case-list-item').first()
    await expect(row).toContainText('R 249,99')
    const title = (await row.locator('.case-row-main').boundingBox())!,
      meta = (await row.locator('.case-row-meta').boundingBox())!
    if (width > 992) {
      expect(meta.x).toBeGreaterThan(title.x + title.width - 1)
      expect(Math.abs(meta.y + meta.height / 2 - title.y - title.height / 2)).toBeLessThan(3)
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath('dispute-rows.png') })
    await page.goto('/transactions')
    const transaction = page.locator('.transaction-row').first()
    await expect(transaction).toBeVisible()
    if (width > 992) {
      const name = (await transaction.locator('.transaction-merchant').boundingBox())!,
        description = (await transaction
          .locator('.customer-transaction-description')
          .boundingBox())!,
        date = (await transaction.locator('.transaction-date').boundingBox())!
      expect(description.x).toBeGreaterThan(name.x + name.width - 1)
      expect(date.x).toBeGreaterThan(description.x + description.width - 1)
    }
    await page.screenshot({ path: info.outputPath('transaction-rows.png') })
    await mockScreens(page, 'staff')
    await page.goto('/admin/users')
    await page.getByRole('button', { name: 'Manage Sam Specialist', exact: true }).click()
    const dialog = await dialogFits(page)
    const status = (await dialog.getByRole('button', { name: /^Account Status:/ }).boundingBox())!,
      capacity = (await dialog.getByLabel('Case Capacity', { exact: true }).boundingBox())!
    if (width > 600) {
      expect(Math.abs(status.y - capacity.y)).toBeLessThan(2)
      expect(Math.abs(status.height - capacity.height)).toBeLessThan(3)
    }
    await page.screenshot({ path: info.outputPath('user-access.png') })
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  })
}

test('description conflicts keep the modal and user input available for correction', async ({
  page
}) => {
  await mockScreens(page, 'staff')
  await page.route('**/api/v1/lookups/case-statuses/*', (route) =>
    route.fulfill({
      status: 409,
      json: { detail: 'These descriptions changed. Refresh the catalogue and try again.' }
    })
  )
  await page.goto('/admin/dispute-rules')
  await page.getByRole('button', { name: 'Update Descriptions', exact: true }).first().click()
  const dialog = await dialogFits(page)
  await dialog.getByLabel('Display Name', { exact: true }).fill('My Edited Description')
  await dialog
    .getByLabel('Reason For Change', { exact: true })
    .fill('Keep the edited input after a concurrent change.')
  await dialog.getByRole('button', { name: 'Save Descriptions', exact: true }).click()
  await expect(dialog.getByRole('alert')).toContainText('These descriptions changed.')
  await expect(dialog.getByLabel('Display Name', { exact: true })).toHaveValue(
    'My Edited Description'
  )
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
})
