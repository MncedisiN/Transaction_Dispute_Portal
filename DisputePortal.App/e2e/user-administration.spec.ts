import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { test, expect } from '@playwright/test'

test.use({
  channel: process.env.E2E_BROWSER_CHANNEL,
  viewport: { width: 375, height: 740 },
  serviceWorkers: 'block'
})
test.setTimeout(60000)

test('compact user administration creates accounts and edits roles without exposing every form', async ({
  page
}) => {
  const actor = {
    id: 'admin',
    displayName: 'Super Admin',
    roles: ['super_admin'],
    permissions: ['disputes.read', 'disputes.manage', 'operations.manage', 'users.manage']
  }
  const users = [
    {
      id: 'admin',
      displayName: 'Super Admin',
      email: 'admin@example.test',
      role: 'super_admin',
      status: 'Active',
      capacity: 5,
      activeCases: 0,
      availability: 'Offline'
    },
    {
      id: 'agent',
      displayName: 'Alex Specialist',
      email: 'alex@example.test',
      role: 'dispute_agent',
      status: 'Active',
      capacity: 5,
      activeCases: 0,
      availability: 'Available'
    },
    {
      id: 'busy',
      displayName: 'Busy Specialist',
      email: 'busy@example.test',
      role: 'fraud_agent',
      status: 'Active',
      capacity: 5,
      activeCases: 2,
      availability: 'Available'
    }
  ]
  let created = false,
    updated = false
  await page.route('**/api/v1/**', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })

    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/auth/csrf'))
      return route.fulfill({ json: { requestToken: 'fixture-csrf' } })
    if (path.endsWith('/auth/me')) return route.fulfill({ json: actor })
    if (path.endsWith('/notifications')) return route.fulfill({ json: { items: [], total: 0 } })
    if (path.endsWith('/operations/users') && route.request().method() === 'POST') {
      const data = route.request().postDataJSON()
      expect(data.displayName).toBe('New Investigator')
      expect(data.email).toBe('new@example.test')
      expect(data.role).toBe('payments_specialist')
      created = true
      users.push({
        id: 'new',
        displayName: data.displayName,
        email: data.email,
        role: data.role,
        status: 'Active',
        capacity: 5,
        activeCases: 0,
        availability: 'Offline'
      })
      return route.fulfill({ status: 201, json: { id: 'new' } })
    }
    if (path.endsWith('/operations/users/agent')) {
      const data = route.request().postDataJSON()
      expect(data).toEqual({ role: 'card_specialist', status: 'Active', capacity: 8 })
      Object.assign(users[1]!, data)
      updated = true
      return route.fulfill({ status: 204 })
    }
    if (path.endsWith('/operations/users')) return route.fulfill({ json: users })
    return route.fulfill({ status: 404, json: {} })
  })
  await page.goto('/admin/users')
  await expect(page.getByRole('button', { name: 'Manage Alex Specialist' })).toBeVisible()
  await expect(page.locator('.user-editor')).not.toBeVisible()
  await page.getByRole('button', { name: 'Add User', exact: true }).click()
  const editor = page.locator('.user-editor')
  await editor.getByLabel('First Name', { exact: true }).fill('New')
  await editor.getByLabel('Surname', { exact: true }).fill('Investigator')
  await editor.getByLabel('Email Address').fill('new@example.test')
  await editor.getByRole('button', { name: /^Role:/ }).click()
  await page
    .getByRole('dialog', { name: 'Role', exact: true })
    .getByRole('button', { name: 'Payments investigator', exact: true })
    .click()
  await editor.getByLabel('Initial Password').fill('SyntheticPass!2026')
  await editor.getByRole('button', { name: 'Create User' }).click()
  await expect(editor).not.toBeVisible()
  expect(created).toBe(true)
  await page.getByLabel('Search Users').fill('alex')
  await expect(page.locator('.user-directory-row')).toHaveCount(1)
  await page.getByRole('button', { name: 'Manage Alex Specialist' }).click()
  await expect(editor.getByLabel('First Name', { exact: true })).toHaveValue('')
  await expect(editor.getByLabel('First Name', { exact: true })).not.toHaveAttribute('required', '')
  await editor.getByRole('button', { name: /^Role:/ }).click()
  await page
    .getByRole('dialog', { name: 'Role', exact: true })
    .getByRole('button', { name: 'Card specialist', exact: true })
    .click()
  await editor.getByLabel('Case Capacity').fill('8')
  await editor.getByRole('button', { name: 'Save Access' }).click()
  await expect(editor).not.toBeVisible()
  expect(updated).toBe(true)
  await page.getByLabel('Search Users').fill('busy')
  await page.getByRole('button', { name: 'Manage Busy Specialist' }).click()
  await expect(editor.getByRole('button', { name: /^Role:/ })).toBeDisabled()
  await expect(editor.getByRole('button', { name: /^Account status:/ })).toBeDisabled()
  await expect(
    editor.getByText('Reassign this user’s open cases before changing their role or status.')
  ).toBeVisible()
  await editor.getByRole('button', { name: 'Cancel', exact: true }).click()
  await page.getByLabel('Search Users').fill('admin')
  await page.getByRole('button', { name: 'Manage Super Admin' }).click()
  await expect(editor.getByRole('button', { name: /^Role:/ })).toBeDisabled()
  await expect(editor.getByRole('button', { name: 'Save Access' })).toHaveCount(0)
  await page.keyboard.press('Escape')
  await expect(editor).not.toBeVisible()
  await page.getByLabel('Search Users').fill('')
  await page.getByLabel('Filter Users By Role').selectOption('payments_specialist')
  await expect(page.locator('.user-directory-row')).toHaveCount(1)
  await expect(page.getByRole('button', { name: 'Manage New Investigator' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
