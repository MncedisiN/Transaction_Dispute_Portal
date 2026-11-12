import { expect, test } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

for (const width of [390, 1440]) {
  test(`customer activity, overview and profile use available width at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page)
    await page.goto('/personal')
    const card = page.locator('.premium-account-card')
    await expect(card).toBeVisible()
    expect(await card.evaluate((element) => getComputedStyle(element).backgroundColor)).toBe(
      'rgb(255, 255, 255)'
    )
    if (width < 993) {
      expect((await page.locator('.primary-disputes-navigation svg').boundingBox())!.height).toBe(
        28
      )
    }
    await page.screenshot({ path: info.outputPath('overview.png') })
    await page.goto('/transactions')
    await expect(page.locator('.history-window-hint')).toHaveCount(0)
    const row = page.locator('.transaction-row').first()
    await expect(row).toBeVisible()
    const rowBox = (await row.boundingBox())!
    if (width < 600) expect(rowBox.width).toBeGreaterThan(width - 64)
    const right = (await row.locator('.transaction-right').boundingBox())!
    expect(right.x + right.width).toBeLessThanOrEqual(rowBox.x + rowBox.width + 1)
    expect(right.x).toBeGreaterThanOrEqual(rowBox.x)
    const description = (await row.locator('.transaction-info').boundingBox())!
    expect(right.x).toBeGreaterThanOrEqual(description.x + description.width - 1)
    expect(right.y).toBeGreaterThanOrEqual(rowBox.y)
    expect(right.y + right.height).toBeLessThanOrEqual(rowBox.y + rowBox.height + 1)
    await page.screenshot({ path: info.outputPath('activity.png') })
    await page.goto('/disputes')
    await expect(page.getByRole('button', { name: 'Active Cases', exact: true })).toHaveAttribute(
      'aria-pressed',
      'true'
    )
    await page.screenshot({ path: info.outputPath('active-cases.png') })
    await page.getByRole('button', { name: 'Closed Cases', exact: true }).click()
    await expect(page.getByText('Closed in the last 30 days')).toBeVisible()
    await page.screenshot({ path: info.outputPath('closed-cases.png') })
    await page.goto('/profile')
    const first = (await page
      .getByRole('textbox', { name: 'First Name', exact: true })
      .boundingBox())!
    const middle = (await page.getByRole('textbox', { name: /Middle Name/ }).boundingBox())!
    expect(Math.abs(first.x - middle.x)).toBeLessThan(2)
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)).toBe(
      false
    )
  })

  test(`policy toolbar and version actions fill their columns at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page, 'staff')
    await page.goto('/admin/dispute-rules')
    const policy = page.locator('.policy-history').first()
    const toolbar = policy.locator('.policy-version-toolbar')
    const create = toolbar.getByRole('button', { name: 'Create New Version' })
    const search = toolbar.getByRole('searchbox')
    await expect(create).toBeVisible()
    const a = (await create.boundingBox())!,
      b = (await search.boundingBox())!
    if (width > 992) expect(Math.abs(a.y - b.y)).toBeLessThan(3)
    await create.click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    const actions = dialog.locator('.dialog-actions')
    const buttons = await actions.getByRole('button').all()
    expect(buttons.length).toBe(3)
    const boxes = await Promise.all(buttons.map((button) => button.boundingBox()))
    if (width > 600) {
      expect(
        Math.max(...boxes.map((box) => box!.y)) - Math.min(...boxes.map((box) => box!.y))
      ).toBeLessThan(2)
      expect(
        Math.max(...boxes.map((box) => box!.width)) - Math.min(...boxes.map((box) => box!.width))
      ).toBeLessThan(2)
    }
    for (const box of boxes) expect(box!.x + box!.width).toBeLessThanOrEqual(width + 1)
    await page.screenshot({ path: info.outputPath('policy-actions.png') })
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(dialog).toHaveCount(0)
  })

  test(`closed cases show the saved resolution instead of an earlier review decision at ${width}px`, async ({
    page
  }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page, 'staff')
    await page.goto('/admin/disputes/case')
    const detail = await page.evaluate(
      async () => await (await fetch('/api/v1/admin/disputes/case')).json()
    )
    const reason =
      'The payment was authorised. The dispute was declined and no refund was approved.'
    await page.route('**/api/v1/admin/disputes/case', (route) =>
      route.fulfill({
        json: { ...detail, dispute: { ...detail.dispute, status: 'Declined', resolution: reason } }
      })
    )
    await page.reload()
    await expect(page.locator('.financial-outcome')).toContainText(reason)
    await expect(page.locator('.financial-outcome')).not.toContainText('Under investigation')
  })
}
