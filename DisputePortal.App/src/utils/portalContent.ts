import { reference } from '../portalReference'

export function clientOption(key: string): number {
  const value = reference.clientOptions?.[key]
  if (!Number.isSafeInteger(value) || !value || value < 1)
    throw new Error('Portal display settings are unavailable. Please reload the portal.')
  return value
}
