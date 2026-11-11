import { userRoles } from './portalReference'
export { currentWorkspace, selectWorkspace, type Workspace } from './workspaceState'
export function staffRoleLabel(roles: readonly string[]) {
  return (
    Object.entries(userRoles)
      .filter(([code]) => roles.includes(code) && code !== 'customer')
      .sort(
        ([, left], [, right]) =>
          (left.rank ?? Number.MAX_SAFE_INTEGER) - (right.rank ?? Number.MAX_SAFE_INTEGER)
      )
      .map(([, role]) => role.label)[0] ?? 'Staff Member'
  )
}
