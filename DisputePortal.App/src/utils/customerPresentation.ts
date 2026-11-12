import type { ActionActor } from '../types'
export function customerActor(
  identity?: ActionActor | null,
  fallback?: string | null,
  userId?: string,
  action = '',
  userDisplayName?: string
) {
  if (identity?.userId && identity.userId === userId) return 'You'
  // Customer snapshots intentionally omit actor IDs and roles. Match their recorded
  // display name only when no explicit actor ID is available.
  const recordedName = identity?.fullName || identity?.accountName || identity?.label || fallback
  const normalizeName = (value?: string | null) =>
    value?.trim().replace(/\s+/g, ' ').toLocaleLowerCase()
  if (
    !identity?.userId &&
    userDisplayName &&
    normalizeName(recordedName) === normalizeName(userDisplayName)
  )
    return 'You'
  const roles = identity?.roles ?? []
  if (
    identity?.kind === 'System' ||
    /^(system|automatic|automated processing)$/i.test(fallback?.trim() ?? '')
  )
    return /refund/i.test(action) ? 'Automatic refund' : 'Automatic update'
  if (
    roles.some((role) => /specialist|supervisor|senior/i.test(role)) ||
    /specialist|supervisor|senior/i.test(fallback ?? '')
  )
    return 'Specialist agent'
  if (roles.includes('customer')) return 'You'
  return 'Agent'
}
export function customerActivityText(value: string | null | undefined) {
  return (value || '')
    .replace(
      /Customer withdrew and stopped this case\./gi,
      'You withdrew this dispute and ended the review.'
    )
    .replace(
      /A service consultant submitted this dispute on behalf of the customer\./gi,
      'An agent submitted this dispute on your behalf.'
    )
    .replace(/\bportal administrator\b/gi, 'agent')
    .replace(/\bservice consultant\b/gi, 'agent')
}

export function customerResolutionLabel(
  identity: ActionActor,
  userId: string | undefined,
  status: string,
  resolution?: string | null,
  userDisplayName?: string
) {
  const actor = customerActor(
    identity,
    identity.label,
    userId,
    resolution || status,
    userDisplayName
  )
  if (actor === 'Automatic refund') return 'Refund processed automatically'
  if (actor === 'Automatic update')
    return status === 'Resolved' ? 'Resolved automatically' : 'Closed automatically'
  if (actor === 'You')
    return status === 'Withdrawn'
      ? 'You withdrew this dispute'
      : status === 'Resolved'
        ? 'Resolved by you'
        : 'Closed by you'
  return (
    (status === 'Resolved' ? 'Resolved by ' : 'Closed by ') +
    (actor === 'Specialist agent' ? 'a specialist agent' : 'an agent')
  )
}
