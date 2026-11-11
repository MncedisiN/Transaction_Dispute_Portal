import { notify } from './toasts'
import { currentWorkspace } from './workspaceState'
import type { ApiProblem, PageResult } from './types'
import { hasRecentUserActivity } from './userActivity'

let unauthorizedHandler: (() => void) | undefined
export function onUnauthorized(handler?: () => void): void {
  unauthorizedHandler = handler
}

export function assertLocalApiPath(path: string): void {
  if (
    !path.startsWith('/api/v1/') ||
    path.includes('\\') ||
    Array.from(path).some((char) => char.charCodeAt(0) < 32)
  )
    throw new Error('Only local API requests are allowed.')
}

let sessionEpoch = 0
let csrfToken: string | null = null
let csrfRefresh: Promise<string> | null = null

export function setCsrfToken(token: string | null): void {
  csrfToken = token
  if (token === null) {
    sessionEpoch++
    csrfRefresh = null
  }
}

function assertSessionEpoch(expected: number): void {
  if (expected !== sessionEpoch)
    throw new DOMException('The session changed. Please try again.', 'AbortError')
}

export class ApiError extends Error {
  status: number
  fields: Record<string, string[]>

  constructor(status: number, message: string, fields: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fields = fields
  }
}

export function refreshCsrfToken(): Promise<string> {
  if (!csrfRefresh) {
    const expected = sessionEpoch
    const pending: Promise<string> = request<{ requestToken: string }>(
      '/api/v1/auth/csrf',
      { cache: 'no-store' },
      false,
      'json',
      expected
    )
      .then(({ requestToken }) => {
        assertSessionEpoch(expected)
        if (!requestToken) throw new Error('Secure session could not be refreshed.')
        setCsrfToken(requestToken)
        return requestToken
      })
      .finally(() => {
        if (csrfRefresh === pending) csrfRefresh = null
      })
    csrfRefresh = pending
  }
  return csrfRefresh
}

async function request<T>(
  path: string,
  init: RequestInit = {},
  retryCsrf = true,
  responseKind: 'json' | 'blob' | 'collection' | 'cursor' = 'json',
  requestEpoch = sessionEpoch
): Promise<T> {
  assertLocalApiPath(path)
  assertSessionEpoch(requestEpoch)
  const method = (init.method ?? 'GET').toUpperCase()
  const headers = new Headers(init.headers)
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type'))
    headers.set('Content-Type', 'application/json')
  const mutation = !['GET', 'HEAD', 'OPTIONS'].includes(method)
  if (mutation) {
    if (!csrfToken) await refreshCsrfToken()
    assertSessionEpoch(requestEpoch)
    headers.set('X-CSRF-TOKEN', csrfToken!)
  }
  headers.set('X-Portal-Workspace', currentWorkspace())
  headers.set('X-Portal-Activity', hasRecentUserActivity() ? '1' : '0')
  const response = await fetch(path, {
    ...init,
    method,
    headers,
    credentials: 'include',
    cache: 'no-store',
    redirect: 'error'
  })
  assertSessionEpoch(requestEpoch)
  if (response.status === 204) return undefined as T
  if (response.ok && responseKind === 'blob') {
    const blob = await response.blob()
    assertSessionEpoch(requestEpoch)
    return blob as T
  }
  const body = (await response.json().catch(() => ({}))) as ApiProblem
  assertSessionEpoch(requestEpoch)
  // This rejection occurs before the controller runs. Never replay other failures.
  if (mutation && retryCsrf && response.status === 400 && body.code === 'csrf_validation_failed') {
    await refreshCsrfToken()
    assertSessionEpoch(requestEpoch)
    return request<T>(path, init, false, responseKind, requestEpoch)
  }
  if (
    response.status === 401 &&
    requestEpoch === sessionEpoch &&
    !path.startsWith('/api/v1/auth/sign-in') &&
    !path.startsWith('/api/v1/identity/')
  )
    unauthorizedHandler?.()
  if (!response.ok) {
    const fields = body.errors ?? {}
    const validationMessage = [400, 422].includes(response.status)
      ? Object.values(fields)
          .flat()
          .find((value) => typeof value === 'string' && value.length <= 500)
      : undefined
    const rawMessage =
      validationMessage ??
      body.detail ??
      body.title ??
      'Please try again shortly. If this continues, contact support.'
    const paymentRequest = /^\/api\/v1\/platform\/payments(?:\/|$)/.test(path)
    const message =
      paymentRequest && /insufficient|not enough funds/i.test((body.code ?? '') + ' ' + rawMessage)
        ? 'Insufficient funds. Choose a smaller amount or check your available balance.'
        : paymentRequest && /configured limits|currency precision/i.test(rawMessage)
          ? 'We cannot transfer this amount. Try a smaller amount and check the decimal places.'
          : rawMessage
    if (mutation) notify(message, 'error')
    throw new ApiError(response.status, message, fields)
  }
  if (responseKind === 'cursor') {
    const hasMore = response.headers.get('X-Has-More')
    if (!Array.isArray(body) || (hasMore !== 'true' && hasMore !== 'false'))
      throw new ApiError(502, 'Message history could not be read. Please try again.')
    return { items: body, hasMore: hasMore === 'true' } as T
  }
  if (responseKind === 'collection') {
    const total = Number(response.headers.get('X-Total-Count'))
    const page = Number(response.headers.get('X-Page'))
    const pageSize = Number(response.headers.get('X-Page-Size'))
    if (
      !Array.isArray(body) ||
      !Number.isSafeInteger(total) ||
      total < 0 ||
      !Number.isSafeInteger(page) ||
      page < 1 ||
      !Number.isSafeInteger(pageSize) ||
      pageSize < 1 ||
      response.headers.get('X-Total-Count') === null
    ) {
      throw new ApiError(502, 'The conversation list could not be read. Please try again.')
    }
    return { items: body, total, page, pageSize } as T
  }
  return body as T
}

function queryPath(path: string, params: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params))
    if (value !== undefined && value !== '') query.set(key, String(value))
  const encoded = query.toString()
  return path + (encoded ? '?' + encoded : '')
}

export const api = {
  cursorPage: <T>(path: string, signal?: AbortSignal) =>
    request<{ items: T[]; hasMore: boolean }>(path, { signal }, true, 'cursor'),
  upload: <T>(
    path: string,
    file: File,
    category: string,
    signal?: AbortSignal,
    original?: File
  ) => {
    const body = new FormData()
    body.append('file', file)
    body.append('category', category)
    if (original) body.append('original', original)
    return request<T>(path, { method: 'POST', body, signal })
  },
  download: (path: string, signal?: AbortSignal) => request<Blob>(path, { signal }, true, 'blob'),
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { signal }),
  post: <T>(path: string, body?: unknown, headers?: Record<string, string>, signal?: AbortSignal) =>
    request<T>(path, {
      method: 'POST',
      body: body === undefined ? undefined : JSON.stringify(body),
      headers,
      signal
    }),
  async page<T>(
    path: string,
    params: Record<string, string | number | undefined>,
    signal?: AbortSignal
  ): Promise<PageResult<T>> {
    return request<PageResult<T>>(queryPath(path, params), { signal })
  },
  collectionPage<T>(
    path: string,
    params: Record<string, string | number | undefined>,
    signal?: AbortSignal
  ): Promise<PageResult<T>> {
    return request<PageResult<T>>(queryPath(path, params), { signal }, true, 'collection')
  }
}
