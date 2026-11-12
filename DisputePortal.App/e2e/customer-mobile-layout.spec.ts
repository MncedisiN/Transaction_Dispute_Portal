import { expect, test, type Locator, type Page } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

test.use({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' })

async function bounds(locator: Locator) {
  const box = await locator.boundingBox()
  expect(box).not.toBeNull()
  return box!
}
async function fits(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
}
function luminance(color: string) {
  const rgb = color
    .match(/[\d.]+/g)!
    .slice(0, 3)
    .map(Number)
    .map((value) => {
      const component = value / 255
      return component <= 0.04045 ? component / 12.92 : ((component + 0.055) / 1.055) ** 2.4
    })
  return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722
}

for (const theme of ['light', 'dark']) {
  test(`mobile registration fields and security note align in ${theme} mode`, async ({
    page
  }, info) => {
    await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
    await mockScreens(page, 'anonymous')
    await page.route('**/api/v1/identity/channels', (route) =>
      route.fulfill({ json: { channels: ['email'], verificationRequired: true } })
    )
    await page.goto('/register')
    const first = await bounds(page.getByLabel('First Name', { exact: true }))
    const last = await bounds(page.getByLabel('Surname', { exact: true }))
    const email = await bounds(page.getByLabel('Email Address', { exact: true }))
    for (const field of [first, last]) {
      expect(Math.abs(field.x - email.x)).toBeLessThan(2)
      expect(Math.abs(field.width - email.width)).toBeLessThan(2)
      expect(field.height).toBeGreaterThanOrEqual(44)
    }
    const note = page.locator('.registration-security')
    const icon = await bounds(note.locator('svg'))
    const text = await bounds(note.locator('span'))
    expect(text.x - icon.x - icon.width).toBeGreaterThanOrEqual(8)
    expect(Math.abs(text.y - icon.y)).toBeLessThan(5)
    expect(text.x + text.width).toBeLessThanOrEqual(email.x + email.width + 2)
    await fits(page)
    await page.screenshot({ path: info.outputPath(`registration-${theme}.png`), fullPage: true })
  })

  test(`mobile account selectors and profile controls remain aligned and readable in ${theme} mode`, async ({
    page
  }, info) => {
    await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
    await mockScreens(page)
    await page.goto('/personal')
    const account = await bounds(page.locator('.premium-account-card'))
    const first = await bounds(
      page.getByRole('navigation', { name: 'Choose Account' }).getByRole('button').first()
    )
    const last = await bounds(
      page.getByRole('navigation', { name: 'Choose Account' }).getByRole('button').last()
    )
    expect(Math.abs(account.x - first.x)).toBeLessThan(2)
    expect(Math.abs(account.x + account.width - last.x - last.width)).toBeLessThan(2)
    await page.screenshot({ path: info.outputPath(`accounts-${theme}.png`), fullPage: true })
    await page.goto('/profile')
    const tabs = page.getByRole('navigation', { name: 'Profile Sections' })
    for (const label of ['Details', 'Security', 'Alerts']) {
      const button = tabs.getByRole('button', { name: label, exact: true })
      await expect(button).toBeVisible()
      const colors = await button.evaluate((element) => ({
        color: getComputedStyle(element).color,
        background: getComputedStyle(element.parentElement!).backgroundColor
      }))
      const a = luminance(colors.color),
        b = luminance(colors.background)
      expect((Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)).toBeGreaterThanOrEqual(4.5)
      expect((await bounds(button)).height).toBeGreaterThanOrEqual(44)
      await button.click()
      await expect(button).toHaveAttribute('aria-pressed', 'true')
    }
    await tabs.getByRole('button', { name: 'Details', exact: true }).click()
    await fits(page)
    await page.screenshot({ path: info.outputPath(`profile-${theme}.png`), fullPage: true })
  })

  test(`mobile evidence controls have space and removal works in ${theme} mode`, async ({
    page
  }, info) => {
    await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
    const fixture = await mockScreens(page)
    await page.goto('/disputes/case')
    await expect(
      page.getByText('You can withdraw your dispute while it is open.', { exact: true })
    ).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Withdraw Dispute', exact: true })).toBeVisible()
    expect(
      await page
        .locator('.case-activity-item')
        .first()
        .evaluate((element) => getComputedStyle(element).borderLeftWidth)
    ).toBe('0px')
    await page.getByRole('button', { name: 'Evidence', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Attach Files', exact: true })).toHaveCSS(
      'opacity',
      '1'
    )
    await page.getByRole('button', { name: /^Document Category:/ }).click()
    await page
      .getByRole('dialog', { name: 'Document Category', exact: true })
      .getByRole('button', { name: 'Photo Or Screenshot', exact: true })
      .click()
    await expect(page.getByRole('button', { name: 'Attach Files', exact: true })).toBeEnabled()
    await expect(page.getByRole('button', { name: 'Take Photo', exact: true })).toBeEnabled()
    const attach = await bounds(page.getByRole('button', { name: 'Attach Files', exact: true }))
    const photo = await bounds(page.getByRole('button', { name: 'Take Photo', exact: true }))
    expect(photo.x - attach.x - attach.width).toBeGreaterThanOrEqual(10)
    expect(attach.height).toBeGreaterThanOrEqual(44)
    expect(photo.height).toBeGreaterThanOrEqual(44)
    const row = page.locator('.evidence-file-row')
    const view = await bounds(row.getByRole('button', { name: 'View', exact: true }))
    const remove = await bounds(row.getByRole('button', { name: 'Remove File', exact: true }))
    expect(Math.abs(view.y - remove.y)).toBeLessThan(2)
    expect(remove.x - view.x - view.width).toBeGreaterThanOrEqual(8)
    await fits(page)
    await page.screenshot({ path: info.outputPath(`evidence-${theme}.png`), fullPage: true })
    await row.getByRole('button', { name: 'Remove File', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: 'Remove Evidence', exact: true })
    await dialog.getByLabel('Reason', { exact: true }).fill('I attached the wrong payment receipt.')
    await expect(dialog.getByRole('button', { name: 'Remove File', exact: true })).toBeEnabled()
    await dialog.getByRole('button', { name: 'Remove File', exact: true }).click()
    await expect(dialog).not.toBeVisible()
    await expect(page.getByText('No evidence attached yet.', { exact: true })).toBeVisible()
    expect(
      fixture.requests.filter((request) => request.path.endsWith('/evidence/evidence/remove'))
    ).toHaveLength(1)
  })
}
