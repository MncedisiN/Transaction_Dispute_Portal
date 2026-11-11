// Only return through known, same-app history. A direct link has a predictable fallback.
export function previousDestination(
  value: unknown,
  current: string,
  fallback: string
): string | null {
  if (
    typeof value !== 'string' ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    /[\\\r\n]/.test(value) ||
    value === current
  )
    return null
  const path = value.split(/[?#]/)[0]!
  const publicPage = /^\/(sign-in|register|forgot-password)$/.test(path)
  if (publicPage) return fallback === '/sign-in' ? value : null
  return /^\/(?:$|transactions(?:\/[^/]+)?$|disputes(?:\/(?:new\/)?[^/]+)?$|accounts\/[^/]+$|profile$|support$|admin(?:\/[^/]+){0,2}$)/.test(
    path
  )
    ? value
    : null
}
export function destinationLabel(path: string): string {
  const clean = path.split(/[?#]/)[0]
  if (clean === '/support')
    return /[?&](thread|new)=/.test(path) ? 'Back To Conversation' : 'Back To Messages'
  if (/^\/(admin\/)?disputes\/[^/]+$/.test(clean!)) return 'Back To Dispute'
  if (clean === '/disputes') return 'Back To Disputes'
  if (clean === '/admin/disputes') return 'Back To Case Management'
  if (clean === '/transactions') return 'Back To Transactions'
  if (clean?.startsWith('/transactions/')) return 'Back To Transaction'
  if (clean?.startsWith('/accounts/')) return 'Back To Account'
  if (clean === '/') return 'Back To Overview'
  if (clean === '/profile') return 'Back To Profile'
  if (clean === '/sign-in') return 'Back To Sign-in'
  return 'Back'
}
