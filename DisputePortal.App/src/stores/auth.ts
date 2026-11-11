import { selectWorkspace, type Workspace } from '../workspace'
import { defineStore } from 'pinia'
import {
  readVerification,
  saveVerification,
  clearVerification,
  clearVerifications
} from '../utils/pendingVerification'
import { api, ApiError, refreshCsrfToken, setCsrfToken } from '../api'
import { useAccountsStore } from './accounts'
import type { SessionUser } from '../types'
import { readTabValue, saveTabValue, removeTabValue } from '../utils/sessionStorage'

export interface IdentityChallenge {
  challengeId: string
  channel: string
  maskedDestination: string
  expiresAt: string
  requiresVerification: true
  resendAvailableAt?: string
  flowExpiresAt?: string
  canRememberDevice?: boolean
  rememberedDeviceDays?: number
}
interface AuthState {
  workspace: Workspace
  generation: number
  pendingChallenge: IdentityChallenge | null
  user: SessionUser | null
  initialized: boolean
}

const initializations = new WeakMap<object, { generation: number; pending: Promise<void> }>()

function pendingSignIn(): IdentityChallenge | null {
  const value = readVerification('login')
  return value
    ? {
        challengeId: value.id,
        expiresAt: value.expiresAt,
        maskedDestination: value.maskedDestination,
        channel: value.channel ?? 'email',
        requiresVerification: true,
        resendAvailableAt: value.resendAvailableAt,
        flowExpiresAt: value.flowExpiresAt,
        canRememberDevice: value.canRememberDevice,
        rememberedDeviceDays: value.rememberedDeviceDays
      }
    : null
}
export const useAuthStore = defineStore('auth', {
  state: (): AuthState => ({
    workspace: 'personal',
    user: null,
    initialized: false,
    pendingChallenge: pendingSignIn(),
    generation: 0
  }),
  getters: {
    isAdmin: (state) => Boolean(state.user?.permissions.includes('disputes.read')),
    isStaffWorkspace: (state) =>
      state.workspace === 'staff' && Boolean(state.user?.permissions.includes('disputes.read')),
    canManage: (state) => Boolean(state.user?.permissions.includes('disputes.manage')),
    canReadAudit: (state) => Boolean(state.user?.permissions.includes('audit.read'))
  },
  actions: {
    setWorkspace(workspace: Workspace) {
      this.workspace = workspace === 'staff' && this.isAdmin ? 'staff' : 'personal'
      selectWorkspace(this.workspace)
      if (this.user) saveTabValue('portal.workspace:' + this.user.id, this.workspace)
    },
    acceptUser(user: SessionUser) {
      const changed = this.user?.id !== user.id
      this.user = user
      const saved = readTabValue('portal.workspace:' + user.id)
      this.setWorkspace(
        changed
          ? saved === 'personal'
            ? 'personal'
            : this.isAdmin
              ? 'staff'
              : 'personal'
          : this.workspace
      )
    },
    async refreshCsrf() {
      await refreshCsrfToken()
    },
    async initialize() {
      if (this.initialized) return
      const generation = this.generation
      const existing = initializations.get(this)
      if (existing?.generation === generation) return existing.pending
      const pending = (async () => {
        try {
          await this.refreshCsrf()
          if (generation !== this.generation) return
          const user = await api.get<SessionUser>('/api/v1/auth/me')
          if (generation === this.generation) this.acceptUser(user)
        } catch {
          if (generation === this.generation) this.user = null
        } finally {
          if (generation === this.generation) this.initialized = true
          if (initializations.get(this)?.generation === generation) initializations.delete(this)
        }
      })()
      initializations.set(this, { generation, pending })
      return pending
    },
    async signIn(loginIdentifier: string, password: string) {
      const generation = ++this.generation
      this.user = null
      useAccountsStore().reset()
      this.pendingChallenge = null
      clearVerifications()
      setCsrfToken(null)
      await this.refreshCsrf()
      const result = await api.post<IdentityChallenge | { user: SessionUser }>(
        '/api/v1/auth/sign-in',
        {
          loginIdentifier,
          password
        }
      )
      if (generation !== this.generation) return null
      if ('requiresVerification' in result && result.requiresVerification) {
        this.pendingChallenge = result
        saveVerification('login', {
          id: result.challengeId,
          expiresAt: result.expiresAt,
          maskedDestination: result.maskedDestination,
          channel: result.channel,
          resendAvailableAt: result.resendAvailableAt,
          flowExpiresAt: result.flowExpiresAt,
          canRememberDevice: result.canRememberDevice,
          rememberedDeviceDays: result.rememberedDeviceDays
        })
        return null
      }
      return this.loadAuthenticatedSession()
    },
    async verifySignIn(code: string, rememberDevice = false) {
      if (!this.pendingChallenge) throw new Error('Start sign-in again.')
      await api.post('/api/v1/auth/sign-in/verify', {
        challengeId: this.pendingChallenge.challengeId,
        code,
        rememberDevice
      })
      this.pendingChallenge = null
      clearVerification('login')
      return this.loadAuthenticatedSession()
    },
    async resendSignIn() {
      if (!this.pendingChallenge) throw new Error('Start sign-in again.')
      const generation = this.generation
      const result = await api.post<IdentityChallenge>('/api/v1/auth/sign-in/resend', {
        challengeId: this.pendingChallenge.challengeId
      })
      if (generation !== this.generation) return
      this.pendingChallenge = result
      saveVerification('login', {
        id: result.challengeId,
        expiresAt: result.expiresAt,
        maskedDestination: result.maskedDestination,
        channel: result.channel,
        resendAvailableAt: result.resendAvailableAt,
        flowExpiresAt: result.flowExpiresAt,
        canRememberDevice: result.canRememberDevice,
        rememberedDeviceDays: result.rememberedDeviceDays
      })
    },
    async loadAuthenticatedSession() {
      const generation = this.generation
      setCsrfToken(null)
      await this.refreshCsrf()
      const user = await api.get<SessionUser>('/api/v1/auth/me')
      if (generation !== this.generation) return null
      this.acceptUser(user)
      this.initialized = true
      window.dispatchEvent(new Event('portal:signed-in'))
      return this.user
    },
    cancelVerification() {
      this.pendingChallenge = null
      clearVerification('login')
    },
    clearSession() {
      this.generation++
      if (this.user) removeTabValue('portal.workspace:' + this.user.id)
      this.workspace = 'personal'
      selectWorkspace('personal')
      this.user = null
      this.pendingChallenge = null
      clearVerifications()
      useAccountsStore().reset()
      setCsrfToken(null)
    },
    async refreshSession() {
      if (!this.user) return
      const generation = this.generation
      try {
        const user = await api.get<SessionUser>('/api/v1/auth/me')
        if (generation === this.generation) this.acceptUser(user)
      } catch (cause) {
        if (generation === this.generation && cause instanceof ApiError && cause.status === 401)
          this.clearSession()
      }
    },
    async signOut() {
      try {
        await api.post('/api/v1/auth/sign-out')
      } catch (cause) {
        if (!(cause instanceof ApiError) || cause.status !== 401) throw cause
      }
      this.clearSession()
      window.dispatchEvent(new Event('portal:signed-out'))
    }
  }
})
