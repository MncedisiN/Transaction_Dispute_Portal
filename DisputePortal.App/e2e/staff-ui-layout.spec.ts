import { expect, test } from '@playwright/test'
import { mockScreens, screenTransaction } from './fixtures/screens'

test.use({ serviceWorkers: 'block' })
for (const width of [375, 1440]) {
  for (const theme of ['light', 'dark']) {
    test(`staff transactions and access forms stay aligned at ${width}px in ${theme} mode`, async ({
      page
    }, info) => {
      test.setTimeout(90000)
      await page.setViewportSize({ width, height: 844 })
      await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
      await mockScreens(page, 'staff')
      const rows = [
        { ...screenTransaction, status: 'Posted', disputeId: null },
        { ...screenTransaction, id: 'pending', status: 'Pending', disputeId: null },
        { ...screenTransaction, id: 'reversed', status: 'Reversed', disputeId: null },
        {
          ...screenTransaction,
          id: 'no-status',
          merchantName: 'Transfer without status',
          status: null,
          direction: 'Credit',
          disputeId: null
        }
      ]
      await page.route('**/api/v1/admin/transactions?**', (route) =>
        route.fulfill({ json: { items: rows, total: rows.length, page: 1, pageSize: 20 } })
      )
      await page.goto('/admin/transactions')
      const transactions = page.locator('.staff-transaction-row')
      await expect(transactions).toHaveCount(4)
      for (const row of await transactions.all()) {
        const infoBox = (await row.locator('.staff-transaction-info').boundingBox())!
        const amount = (await row.locator('.staff-transaction-amount').boundingBox())!
        const arrow = (await row.locator('.staff-row-chevron').boundingBox())!
        const outer = (await row.boundingBox())!
        expect(amount.x - infoBox.x - infoBox.width).toBeGreaterThanOrEqual(8)
        expect(arrow.x - amount.x - amount.width).toBeGreaterThanOrEqual(8)
        expect(Math.abs(arrow.y + arrow.height / 2 - amount.y - amount.height / 2)).toBeLessThan(2)
        expect(arrow.x + arrow.width).toBeLessThanOrEqual(outer.x + outer.width + 1)
      }
      await expect(transactions.first().getByText('Completed', { exact: true })).toBeVisible()
      await expect(transactions.last().getByText('Completed', { exact: true })).toHaveCount(0)
      if (width < 600) {
        const support = page
          .getByRole('navigation', { name: 'Mobile Navigation' })
          .getByRole('link', { name: 'Support', exact: true })
        expect((await support.locator('svg').boundingBox())!.width).toBe(28)
        await expect(support).toHaveCSS('font-weight', '700')
      }
      await page.screenshot({ path: info.outputPath('staff-transactions.png'), fullPage: true })
      await transactions.first().click()
      await expect(page).toHaveURL(/\/admin\/transactions\/payment$/)
      const email = 'staff_' + 'longidentifier'.repeat(7) + '@example.test'
      await page.route('**/api/v1/operations/users', (route) =>
        route.fulfill({
          json: [
            {
              id: 'staff-member',
              displayName: 'Taylor Specialist',
              firstName: 'Taylor',
              middleName: 'Morgan',
              lastName: 'Specialist',
              email,
              role: 'dispute_agent',
              status: 'Active',
              capacity: 5,
              activeCases: 0,
              availability: 'Available'
            },
            {
              id: 'customer',
              displayName: 'Alex Administrator',
              firstName: 'Alex',
              middleName: '',
              lastName: 'Administrator',
              email: 'admin@example.test',
              role: 'super_admin',
              status: 'Active',
              capacity: 5,
              activeCases: 0,
              availability: 'Available'
            }
          ]
        })
      )
      await page.goto('/admin/users')
      const user = page.getByRole('button', { name: 'Manage Taylor Specialist', exact: true })
      await expect(user).toBeVisible()
      const copy = user.locator('.user-directory-copy')
      expect(await copy.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(
        true
      )
      expect((await copy.boundingBox())!.x + (await copy.boundingBox())!.width).toBeLessThan(
        (await user.locator('svg').boundingBox())!.x
      )
      const note = page.locator('.user-admin-footnote')
      const icon = (await note.locator('svg').boundingBox())!,
        text = (await note.locator('span').boundingBox())!
      expect(text.x - icon.x - icon.width).toBeGreaterThanOrEqual(8)
      expect(Math.abs(icon.y - text.y)).toBeLessThan(4)
      await page.screenshot({ path: info.outputPath('user-directory.png'), fullPage: true })
      await user.click()
      const dialog = page.locator('.user-editor')
      await expect(dialog).toBeVisible()
      const role = (await dialog.getByRole('button', { name: /^Role:/ }).boundingBox())!
      const first = (await dialog.getByLabel('First Name', { exact: true }).boundingBox())!
      const middle = (await dialog.getByLabel(/Middle Name/).boundingBox())!
      const last = (await dialog.getByLabel('Surname', { exact: true }).boundingBox())!
      expect(Math.abs(first.x - role.x)).toBeLessThan(2)
      for (const field of [first, middle, last]) expect(field.height).toBeGreaterThanOrEqual(44)
      if (width < 600) {
        for (const field of [middle, last]) {
          expect(Math.abs(field.x - first.x)).toBeLessThan(2)
          expect(Math.abs(field.width - first.width)).toBeLessThan(2)
        }
        const capacity = (await dialog.getByLabel('Case Capacity', { exact: true }).boundingBox())!
        expect(Math.abs(capacity.x - role.x)).toBeLessThan(2)
        expect(Math.abs(capacity.width - role.width)).toBeLessThan(2)
      } else {
        expect(Math.abs(last.x + last.width - role.x - role.width)).toBeLessThan(2)
        expect(Math.abs(first.width - middle.width)).toBeLessThan(2)
      }
      expect(await dialog.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(
        true
      )
      await page.screenshot({ path: info.outputPath('access-editor.png') })
      await dialog.getByRole('button', { name: 'Close User Editor', exact: true }).click()
      await page.getByRole('button', { name: 'Manage Alex Administrator', exact: true }).click()
      await expect(dialog.getByLabel('First Name', { exact: true })).toBeDisabled()
      await expect(dialog.getByLabel('First Name', { exact: true })).toHaveCSS('opacity', '1')
      await expect(dialog.getByRole('button', { name: 'Save Access', exact: true })).toHaveCount(0)
      await dialog.getByRole('button', { name: 'Done', exact: true }).click()
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true
      )
    })
  }
}
for (const theme of ['light', 'dark']) {
  test(`sign-in fields have aligned labels and breathing room in ${theme} mode`, async ({
    page
  }, info) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.addInitScript((value) => localStorage.setItem('portal-theme', value), theme)
    await mockScreens(page, 'anonymous')
    await page.goto('/sign-in')
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
    const email = page.getByLabel('Email Or Mobile Number', { exact: true })
    const password = page.getByLabel('Password', { exact: true })
    await expect(email).toBeVisible()
    const a = (await email.boundingBox())!,
      b = (await password.boundingBox())!
    expect(Math.abs(a.x - b.x)).toBeLessThan(2)
    expect(Math.abs(a.width - b.width)).toBeLessThan(2)
    const fields = page.locator('.sign-in-field')
    const first = (await fields.first().boundingBox())!,
      last = (await fields.last().boundingBox())!
    expect(last.y - first.y - first.height).toBeGreaterThanOrEqual(24)
    const label = (await page.locator('label[for="password"]').boundingBox())!
    expect(Math.abs(label.x - b.x)).toBeLessThan(2)
    await password.fill('SyntheticPassword!42')
    await page.getByRole('button', { name: 'Show password', exact: true }).click()
    await expect(password).toHaveAttribute('type', 'text')
    await page.getByRole('button', { name: 'Hide password', exact: true }).click()
    await expect(password).toHaveAttribute('type', 'password')
    await expect(page.getByRole('link', { name: 'Forgot Password?', exact: true })).toHaveAttribute(
      'href',
      '/forgot-password'
    )
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath('sign-in.png'), fullPage: true })
  })
}
