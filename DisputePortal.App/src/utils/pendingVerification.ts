export type VerificationFlow =
  'login' | 'registration' | 'recovery' | 'contact' | 'dispute' | 'deletion'
export interface PendingVerification {
  id: string
  expiresAt: string
  maskedDestination: string
  channel?: string
  field?: string
  resendAvailableAt?: string
  flowExpiresAt?: string
  canRememberDevice?: boolean
  rememberedDeviceDays?: number
}
const prefix = 'resolve.verification.v1:'
function key(flow: VerificationFlow, owner = '', context = '') {
  return prefix + flow + ':' + owner + ':' + context
}
export function saveVerification(
  flow: VerificationFlow,
  value: PendingVerification,
  owner = '',
  context = ''
): void {
  try {
    sessionStorage.setItem(
      key(flow, owner, context),
      JSON.stringify({
        id: value.id,
        expiresAt: value.expiresAt,
        maskedDestination: value.maskedDestination,
        channel: value.channel,
        field: value.field,
        ...(flow === 'login'
          ? {
              resendAvailableAt: value.resendAvailableAt,
              flowExpiresAt: value.flowExpiresAt,
              canRememberDevice: value.canRememberDevice,
              rememberedDeviceDays: value.rememberedDeviceDays
            }
          : {})
      })
    )
  } catch {
    /* Private browsing may restrict tab storage; the active page still works. */
  }
}
export function readVerification(
  flow: VerificationFlow,
  owner = '',
  context = ''
): PendingVerification | null {
  try {
    const raw = sessionStorage.getItem(key(flow, owner, context))
    if (!raw) return null
    const value = JSON.parse(raw) as PendingVerification
    const expires = Date.parse(
      flow === 'login' && value.flowExpiresAt ? value.flowExpiresAt : value.expiresAt
    )
    if (
      typeof value.id !== 'string' ||
      !value.id ||
      value.id.length > 100 ||
      typeof value.maskedDestination !== 'string' ||
      value.maskedDestination.length > 320 ||
      !Number.isFinite(expires) ||
      expires <= Date.now()
    ) {
      clearVerification(flow, owner, context)
      return null
    }
    return {
      id: value.id,
      expiresAt: value.expiresAt,
      maskedDestination: value.maskedDestination,
      channel: typeof value.channel === 'string' ? value.channel : undefined,
      field: typeof value.field === 'string' ? value.field : undefined,
      ...(flow === 'login'
        ? {
            resendAvailableAt:
              typeof value.resendAvailableAt === 'string' ? value.resendAvailableAt : undefined,
            flowExpiresAt:
              typeof value.flowExpiresAt === 'string' ? value.flowExpiresAt : undefined,
            canRememberDevice: value.canRememberDevice === true,
            rememberedDeviceDays:
              typeof value.rememberedDeviceDays === 'number' &&
              Number.isSafeInteger(value.rememberedDeviceDays) &&
              value.rememberedDeviceDays > 0
                ? value.rememberedDeviceDays
                : undefined
          }
        : {})
    }
  } catch {
    clearVerification(flow, owner, context)
    return null
  }
}
export function clearVerification(flow: VerificationFlow, owner = '', context = ''): void {
  try {
    sessionStorage.removeItem(key(flow, owner, context))
  } catch {
    /* No persisted state is available. */
  }
}
export function clearVerifications(): void {
  try {
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const name = sessionStorage.key(index)
      if (name?.startsWith(prefix)) sessionStorage.removeItem(name)
    }
  } catch {
    /* No persisted state is available. */
  }
}
