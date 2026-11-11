import { expect, test, type Page } from '@playwright/test'
import reference from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'

async function mockInbox(page: Page) {
  const requests: URL[] = []
  const records = Array.from({ length: 101 }, (_, index) => ({
    id: 'thread-' + index,
    customerId: 'customer',
    customerName: 'Portal Customer',
    subject: 'Conversation ' + String(index + 1).padStart(3, '0'),
    status: 'Closed',
    createdAt: '2026-11-01T10:00:00Z',
    lastMessageAt: '2026-11-01T10:00:00Z',
    unreadCount: index === 100 ? 1 : 0,
    lastReadSequence: 0,
    canReply: false,
    canClose: false,
    canAssign: false,
    canEscalate: false
  }))
  await page.routeWebSocket('**/api/v1/realtime**', (socket) => socket.close())
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname
    if (path === '/api/v1/realtime') return route.abort()
    if (path === '/api/v1/portal/reference') return route.fulfill({ json: reference })
    if (path === '/api/v1/platform/settings') return route.fulfill({ json: platformSettings() })
    if (path === '/api/v1/platform/status') return route.fulfill({ json: { enabled: true } })
    if (path === '/api/v1/auth/me')
      return route.fulfill({
        json: {
          id: 'customer',
          displayName: 'Portal Customer',
          roles: ['customer'],
          permissions: []
        }
      })
    if (path === '/api/v1/auth/csrf')
      return route.fulfill({ json: { requestToken: 'isolated-browser-token' } })
    if (path === '/api/v1/notifications')
      return route.fulfill({ json: { items: [], page: 1, pageSize: 20, total: 0 } })
    if (path === '/api/v1/support/last-opened') return route.fulfill({ json: { threadId: null } })
    if (path === '/api/v1/support') {
      expect(request.method()).toBe('GET')
      requests.push(url)
      const current = Number(url.searchParams.get('page') ?? 1),
        size = 20
      const rows = records.filter(
        (row) =>
          (!url.searchParams.get('search') ||
            row.subject.toLowerCase().includes(url.searchParams.get('search')!.toLowerCase())) &&
          (url.searchParams.get('unreadOnly') !== 'true' || row.unreadCount > 0)
      )
      return route.fulfill({
        json: rows.slice((current - 1) * size, current * size),
        headers: {
          'X-Total-Count': String(rows.length),
          'X-Page': String(current),
          'X-Page-Size': String(size)
        }
      })
    }
    if (path.endsWith('/messages'))
      return route.fulfill({
        headers: { 'X-Has-More': 'false' },
        json: [
          {
            id: 'retained',
            sequence: 1,
            authorId: 'staff',
            authorName: 'Support',
            body: 'Your closed conversation remains readable.\nReference: ' + 'X'.repeat(140),
            createdAt: '2026-11-01T10:00:00Z'
          }
        ]
      })
    if (path.endsWith('/read')) return route.fulfill({ status: 204 })
    if (path.startsWith('/api/v1/support/thread-'))
      return route.fulfill({ json: records.find((row) => path.endsWith('/' + row.id)) })
    return route.fulfill({ json: [] })
  })
  return requests
}

for (const width of [390, 1440]) {
  test(`conversation history pages and stays readable at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 })
    const requests = await mockInbox(page)
    await page.goto('/support')
    const pager = page.getByRole('navigation', { name: 'Conversation Pages' })
    await expect(pager.getByText('Page 1 of 6', { exact: true })).toBeVisible()
    for (let current = 2; current <= 6; current++) {
      await pager.getByRole('button', { name: 'Next', exact: true }).click()
      await expect(pager.getByText(`Page ${current} of 6`, { exact: true })).toBeVisible()
    }
    await expect(page.getByRole('button', { name: /Conversation 101/ })).toBeVisible()
    await expect(pager.getByRole('button', { name: 'Next', exact: true })).toBeDisabled()
    expect(requests.at(-1)?.searchParams.get('ownOnly')).toBe('true')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath('last-page.png'), fullPage: true })
    await page.getByRole('button', { name: /Conversation 101/ }).click()
    await expect(page.getByRole('log', { name: 'Conversation Messages' })).toContainText(
      'Your closed conversation remains readable.'
    )
    await expect(page.getByLabel('Your Message', { exact: true })).toHaveCount(0)
    await expect(page.getByRole('navigation', { name: 'Conversation Pages' })).not.toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath('closed-conversation.png'), fullPage: true })
  })

  test(`conversation filters restart server paging at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    const requests = await mockInbox(page)
    await page.goto('/support')
    const pager = page.getByRole('navigation', { name: 'Conversation Pages' })
    await pager.getByRole('button', { name: 'Next', exact: true }).click()
    await expect(pager.getByText('Page 2 of 6', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Unread', exact: true }).click()
    await expect(page.getByRole('button', { name: /Conversation 101/ })).toBeVisible()
    expect(requests.at(-1)?.searchParams.get('unreadOnly')).toBe('true')
    expect(requests.at(-1)?.searchParams.get('page')).toBe('1')
    await expect(pager).toHaveCount(0)
    await page.getByRole('button', { name: 'All Conversations', exact: true }).click()
    await page
      .getByRole('searchbox', { name: 'Search Conversations', exact: true })
      .fill('Conversation 101')
    await page.getByRole('button', { name: 'Find Conversations', exact: true }).click()
    await expect(page.getByRole('button', { name: /Conversation 101/ })).toBeVisible()
    expect(requests.at(-1)?.searchParams.get('search')).toBe('Conversation 101')
    expect(requests.at(-1)?.searchParams.get('page')).toBe('1')
  })
}
