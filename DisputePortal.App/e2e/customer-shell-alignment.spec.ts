import { expect, test, type Page } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

test.use({ serviceWorkers: 'block' })

async function headerEdges(page: Page) {
  const brand = page.locator('.topbar .mobile-brand')
  const profile = page.getByRole('button', { name: 'Profile Menu', exact: true })
  await expect(profile).toBeVisible()
  const edges = await page.locator('.content-area').evaluate((element) => {
    const rect = element.getBoundingClientRect(),
      style = getComputedStyle(element)
    return {
      left: rect.left + parseFloat(style.paddingLeft),
      right: rect.right - parseFloat(style.paddingRight)
    }
  })
  const product = (await brand.boundingBox())!,
    account = (await profile.boundingBox())!
  const notifications = (await page.locator('.topbar .notification-control').boundingBox())!
  expect(Math.abs(product.x - edges.left)).toBeLessThan(2)
  expect(Math.abs(account.x - edges.left)).toBeLessThan(2)
  expect(Math.abs(account.x + account.width - edges.right)).toBeLessThan(2)
  expect(Math.abs(notifications.x + notifications.width - edges.right)).toBeLessThan(2)
  expect(
    await profile
      .locator('.profile-copy')
      .evaluate((element) => element.scrollWidth <= element.clientWidth)
  ).toBe(true)
}

for (const width of [375, 390]) {
  for (const theme of ['light', 'dark']) {
    test(`mobile header and dispute stages align at ${width}px in ${theme} mode`, async ({
      page
    }, info) => {
      await page.setViewportSize({ width, height: 844 })
      await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
      await mockScreens(page)
      await page.route('**/api/v1/auth/me', (route) =>
        route.fulfill({
          json: {
            id: 'customer',
            displayName: 'Alexandria Customer',
            roles: ['customer'],
            permissions: []
          }
        })
      )
      await page.goto('/personal')
      await expect(page.locator('.premium-account-card')).toBeVisible()
      await headerEdges(page)
      const accountCard = (await page.locator('.premium-account-card').boundingBox())!
      const profileCard = (await page
        .getByRole('button', { name: 'Profile Menu', exact: true })
        .boundingBox())!
      expect(Math.abs(accountCard.x - profileCard.x)).toBeLessThan(2)
      expect(
        Math.abs(accountCard.x + accountCard.width - profileCard.x - profileCard.width)
      ).toBeLessThan(2)
      await page.screenshot({ path: info.outputPath('overview.png') })
      await page.goto('/transactions')
      await expect(page.locator('.transaction-row').first()).toBeVisible()
      await headerEdges(page)
      await expect(page.getByText('Amounts In Account Currency', { exact: true })).toHaveCount(0)
      await expect(page.getByText(/days after account opening/)).toHaveCount(0)
      await page.goto('/disputes/case')
      await headerEdges(page)
      const progress = page.getByRole('list', { name: 'Case Progress', exact: true })
      await expect(progress).toBeVisible()
      const stage = progress.locator(':scope > li')
      await expect(stage).toHaveCount(3)
      const row = (await progress.boundingBox())!
      const first = (await stage.first().boundingBox())!,
        middle = (await stage.nth(1).boundingBox())!,
        last = (await stage.last().boundingBox())!
      expect(Math.abs(first.x - row.x)).toBeLessThan(2)
      expect(Math.abs(last.x + last.width - row.x - row.width)).toBeLessThan(2)
      expect(Math.abs(middle.x + middle.width / 2 - row.x - row.width / 2)).toBeLessThan(2)
      for (const label of ['Received', 'In Review', 'Complete'])
        await expect(progress.getByText(label, { exact: true })).toBeVisible()
      const badges = await stage.locator('.w3-badge').all()
      const badgeBoxes = await Promise.all(badges.map((badge) => badge.boundingBox()))
      expect(
        Math.max(...badgeBoxes.map((box) => box!.y)) - Math.min(...badgeBoxes.map((box) => box!.y))
      ).toBeLessThan(2)
      for (const label of await stage.locator('b').all())
        expect((await label.boundingBox())!.height).toBeLessThan(24)
      await expect(stage.first()).toHaveAttribute('aria-current', 'step')
      await expect(
        page.getByRole('heading', { name: 'No Longer Need This Dispute?', exact: true })
      ).toBeVisible()
      await expect(
        page.getByRole('button', { name: 'Withdraw Dispute', exact: true })
      ).toBeVisible()
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true
      )
      await page.screenshot({ path: info.outputPath('dispute-progress.png') })
    })
  }
}
