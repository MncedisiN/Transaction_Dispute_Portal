import type { Route } from '@playwright/test'

// Match the HTTP array contract and pagination headers used by the real API.
export function fulfillJson(route: Route, data: unknown): Promise<void> {
  const request = route.request()
  const url = new URL(request.url())
  if (request.method() === 'GET' && url.pathname === '/api/v1/support' && Array.isArray(data)) {
    const page = Math.max(1, Number(url.searchParams.get('page')) || 1)
    const pageSize = Math.min(100, Math.max(1, Number(url.searchParams.get('pageSize')) || 20))
    return route.fulfill({
      json: data.slice((page - 1) * pageSize, page * pageSize),
      headers: {
        'X-Total-Count': String(data.length),
        'X-Page': String(page),
        'X-Page-Size': String(pageSize)
      }
    })
  }
  return route.fulfill({ json: data })
}
