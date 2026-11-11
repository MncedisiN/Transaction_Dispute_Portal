import { userRoles } from '../portalReference'
export { userRoles }
export function isSpecialistRole(role: string): boolean {
  return userRoles[role]?.specialist === true
}
export function isAssignableRole(role: string): boolean {
  return userRoles[role]?.assignable === true
}
