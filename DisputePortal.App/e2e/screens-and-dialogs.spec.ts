import { expect, test, type Page } from '@playwright/test'
import { mockScreens } from './fixtures/screens'

test.describe.configure({ mode: 'parallel' })

const customerScreens = [
  '/',
  '/personal',
  '/transactions',
  '/transactions/payment',
  '/disputes',
  '/disputes/new/payment',
  '/disputes/case',
  '/accounts/manage',
  '/accounts/account',
  '/profile',
  '/profile?section=security',
  '/profile?section=alerts',
  '/notifications',
  '/support'
]
const staffScreens = [
  '/admin/disputes',
  '/admin/disputes/case',
  '/admin/disputes/case?section=workflow',
  '/admin/disputes/case?section=activity',
  '/admin/disputes/case?section=evidence',
  '/admin/transactions',
  '/admin/transactions/payment',
  '/admin/dispute-rules',
  '/admin/dispute-operations',
  '/admin/account-requests',
  '/admin/users',
  '/admin/monitoring',
  '/admin/audit'
]

async function bounded(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
    page.viewportSize()!.width + 1
  )
  const outside = await page
    .locator('input:not([type="hidden"]), textarea, select, button, a.w3-button')
    .evaluateAll((elements) =>
      elements
        .filter((element) => {
          const box = element.getBoundingClientRect(),
            style = getComputedStyle(element)
          if (
            !element.checkVisibility() ||
            !box.width ||
            !box.height ||
            style.visibility === 'hidden' ||
            element.closest('.w3-responsive')
          )
            return false
          return box.left < -1 || box.right > innerWidth + 1
        })
        .map((element) => ({
          tag: element.tagName,
          bounds: JSON.stringify(element.getBoundingClientRect().toJSON()),
          boxSizing: getComputedStyle(element).boxSizing,
          parent: element.parentElement?.className,
          text: (element.textContent || element.getAttribute('aria-label') || '')
            .trim()
            .slice(0, 90)
        }))
    )
  expect(outside).toEqual([])
  const clippedIcons = await page.locator('button:visible').evaluateAll((buttons) =>
    buttons.flatMap((button) => {
      const bounds = button.getBoundingClientRect()
      return [...button.querySelectorAll('svg')]
        .filter((icon) => icon.checkVisibility())
        .filter((icon) => {
          const box = icon.getBoundingClientRect()
          return (
            box.width && box.height && (box.left < bounds.left - 1 || box.right > bounds.right + 1)
          )
        })
        .map(() =>
          (button.getAttribute('aria-label') || button.textContent || '').trim().slice(0, 80)
        )
    })
  )
  expect(clippedIcons).toEqual([])
}
async function dialogFits(page: Page) {
  const dialog = page.locator('dialog[open]').last()
  await expect(dialog).toBeVisible()
  const box = await dialog.boundingBox()
  expect(box).not.toBeNull()
  expect(box!.x).toBeGreaterThanOrEqual(0)
  expect(box!.y).toBeGreaterThanOrEqual(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(page.viewportSize()!.width + 1)
  expect(box!.y + box!.height).toBeLessThanOrEqual(page.viewportSize()!.height + 1)
  return dialog
}

for (const width of [375, 1440]) {
  for (const [mode, screens] of [
    ['customer', customerScreens],
    ['staff', staffScreens],
    ['anonymous', ['/sign-in', '/register', '/forgot-password']]
  ] as const) {
    for (const path of screens) {
      test(`${mode} screen ${path} fits at ${width}px`, async ({ page }, info) => {
        await page.setViewportSize({ width, height: 900 })
        const failures: string[] = []
        page.on('pageerror', (error) => failures.push(error.message))
        const state = await mockScreens(page, mode)
        await page.goto(path)
        await expect(page.locator('h1:visible, h2:visible').first()).toBeVisible()
        await page.waitForTimeout(200)
        await bounded(page)
        expect(failures).toEqual([])
        await info.attach('unhandled-fixture-routes', {
          body: JSON.stringify([...state.unknown]),
          contentType: 'application/json'
        })
        await page.screenshot({ path: info.outputPath('screen.png') })
      })
    }
  }

  test(`filters, appearance and profile dialogs work at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page)
    for (const path of ['/transactions', '/disputes', '/support']) {
      await page.goto(path)
      await page.getByRole('button', { name: 'Filters', exact: true }).click()
      const dialog = await dialogFits(page)
      await bounded(page)
      await page.keyboard.press('Escape')
      await expect(dialog).not.toBeVisible()
    }
    await page.getByRole('button', { name: 'Profile Menu' }).click()
    await page.getByRole('button', { name: 'Appearance', exact: true }).click()
    const appearance = await dialogFits(page)
    for (const name of [
      'Dark Easy on the eyes',
      'Light Bright and clear',
      'Use device setting Matches your device'
    ])
      await appearance.getByRole('button', { name, exact: true }).click()
    await appearance.getByRole('button', { name: 'Close Appearance' }).click()
    await page.goto('/profile?section=security')
    await page.getByRole('button', { name: 'Request Account Closure', exact: true }).click()
    const closure = await dialogFits(page)
    await closure.getByLabel('Current Password', { exact: true }).fill('SyntheticOnly!2026')
    await page.keyboard.press('Escape')
    await expect(closure).not.toBeVisible()
    await bounded(page)
  })

  test(`dispute review, guided enquiry and evidence dialogs work at ${width}px`, async ({
    page
  }) => {
    await page.setViewportSize({ width, height: 900 })
    const state = await mockScreens(page)
    await page.goto('/disputes/new/payment')
    await page.getByRole('radio', { name: 'I was charged more than once', exact: true }).check()
    await page
      .getByLabel('Add A Few Details', { exact: true })
      .fill('The same payment appears twice on my statement.')
    await page.getByRole('button', { name: 'Review Your Dispute', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Submit Dispute', exact: true })).toBeVisible()
    await bounded(page)
    await page.getByRole('button', { name: 'Edit Details', exact: true }).click()
    await expect(page.getByLabel('Add A Few Details', { exact: true })).toHaveValue(
      'The same payment appears twice on my statement.'
    )
    await page.goto('/disputes/case')
    await page.getByRole('button', { name: 'Evidence', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Access', exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: 'Remove File', exact: true }).click()
    const removal = await dialogFits(page)
    await removal.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(removal).not.toBeVisible()
    await page.goto('/support?new=1')
    await page.getByRole('button', { name: 'Something Else', exact: true }).click()
    const message = page.getByLabel('Your Message', { exact: true })
    await message.fill('I need help understanding the next step for my enquiry.')
    await page.getByRole('button', { name: 'Continue', exact: true }).click()
    if (await page.getByRole('button', { name: 'Skip', exact: true }).isVisible())
      await page.getByRole('button', { name: 'Skip', exact: true }).click()
    else if (await message.isVisible())
      await page.getByRole('button', { name: 'Continue', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Submit Enquiry', exact: true })).toBeVisible()
    await bounded(page)
    await page.getByRole('button', { name: 'Submit Enquiry', exact: true }).click()
    await expect(page.getByRole('log', { name: 'Conversation Messages' })).toBeVisible()
    expect(state.requests.some((request) => request.path === '/api/v1/support')).toBe(true)
  })

  test(`staff editors and nested choices work at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page, 'staff')
    await page.goto('/admin/users')
    await page.getByRole('button', { name: 'Add User', exact: true }).click()
    const editor = await dialogFits(page)
    await editor.getByLabel('First Name', { exact: true }).fill('New')
    await editor.getByLabel('Surname', { exact: true }).fill('Specialist')
    await editor.getByRole('button', { name: /^Role:/ }).click()
    await dialogFits(page)
    const role = page.getByRole('dialog', { name: 'Role', exact: true })
    await page.keyboard.press('Escape')
    await expect(role).not.toBeVisible()
    await expect(editor).toBeVisible()
    await editor.getByRole('button', { name: 'Close User Editor', exact: true }).click()
    await page.goto('/admin/disputes/case?section=workflow')
    await page.getByRole('button', { name: 'Assign', exact: true }).click()
    await page.getByRole('button', { name: /^Case Owner:/ }).click()
    await dialogFits(page)
    await page.keyboard.press('Escape')
    await page
      .getByLabel('Assignment Reason', { exact: true })
      .fill('Assigning this case to the qualified specialist for review.')
    await bounded(page)
    await page.goto('/admin/disputes/case?section=evidence')
    await page.getByRole('button', { name: 'Access', exact: true }).click()
    const access = await dialogFits(page)
    await access.getByRole('button', { name: /^Access Duration:/ }).click()
    await dialogFits(page)
    await page.keyboard.press('Escape')
    await access.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(access).not.toBeVisible()
  })

  test(`registration country choice and second step work at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page, 'anonymous')
    await page.goto('/register')
    await page.getByLabel('First Name', { exact: true }).fill('Alex')
    await page.getByLabel('Surname', { exact: true }).fill('Customer')
    await page.getByLabel('Email Address', { exact: true }).fill('alex@example.test')
    await page.getByLabel('Mobile Country Code', { exact: true }).click()
    await page
      .getByRole('searchbox', { name: 'Search mobile country code', exact: true })
      .fill('South Africa')
    await page.getByRole('option', { name: /South Africa/ }).click()
    await page.getByLabel('Mobile Number', { exact: true }).fill('821234567')
    await page.getByRole('button', { name: 'Continue', exact: true }).click()
    await expect(page.getByLabel('Identity Document', { exact: true })).toBeVisible()
    await expect(page.getByText('Birth Century', { exact: true })).toHaveCount(0)
    await expect(page.getByText('Residential Address', { exact: true })).toHaveCount(0)
    await page.getByLabel('Create A Password', { exact: true }).fill('SyntheticOnly!2026')
    await page.getByLabel('Confirm Password', { exact: true }).fill('SyntheticOnly!2026')
    await bounded(page)
    await page.getByRole('button', { name: 'Back To Your Details', exact: true }).click()
    await expect(page.getByLabel('First Name', { exact: true })).toHaveValue('Alex')
  })

  for (const mode of ['customer', 'staff'] as const) {
    test(`${mode} can send and close a conversation at ${width}px`, async ({ page }, info) => {
      await page.setViewportSize({ width, height: 900 })
      const state = await mockScreens(page, mode)
      if (mode === 'staff') await page.goto('/admin/disputes')
      await page.goto('/support?thread=open-thread')
      await expect(page.getByRole('log', { name: 'Conversation Messages' })).toContainText(
        'Your message is with our specialist.'
      )
      await expect(page.getByRole('navigation', { name: 'Mobile Navigation' })).not.toBeVisible()
      await expect(
        page.getByText(mode === 'staff' ? 'Staff Workspace' : 'Customer Workspace', { exact: true })
      ).toBeVisible()
      await expect(
        page.getByRole('link', { name: 'Open Linked Transaction', exact: true })
      ).toHaveAttribute(
        'href',
        mode === 'staff' ? '/admin/transactions/payment' : '/transactions/payment'
      )
      await expect(
        page.getByRole('link', { name: 'Open Linked Case', exact: true })
      ).toHaveAttribute('href', mode === 'staff' ? '/admin/disputes/case' : '/disputes/case')
      const input = page.getByLabel('Your Message', { exact: true })
      await input.fill('Please confirm the next review step.\nThank you.')
      await page.setViewportSize({ width, height: 460 })
      await input.focus()
      await expect(input).toBeFocused()
      await expect
        .poll(async () => {
          const box = await input.boundingBox()
          return box!.y >= 0 && box!.y + box!.height <= 460
        })
        .toBe(true)
      await page.setViewportSize({ width, height: 900 })
      await page.getByRole('button', { name: 'Send Message', exact: true }).click()
      await expect(page.getByRole('log')).toContainText('Please confirm the next review step.')
      await page.getByRole('button', { name: 'Conversation Actions', exact: true }).click()
      await page.getByRole('button', { name: 'Close Conversation', exact: true }).click()
      const confirmation = await dialogFits(page)
      if (mode === 'staff')
        await confirmation
          .getByLabel('Reason', { exact: true })
          .fill('The enquiry was answered and confirmed by the customer.')
      await confirmation.getByRole('button', { name: 'Cancel', exact: true }).click()
      await expect(confirmation).not.toBeVisible()
      await expect(input).toHaveValue('')
      await page.getByRole('button', { name: 'Conversation Actions', exact: true }).click()
      await page.getByRole('button', { name: 'Close Conversation', exact: true }).click()
      await dialogFits(page)
      if (mode === 'staff')
        await confirmation
          .getByLabel('Reason', { exact: true })
          .fill('The enquiry was answered and confirmed by the customer.')
      await confirmation.getByRole('button', { name: 'Confirm Closure', exact: true }).click()
      await expect(input).toHaveCount(0)
      expect(state.thread.status).toBe('Closed')
      await expect(page.getByRole('log')).toContainText('Please confirm the next review step.')
      await bounded(page)
      await page.screenshot({ path: info.outputPath('closed-chat.png') })
    })
  }
}

for (const width of [375, 1440]) {
  test(`sign-in feedback remains compact and dismissible at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockScreens(page, 'anonymous')
    await page.route('**/api/v1/auth/sign-in', (route) =>
      route.fulfill({
        status: 401,
        json: { title: 'Sign-in failed', detail: 'Check your sign-in details and try again.' }
      })
    )
    await page.goto('/sign-in')
    await page.getByLabel('Email Or Mobile Number').fill('reviewer@example.test')
    await page.getByLabel('Password', { exact: true }).fill('Example!Password42')
    await page.getByRole('button', { name: 'Sign In Securely', exact: true }).click()
    const toast = page.locator('.app-toast').first()
    await expect(toast).toBeVisible()
    const box = await toast.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(width)
    expect(box!.y).toBeLessThan(80)
    expect(box!.height).toBeLessThan(250)
    await page.getByRole('button', { name: 'Dismiss Notification' }).click()
    await expect(toast).toHaveCount(0)
  })
}
