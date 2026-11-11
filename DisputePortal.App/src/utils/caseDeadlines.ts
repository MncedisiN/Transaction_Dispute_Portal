const offset = 2 * 60 * 60 * 1000
export function targetInput(value?: string | null): string {
  return value ? new Date(Date.parse(value) + offset).toISOString().slice(0, 16) : ''
}
export function targetInstant(value: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)) return null
  const parsed = new Date(value + ':00+02:00')
  return Number.isFinite(parsed.getTime()) && targetInput(parsed.toISOString()) === value
    ? parsed.toISOString()
    : null
}
