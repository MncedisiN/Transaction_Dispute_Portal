import { expect, test } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

test('assigned case actions use the available row and icon navigation stays labelled', async ({
  page
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockScreens(page, 'staff')
  await page.goto('/admin/disputes')
  const actions = page.locator('.case-management-actions').first()
  const manage = actions.getByRole('link', { name: 'Manage Case', exact: true })
  await expect(manage).toBeVisible()
  expect((await manage.boundingBox())!.width).toBeGreaterThan(
    (await actions.boundingBox())!.width - 3
  )
  await expect(
    page.locator('.primary-nav').getByRole('link', { name: 'Transactions', exact: true })
  ).toHaveAttribute('title', 'Transactions')
})

test('desktop workflow keeps review controls beside the case information', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockScreens(page, 'staff')
  await page.goto('/admin/disputes/case?section=workflow')
  const owner = page.locator('.admin-detail-page > .ops-panel')
  const editor = page.locator('.admin-actions')
  await expect(owner).toBeVisible()
  await expect(editor).toBeVisible()
  const left = (await owner.boundingBox())!
  const right = (await editor.boundingBox())!
  expect(right.x).toBeGreaterThanOrEqual(left.x + left.width)
  expect(Math.abs(left.y - right.y)).toBeLessThan(24)
})
