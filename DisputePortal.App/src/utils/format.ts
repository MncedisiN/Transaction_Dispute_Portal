import { reference } from '../portalReference'
import { formattingContext } from './localeContext'
export function formatMoney(amount: number, currency?: string, countryCode?: string): string {
  const context = formattingContext(currency, countryCode)
  if (!context.currency) return 'Amount unavailable'
  return new Intl.NumberFormat(context.locale, {
    style: 'currency',
    currency: context.currency,
    minimumFractionDigits: context.decimals,
    maximumFractionDigits: context.decimals ?? 2
  }).format(amount)
}

const unavailableDate = 'Date unavailable'
function instant(value: string | null | undefined): Date | null {
  if (typeof value !== 'string' || !value.trim()) return null
  const parsed = new Date(value)
  return Number.isFinite(parsed.getTime()) ? parsed : null
}

export function formatDate(
  value: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short', year: 'numeric' },
  countryCode?: string
): string {
  const parsed = instant(value)
  if (!parsed) return unavailableDate
  const context = formattingContext(undefined, countryCode)
  const clock: Intl.DateTimeFormatOptions =
    options.dateStyle || options.timeStyle ? {} : { hour: 'numeric', minute: '2-digit' }
  return new Intl.DateTimeFormat(context.locale, {
    ...clock,
    ...options,
    hour12: true,
    timeZone: context.timeZone
  })
    .format(parsed)
    .replace(/\b(am|pm)\b/gi, (part) => part.toUpperCase())
}

export function maskAccount(value: string): string {
  const digits = value.replace(/\D/g, '')
  return digits.length < 4 ? '••••' : '••••••••' + digits.slice(-4)
}

export function maskEmail(value: string): string {
  const [name, domain] = value.split('@')
  if (!domain) return '••••'
  return (name.length > 0 ? name[0] : '•') + '••••@' + domain
}

export function formatTransactionTime(value: string | null | undefined): string {
  const parsed = instant(value)
  if (!parsed) return unavailableDate
  const context = formattingContext()
  const compactTransactionDate = new Intl.DateTimeFormat(context.locale, {
    day: 'numeric',
    month: 'short',
    timeZone: context.timeZone
  })
  const localTime = new Intl.DateTimeFormat(context.locale, {
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
    timeZone: context.timeZone
  })
  const dateKey = new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone: context.timeZone
  })
  const key = dateKey.format(parsed),
    today = dateKey.format(new Date())
  const previous = new Date(today + 'T00:00:00Z')
  previous.setUTCDate(previous.getUTCDate() - 1)
  const yesterday = previous.toISOString().slice(0, 10)
  const day =
    key === today
      ? 'Today'
      : key === yesterday
        ? 'Yesterday'
        : compactTransactionDate.format(parsed)
  return (
    day + ' · ' + localTime.format(parsed).replace(/\b(am|pm)\b/gi, (part) => part.toUpperCase())
  )
}
// Date-only identity fields have no timestamp or time-zone conversion.
export function formatBirthDate(value: string | null | undefined): string {
  if (typeof value !== 'string') return unavailableDate
  const match = /^(\d{4})-(\d{2})-(\d{2})(?:T.*)?$/.exec(value)
  if (!match) return unavailableDate
  const year = Number(match[1]),
    month = Number(match[2]),
    day = Number(match[3])
  const parsed = new Date(Date.UTC(year, month - 1, day))
  if (
    parsed.getUTCFullYear() !== year ||
    parsed.getUTCMonth() !== month - 1 ||
    parsed.getUTCDate() !== day
  )
    return unavailableDate
  return new Intl.DateTimeFormat(formattingContext().locale, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC'
  }).format(parsed)
}

export function bankingToday(): string {
  return new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone: formattingContext().timeZone
  }).format(new Date())
}

export function transactionTypeLabel(type: string): string {
  return reference.transactionTypes?.[type] ?? type.replace(/([a-z])([A-Z])/g, '$1 $2')
}

export function transactionStatusLabel(status: string): string {
  return reference.transactionStatuses?.[status] ?? status.replace(/([a-z])([A-Z])/g, '$1 $2')
}
