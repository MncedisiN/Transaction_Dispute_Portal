import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
  type HubConnection
} from '@microsoft/signalr'
import { readonly, shallowRef } from 'vue'
import { refreshCsrfToken } from './api'

export const liveTopics = [
  'disputes',
  'support',
  'banking',
  'notifications',
  'profile',
  'administration'
] as const
export type LiveTopic = (typeof liveTopics)[number]
export interface LiveChange {
  topics: readonly LiveTopic[]
  resourceId?: string
  occurredAt: string
  reconnected?: boolean
}
const state = shallowRef<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>(
  'disconnected'
)
export const liveConnectionState = readonly(state)
const listeners = new Set<(change: LiveChange) => void>()
let activeConnection: HubConnection | undefined
let activeUser: string | null = null
let generation = 0
let retry: ReturnType<typeof setTimeout> | undefined
let failures = 0
let startingFor: number | null = null

export function onLiveChange(listener: (change: LiveChange) => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}
function announce(change: LiveChange): void {
  for (const listener of [...listeners]) {
    try {
      listener(change)
    } catch {
      /* One workspace must not block another. */
    }
  }
}
function reconcile(): void {
  announce({ topics: liveTopics, occurredAt: new Date().toISOString(), reconnected: true })
}
export function parseLiveChange(value: unknown): LiveChange | null {
  if (!value || typeof value !== 'object') return null
  const data = value as Record<string, unknown>
  if (!Array.isArray(data.topics)) return null
  const topics = [
    ...new Set(
      data.topics.filter((topic): topic is LiveTopic => liveTopics.includes(topic as LiveTopic))
    )
  ]
  if (
    !topics.length ||
    typeof data.occurredAt !== 'string' ||
    !Number.isFinite(Date.parse(data.occurredAt))
  )
    return null
  const resourceId =
    typeof data.resourceId === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(data.resourceId)
      ? data.resourceId
      : undefined
  return { topics, resourceId, occurredAt: data.occurredAt }
}
function scheduleConnection(): void {
  if (retry || !activeUser || !navigator.onLine || document.visibilityState === 'hidden') return
  const delay =
    Math.min(30_000, 1000 * 2 ** Math.min(failures++, 5)) + Math.floor(Math.random() * 500)
  retry = setTimeout(() => {
    retry = undefined
    void connect()
  }, delay)
}
async function connect(): Promise<void> {
  if (!activeUser || activeConnection || startingFor === generation || !navigator.onLine) return
  const expected = generation
  startingFor = expected
  state.value = 'connecting'
  try {
    const csrf = await refreshCsrfToken()
    if (expected !== generation || !activeUser) return
    const connection = new HubConnectionBuilder()
      .withUrl('/api/v1/realtime', {
        transport: HttpTransportType.WebSockets,
        withCredentials: true,
        headers: { 'X-CSRF-TOKEN': csrf }
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) =>
          Math.min(30_000, 1000 * 2 ** Math.min(context.previousRetryCount, 5)) +
          Math.floor(Math.random() * 500)
      })
      .withStatefulReconnect({ bufferSize: 10_000 })
      .configureLogging(LogLevel.None)
      .build()
    connection.serverTimeoutInMilliseconds = 60_000
    connection.keepAliveIntervalInMilliseconds = 15_000
    activeConnection = connection
    connection.on('SessionEnded', () => {
      if (expected === generation) window.dispatchEvent(new Event('portal:session-ended'))
    })
    connection.on('Changed', (value: unknown) => {
      if (expected !== generation) return
      const change = parseLiveChange(value)
      if (change) announce(change)
    })
    connection.onreconnecting(() => {
      if (expected === generation) state.value = 'reconnecting'
    })
    connection.onreconnected(() => {
      if (expected !== generation) return
      failures = 0
      state.value = 'connected'
      reconcile()
    })
    connection.onclose(() => {
      if (activeConnection === connection) activeConnection = undefined
      if (expected !== generation) return
      window.dispatchEvent(new Event('portal:session-check'))
      state.value = 'disconnected'
      scheduleConnection()
    })
    await connection.start()
    if (expected !== generation) {
      await connection.stop()
      return
    }
    failures = 0
    state.value = 'connected'
    reconcile()
  } catch {
    if (expected !== generation) return
    const failed = activeConnection
    activeConnection = undefined
    await failed?.stop().catch(() => undefined)
    state.value = 'disconnected'
    scheduleConnection()
  } finally {
    if (startingFor === expected) startingFor = null
  }
}
export async function setLiveUser(userId: string | null): Promise<void> {
  if (activeUser === userId) {
    if (userId && !activeConnection) void connect()
    return
  }
  generation++
  activeUser = userId
  clearTimeout(retry)
  retry = undefined
  failures = 0
  const previous = activeConnection
  activeConnection = undefined
  state.value = 'disconnected'
  await previous?.stop().catch(() => undefined)
  if (userId) void connect()
}
export function resumeLiveUpdates(): void {
  if (activeUser && !activeConnection && document.visibilityState !== 'hidden') {
    clearTimeout(retry)
    retry = undefined
    void connect()
  }
}
export function notifyPushUpdate(): void {
  announce({
    topics: ['notifications', 'disputes', 'banking'],
    occurredAt: new Date().toISOString()
  })
}
export async function disposeLiveUpdates(): Promise<void> {
  await setLiveUser(null)
  listeners.clear()
}
