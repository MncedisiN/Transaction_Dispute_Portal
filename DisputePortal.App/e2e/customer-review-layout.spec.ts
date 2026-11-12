import { expect, test } from '@playwright/test'
import { mockScreens, screenTransaction } from './fixtures/screens'

test.use({ serviceWorkers: 'block' })

for (const width of [375, 1440]) {
  for (const theme of ['light', 'dark']) {
    test(`review, existing dispute and pagination stay clear at ${width}px in ${theme} mode`, async ({
      page
    }, info) => {
      await page.setViewportSize({ width, height: 900 })
      await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
      await mockScreens(page)
      await page.goto('/disputes/new/payment')
      await page.getByRole('radio', { name: 'I was charged more than once', exact: true }).check()
      await page
        .getByLabel('Add A Few Details', { exact: true })
        .fill('This payment appears twice.')
      await page.getByRole('button', { name: 'Review Your Dispute', exact: true }).click()
      await expect(
        page.getByRole('heading', { name: 'Review Your Dispute', exact: true })
      ).toBeVisible()
      await expect(page.locator('.review-check')).toHaveCount(0)
      await expect(
        page.getByText('We’ll use these details to start your review.', { exact: true })
      ).toHaveCount(0)
      await page.screenshot({ path: info.outputPath('review.png'), fullPage: true })

      await page.route('**/api/v1/transactions/payment/dispute-check', (route) =>
        route.fulfill({
          json: {
            allowed: false,
            canStart: false,
            activeDisputeId: 'case',
            requiresVerification: false,
            message: 'A dispute is already active for this transaction. View the existing dispute.'
          }
        })
      )
      await page.goto('/disputes/new/payment')
      const card = page.locator('.dispute-intake-state')
      await expect(card).toBeVisible()
      await expect(card).toHaveCSS('text-align', 'center')
      const heading = card.getByRole('heading', { name: 'Dispute Already Open', exact: true })
      expect(
        await heading.evaluate((element) => parseFloat(getComputedStyle(element).fontSize))
      ).toBeLessThanOrEqual(28)
      const cardBox = (await card.boundingBox())!
      const icon = (await card.locator('.intake-mark svg').boundingBox())!
      expect(Math.abs(icon.x + icon.width / 2 - cardBox.x - cardBox.width / 2)).toBeLessThan(2)
      const existing = card.getByRole('link', { name: 'View Existing Dispute', exact: true })
      const back = card.getByRole('link', { name: 'Back To Transaction', exact: true })
      const first = (await existing.boundingBox())!,
        last = (await back.boundingBox())!
      expect(last.y - first.y - first.height).toBeGreaterThanOrEqual(12)
      expect(Math.abs(first.width - last.width)).toBeLessThan(2)
      await expect(existing).toHaveAttribute('href', '/disputes/case')
      await expect(back).toHaveAttribute('href', '/transactions/payment')
      await page.screenshot({ path: info.outputPath('existing-dispute.png'), fullPage: true })

      await page.route('**/api/v1/transactions?**', (route) => {
        const current = Number(new URL(route.request().url()).searchParams.get('page') ?? '1')
        return route.fulfill({
          json: { items: [screenTransaction], total: 21, page: current, pageSize: 10 }
        })
      })
      await page.goto('/transactions')
      await expect(page.locator('.history-window-hint')).toHaveCount(0)
      await expect(page.getByText(/days after account opening/)).toHaveCount(0)
      const paging = page.locator('.transaction-pagination')
      const next = paging.getByRole('button', { name: 'Next', exact: true })
      const previous = paging.getByRole('button', { name: 'Previous', exact: true })
      await expect(paging).toContainText('Page 1 of 3')
      await expect(previous).toBeDisabled()
      await next.click()
      await expect(paging).toContainText('Page 2 of 3')
      await previous.click()
      await expect(paging).toContainText('Page 1 of 3')
      await next.click()
      await expect(paging).toContainText('Page 2 of 3')
      await next.click()
      await expect(paging).toContainText('Page 3 of 3')
      await expect(next).toBeDisabled()
      await expect(next).toHaveCSS('opacity', '1')
      const a = (await previous.boundingBox())!,
        b = (await next.boundingBox())!,
        container = (await paging.boundingBox())!
      expect(Math.abs(a.y - b.y)).toBeLessThan(2)
      expect(Math.abs(a.height - b.height)).toBeLessThan(2)
      expect(Math.abs(a.width - b.width)).toBeLessThan(2)
      expect(Math.abs(b.x + b.width - container.x - container.width)).toBeLessThan(2)
      expect(b.x - a.x - a.width).toBeGreaterThanOrEqual(10)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true
      )
      await page.screenshot({ path: info.outputPath('pagination.png'), fullPage: true })
    })
  }
}
