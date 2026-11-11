import type { SessionUser } from '../types'

export function hasPermission(user: SessionUser | null, permission: string): boolean {
  return Boolean(user?.permissions.includes(permission))
}
