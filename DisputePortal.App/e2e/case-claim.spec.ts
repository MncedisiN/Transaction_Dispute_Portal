import { fulfillJson } from './fixtures/fulfillJson'
import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { test, expect } from '@playwright/test'
test.use({
  channel: process.env.E2E_BROWSER_CHANNEL,
  viewport: { width: 375, height: 740 },
  serviceWorkers: 'block'
})
// API fixtures isolate UI access states; repository integration tests enforce the real locks.
test('chat unlocks after an eligible claim and stays locked for unqualified or assigned work', async ({
  page
}) => {
  const user = {
    id: 'agent',
    displayName: 'Specialist',
    roles: ['dispute_agent'],
    permissions: ['disputes.read', 'disputes.manage']
  }
  let capacity = 5
  const activeCases = 1
  let assignedTo: string | null = null,
    queueType = 'General',
    claims = 0
  const caseId = '40000000-0000-0000-0000-000000000001',
    threadId = '50000000-0000-0000-0000-000000000001'
  const thread = {
    id: threadId,
    disputeId: caseId,
    reference: 'DP-TEST-001',
    subject: 'Case support',
    caseTitle: 'Fresh Market',
    caseCreatedAt: '2026-10-30T10:00:00Z',
    createdAt: '2026-10-30T10:00:00Z',
    lastMessageAt: '2026-10-30T11:00:00Z',
    lastReadSequence: 1,
    unreadCount: 0
  }
  await page.route('**/api/v1/**', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })
    const url = new URL(route.request().url()),
      p = url.pathname
    let data = {}
    if (p.endsWith('/auth/csrf')) data = { requestToken: 'browser-test' }
    else if (p.endsWith('/auth/me')) data = user
    else if (p.endsWith('/notifications')) data = { items: [], total: 0, page: 1, pageSize: 20 }
    else if (p.endsWith('/operations/staff'))
      data = [{ ...user, status: 'Active', availability: 'Available', capacity, activeCases }]
    else if (p.endsWith('/claim')) {
      expect(route.request().postDataJSON().expectedVersion).toBe(7)
      claims++
      assignedTo = user.id
      return route.fulfill({ status: 204 })
    } else if (p.includes('/operations/cases/'))
      data = {
        id: caseId,
        queueType,
        status: 'Submitted',
        assignedTo,
        assignedName: assignedTo ? 'Assigned specialist' : null,
        version: 7
      }
    else if (p.endsWith('/messages'))
      data = [
        {
          id: 'message',
          sequence: 1,
          authorId: 'customer',
          authorName: 'Alex',
          body: 'Please review this transaction.',
          createdAt: '2026-10-30T11:00:00Z'
        }
      ]
    else if (p.endsWith('/read')) return route.fulfill({ status: 204 })
    else if (p.endsWith('/support')) data = [thread]
    else if (p.endsWith('/support/' + threadId)) data = thread
    return fulfillJson(route, data)
  })
  const open = async () => {
    await page.goto('/support?thread=' + threadId)
    await expect(
      page
        .getByRole('log', { name: 'Conversation Messages' })
        .getByText('Please review this transaction.', { exact: true })
    ).toBeVisible()
  }
  await open()
  await expect(page.getByRole('button', { name: 'Take this case' })).toBeEnabled()
  await page.getByRole('button', { name: 'Take this case' }).click()
  await expect(page.getByLabel('Your Message')).toBeVisible()
  expect(claims).toBe(1)
  assignedTo = null
  queueType = 'Fraud'
  await open()
  await expect(page.getByRole('button', { name: 'Case locked' })).toBeDisabled()
  await expect(page.getByText('Requires the fraud investigation team.')).toBeVisible()
  expect(await page.getByLabel('Your Message').count()).toBe(0)
  queueType = 'General'
  capacity = 1
  await open()
  await expect(page.getByRole('button', { name: 'Case locked' })).toBeDisabled()
  await expect(page.getByText('Your case capacity is full.')).toBeVisible()
  capacity = 5
  assignedTo = 'other'
  await open()
  await expect(page.getByText('A supervisor can arrange a handover.')).toBeVisible()
  expect(await page.getByLabel('Your Message').count()).toBe(0)
  expect(claims).toBe(1)
})
