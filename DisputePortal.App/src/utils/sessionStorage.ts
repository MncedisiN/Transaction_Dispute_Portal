// Tab storage is optional. The caller's reactive state remains the source of truth.
export function readTabValue(key: string): string | null {
  try {
    return sessionStorage.getItem(key)
  } catch {
    return null
  }
}

export function saveTabValue(key: string, value: string): void {
  try {
    sessionStorage.setItem(key, value)
  } catch {
    // Restricted browsers can still use the current session in memory.
  }
}

export function removeTabValue(key: string): void {
  try {
    sessionStorage.removeItem(key)
  } catch {
    // Nothing persisted is needed to clear the in-memory state.
  }
}
