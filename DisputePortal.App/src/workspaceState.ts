export type Workspace = 'staff' | 'personal'
let activeWorkspace: Workspace = 'personal'
export function currentWorkspace() {
  return activeWorkspace
}
export function selectWorkspace(value: Workspace) {
  activeWorkspace = value
}
