import { expect, test } from '@playwright/test'
import { mockScreens } from './fixtures/screens'
import reference from './fixtures/portal-reference.fixture.json' with { type: 'json' }

for (const width of [375, 390]) {
  test(`mobile detail values retain usable width at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 })
    await mockScreens(page)
    await page.goto('/disputes/case')
    await page.getByRole('button', { name: 'Details', exact: true }).click()
    const values = page.locator('.detail-definition td.w3-mobile')
    expect(await values.count()).toBeGreaterThan(0)
    for (const value of await values.all()) {
      expect((await value.boundingBox())!.width).toBeGreaterThan(width - 80)
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  })

  test(`chat grows while preserving the mobile composer at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 })
    await mockScreens(page)
    await page.goto('/support?thread=open-thread')
    const input = page.getByRole('textbox', { name: 'Your Message', exact: true })
    await input.fill('First line\nSecond line\nThird line\nFourth line\nFifth line')
    await expect.poll(async () => (await input.boundingBox())!.height).toBeGreaterThan(90)
    await page.setViewportSize({ width, height: 460 })
    await input.focus()
    await expect
      .poll(async () => {
        const box = (await input.boundingBox())!
        return box.height > 70 && box.y >= 0 && box.y + box.height <= 460
      })
      .toBe(true)
    await expect(input).toHaveValue('First line\nSecond line\nThird line\nFourth line\nFifth line')
    await expect(input).toBeFocused()
  })
}

test('customer search follows the API setting on each customer record screen', async ({ page }) => {
  await mockScreens(page)
  let enabled = false
  await page.route('**/api/v1/portal/reference', (route) =>
    route.fulfill({ json: { ...reference, customerSearchEnabled: enabled } })
  )
  for (const path of ['/transactions', '/disputes', '/support']) {
    await page.goto(path)
    await expect(page.locator('.filter-bar')).toHaveCount(0)
  }
  enabled = true
  await page.goto('/transactions')
  await expect(
    page.getByRole('searchbox', { name: 'Search Transactions', exact: true })
  ).toBeVisible()
})

test('portal search publication preserves other settings and uses the current version', async ({
  page
}) => {
  await mockScreens(page, 'staff')
  let version = 7
  let settings = {
    customerSearchEnabled: true,
    customerAccountOpeningEnabled: false,
    roles: { retained: 'original' }
  }
  let published: Record<string, unknown> | undefined
  await page.route('**/api/v1/admin/portal-settings', async (route) => {
    if (route.request().method() === 'POST') {
      published = route.request().postDataJSON()
      settings = published!.settings as typeof settings
      version++
    }
    await route.fulfill({ json: { version, settings } })
  })
  await page.route('**/api/v1/portal/reference', (route) =>
    route.fulfill({ json: { ...reference, customerSearchEnabled: settings.customerSearchEnabled } })
  )
  await page.goto('/admin/dispute-operations')
  await page.getByRole('button', { name: 'Portal Settings', exact: true }).click()
  await page.getByRole('switch', { name: 'Hide Customer Search And Filters' }).check()
  await page
    .getByLabel('Reason For Change', { exact: true })
    .fill('Hide search controls during the customer trial.')
  await page.getByRole('button', { name: 'Save Customer Settings', exact: true }).click()
  await expect.poll(() => version).toBe(8)
  expect(published).toMatchObject({
    expectedVersion: 7,
    settings: { customerSearchEnabled: false, roles: { retained: 'original' } }
  })
})

test('registration hides banking context and country choices are bounded', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mockScreens(page, 'anonymous')
  await page.goto('/register')
  await expect(page.getByText('Banking Country', { exact: true })).toHaveCount(0)
  await expect(page.getByText('Account Currency', { exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: /Mobile Country Code/ }).click()
  const menu = page.locator('.country-menu').last()
  await expect(menu).toBeVisible()
  expect((await menu.boundingBox())!.height).toBeLessThan(360)
})

test('admin data tables scroll horizontally instead of collapsing their columns', async ({
  page
}) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mockScreens(page, 'staff')
  await page.goto('/admin/monitoring')
  const table = page.locator('.w3-responsive table').first()
  await expect(table).toBeVisible()
  expect((await table.boundingBox())!.width).toBeGreaterThanOrEqual(680)
  for (const heading of await table.locator('thead th').all())
    expect((await heading.boundingBox())!.height).toBeLessThan(80)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
test('dark header controls remain readable on desktop and mobile', async ({ page }, info) => {
  await page.addInitScript(() => localStorage.setItem('portal-theme', 'dark'))
  await mockScreens(page, 'staff')
  await page.goto('/notifications')
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 844 })
    const controls = page.locator('.topbar .icon-button')
    await expect(controls.first()).toBeVisible()
    expect(await controls.count()).toBeGreaterThanOrEqual(2)
    for (const control of await controls.all()) {
      const contrast = await control.evaluate((element) => {
        const style = getComputedStyle(element)
        const luminance = (color: string) => {
          const channels = color
            .match(/[\d.]+/g)!
            .slice(0, 3)
            .map(Number)
            .map((value) => {
              const channel = value / 255
              return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
            })
          return channels[0]! * 0.2126 + channels[1]! * 0.7152 + channels[2]! * 0.0722
        }
        const foreground = luminance(style.color)
        const background = luminance(style.backgroundColor)
        return (Math.max(foreground, background) + 0.05) / (Math.min(foreground, background) + 0.05)
      })
      expect(contrast).toBeGreaterThan(4.5)
    }
    expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).not.toBe(
      'rgb(0, 0, 0)'
    )
    await page.screenshot({ path: info.outputPath(`dark-header-${width}.png`) })
  }
})
