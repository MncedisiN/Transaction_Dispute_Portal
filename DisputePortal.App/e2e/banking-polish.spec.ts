import { expect, test } from '@playwright/test'
import { mockScreens } from './fixtures/screens'
import reference from './fixtures/portal-reference.fixture.json' with { type: 'json' }

for (const width of [375, 390]) {
  test(`account controls and touch dialogs remain usable at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 720 })
    await mockScreens(page)
    let accountOpening = false
    await page.route('**/api/v1/portal/reference', (route) =>
      route.fulfill({ json: { ...reference, customerAccountOpeningEnabled: accountOpening } })
    )
    await page.goto('/personal')
    await expect(page.getByRole('button', { name: 'Open Account', exact: true })).toHaveCount(0)
    await expect(page.getByText('Your Accounts', { exact: true })).toHaveCount(0)
    await expect(page.getByText('Reserved Funds', { exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: 'Transfer', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByLabel('From Account')).not.toHaveValue(/South Africa|ZAR/)
    const controlBounds = await dialog.locator('input,select,button').evaluateAll((elements) =>
      elements.map((element) => {
        const box = element.getBoundingClientRect(),
          dialog = element.closest('dialog')!.getBoundingClientRect()
        return {
          left: box.left,
          right: box.right,
          dialogLeft: dialog.left,
          dialogRight: dialog.right
        }
      })
    )
    for (const box of controlBounds) {
      expect(box.left).toBeGreaterThanOrEqual(box.dialogLeft)
      expect(box.right).toBeLessThanOrEqual(box.dialogRight + 1)
    }
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    accountOpening = true
    await page.goto('/personal')
    await page.getByRole('button', { name: 'Open Account', exact: true }).click()
    await expect(page.getByRole('dialog').getByLabel('Account Type')).toBeVisible()
    await page.screenshot({ path: info.outputPath('account-opening.png') })
    await page.keyboard.press('Escape')
    await page.goto('/transactions')
    await page.getByRole('button', { name: 'Filters', exact: true }).click()
    const dates = page.getByRole('dialog').locator('input[type=date]')
    await dates.first().fill('2026-11-01')
    await dates.last().fill('2026-11-10')
    for (const date of await dates.all()) {
      const box = (await date.boundingBox())!
      const bounds = (await page.getByRole('dialog').boundingBox())!
      expect(box.x + box.width).toBeLessThan(bounds.x + bounds.width)
      expect(
        await date.evaluate((e) => Number.parseFloat(getComputedStyle(e).fontSize))
      ).toBeGreaterThanOrEqual(16)
    }
    await page.screenshot({ path: info.outputPath('date-filters.png') })
  })

  test(`dispute textarea retains typing across visual keyboard movement at ${width}px`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width, height: 844 })
    await page.addInitScript(() => {
      const viewport = Object.assign(new EventTarget(), {
        height: 844,
        width: 390,
        offsetTop: 0,
        offsetLeft: 0,
        scale: 1
      })
      Object.defineProperty(window, 'visualViewport', { get: () => viewport })
    })
    await mockScreens(page)
    await page.goto('/disputes/new/payment')
    const input = page.getByLabel('Add A Few Details', { exact: true })
    await input.fill('Please review this payment.')
    expect(
      await input.evaluate((e) => Number.parseFloat(getComputedStyle(e).fontSize))
    ).toBeGreaterThanOrEqual(16)
    await page.evaluate(() => {
      const viewport = window.visualViewport!
      Object.assign(viewport, { height: 350, offsetTop: 65 })
      viewport.dispatchEvent(new Event('resize'))
    })
    await expect(page.locator('html')).toHaveAttribute('data-keyboard-open', '')
    await input.pressSequentially(' I need help.', { delay: 50 })
    await expect(input).toBeFocused()
    await expect(input).toHaveValue('Please review this payment. I need help.')
    const box = (await input.boundingBox())!
    expect(box.y).toBeGreaterThanOrEqual(65)
    expect(box.y + box.height).toBeLessThanOrEqual(415)
    const scroll = await page.evaluate(() => scrollY)
    await page.evaluate(() => {
      for (let i = 0; i < 8; i++) window.visualViewport!.dispatchEvent(new Event('scroll'))
    })
    await page.waitForTimeout(150)
    expect(Math.abs((await page.evaluate(() => scrollY)) - scroll)).toBeLessThan(2)
    await page.screenshot({ path: info.outputPath('keyboard-focus.png') })
  })
}
