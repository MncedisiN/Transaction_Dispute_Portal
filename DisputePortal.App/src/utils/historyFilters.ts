export interface HistoryFilters {
  status: string
  account: string
  from: string
  to: string
  unreadOnly: boolean
  priority?: string
}
export const emptyHistoryFilters = (): HistoryFilters => ({
  status: '',
  account: '',
  from: '',
  to: '',
  unreadOnly: false
})
export function historyFilterQuery(value: HistoryFilters) {
  return {
    priority: value.priority || undefined,
    status: value.status || undefined,
    from: value.from || undefined,
    to: value.to || undefined,
    unreadOnly: value.unreadOnly ? 'true' : undefined
  }
}
