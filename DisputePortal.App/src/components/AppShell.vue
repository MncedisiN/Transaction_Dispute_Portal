<script setup lang="ts">
import { userRoles } from '../portalReference'

import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { staffRoleLabel } from '../workspace'
import { useRoute, useRouter } from 'vue-router'
import {
  ArrowLeftRight,
  Bell,
  CircleHelp,
  FileClock,
  LayoutDashboard,
  ChevronDown,
  ShieldCheck,
  Sun,
  Moon,
  Ellipsis,
  X,
  Monitor,
  Activity,
  SlidersHorizontal,
  Users,
  UserRound,
  ClipboardList,
  Check
} from '@lucide/vue'
import { useAuthStore } from '../stores/auth'
import { api } from '../api'
import { updateApplicationBadge } from '../pushNotifications'
import { useTheme } from '../composables/useTheme'
import { useLiveRefresh } from '../composables/useLiveRefresh'

defineProps<{ conversation?: boolean }>()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
watch(
  () => route.path,
  (path) => {
    if (path.startsWith('/admin')) auth.setWorkspace('staff')
    else if (
      auth.user?.roles.includes('customer') &&
      (/^\/(personal|accounts|transactions|disputes)(\/|$)/.test(path) || path === '/')
    )
      auth.setWorkspace('personal')
  },
  { immediate: true }
)
const profileMenuOpen = ref(false)
function closeProfileMenu() {
  profileMenuOpen.value = false
}
function closeOutsideProfile(event: PointerEvent) {
  if (!(event.target as Element)?.closest?.('.profile-controls')) closeProfileMenu()
}
onMounted(() => document.addEventListener('pointerdown', closeOutsideProfile))
onUnmounted(() => document.removeEventListener('pointerdown', closeOutsideProfile))
watch(() => route.fullPath, closeProfileMenu)
async function switchWorkspace() {
  closeProfileMenu()
  auth.setWorkspace(auth.isStaffWorkspace ? 'personal' : 'staff')
  await router.push(auth.isStaffWorkspace ? '/admin/disputes' : '/personal')
}
const workspaceRole = computed(() =>
  auth.isStaffWorkspace ? staffRoleLabel(auth.user?.roles ?? []) : (userRoles.customer?.label ?? '')
)
const isCurrent = (path: string) =>
  path === '/'
    ? route.path === '/' || route.path === '/personal' || route.path.startsWith('/accounts/')
    : route.path === path || route.path.startsWith(path + '/')
const { preference, isDark } = useTheme()
const isAdmin = computed(() => auth.isStaffWorkspace)
const staffLinks = computed(() =>
  adminLinks.value.filter(
    (item) =>
      (item.to !== '/personal' || auth.user?.roles.includes('customer')) &&
      (item.to !== '/admin/transactions' ||
        auth.user?.permissions.includes('transactions.assist')) &&
      (!['/admin/dispute-rules', '/admin/dispute-operations', '/admin/monitoring'].includes(
        item.to
      ) ||
        auth.user?.roles.includes('super_admin')) &&
      (item.to !== '/admin/audit' || auth.canReadAudit) &&
      (!['/admin/users', '/admin/account-requests'].includes(item.to) ||
        auth.user?.permissions.includes('users.manage'))
  )
)
const unreadCount = ref(0)
const appearanceSheet = ref<HTMLDialogElement | null>(null)
const navigationSheet = ref<HTMLDialogElement | null>(null)
const initials = computed(() =>
  (auth.user?.displayName ?? 'A')
    .split(' ')
    .map((part) => part[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
)

const customerLinks = computed(() => [
  { to: '/', label: 'Overview', icon: LayoutDashboard },
  { to: '/transactions', label: 'Transactions', icon: ArrowLeftRight },
  { to: '/disputes', label: 'Disputes', icon: FileClock },
  { to: '/support', label: 'Support', icon: CircleHelp },
  { to: '/profile', label: 'Profile', icon: UserRound }
])
const adminLinks = computed(() => [
  { to: '/admin/disputes', label: 'Cases', icon: ShieldCheck },
  { to: '/admin/transactions', label: 'Transactions', icon: ArrowLeftRight },
  { to: '/support', label: 'Support', icon: CircleHelp },
  { to: '/admin/dispute-rules', label: 'Policy', icon: ClipboardList },
  {
    to: '/admin/dispute-operations',
    label: 'Dispute Operations',
    icon: SlidersHorizontal
  },
  { to: '/admin/monitoring', label: 'API Monitoring', icon: Activity },
  { to: '/admin/users', label: 'User Administration', icon: Users },
  { to: '/admin/account-requests', label: 'Account Closure Requests', icon: FileClock },
  { to: '/profile', label: 'Profile', icon: ShieldCheck }
])
const notificationRequest = new AbortController()
async function refreshNotifications() {
  try {
    const result = await api.get<{ count: number }>(
      '/api/v1/notifications/unread-count',
      notificationRequest.signal
    )
    if (
      !notificationRequest.signal.aborted &&
      Number.isSafeInteger(result.count) &&
      result.count >= 0
    ) {
      unreadCount.value = result.count
      await updateApplicationBadge(result.count)
    }
  } catch {
    /* Notifications never block the primary workspace. */
  }
}
useLiveRefresh(refreshNotifications, ['notifications'])
onMounted(() => {
  window.addEventListener('portal:notifications-read', refreshNotifications)
})
onUnmounted(() => {
  notificationRequest.abort()
  window.removeEventListener('portal:notifications-read', refreshNotifications)
})
</script>

<template>
  <div
    class="portal-layout w3-row w3-sans-serif w3-text-dark-grey"
    :class="{ 'conversation-layout': conversation }"
  >
    <aside
      v-if="!conversation"
      class="sidebar w3-sidebar w3-col l2 w3-hide-small w3-hide-medium w3-white w3-border-right w3-padding-small"
    >
      <RouterLink
        class="brand w3-button w3-block w3-left-align w3-round-large w3-padding-small w3-padding-16"
        to="/"
        aria-label="Dispute Portal Overview"
      >
        <span class="w3-cell-row">
          <span class="w3-cell w3-cell-middle">
            <img
              src="/app-icon.svg"
              width="36"
              height="36"
              alt=""
            />
          </span>
          <b class="w3-cell w3-cell-middle w3-medium">Dispute Portal</b>
        </span>
      </RouterLink>
      <p class="nav-caption w3-tiny w3-wide w3-text-grey w3-padding-small w3-section">
        {{ isAdmin ? 'WORKSPACE' : 'YOUR MONEY' }}
      </p>
      <nav
        class="primary-nav w3-bar-block"
        :aria-label="isAdmin ? 'Staff navigation' : 'Customer navigation'"
      >
        <RouterLink
          v-for="item in isAdmin ? staffLinks : customerLinks"
          :key="item.to"
          :to="item.to"
          :title="item.label"
          :aria-label="item.label"
          class="nav-link w3-bar-item w3-button w3-round-xlarge w3-padding w3-margin-bottom"
          :class="{ 'nav-current w3-theme-l4 w3-text-black': isCurrent(item.to) }"
          :aria-current="isCurrent(item.to) ? 'page' : undefined"
        >
          <span class="w3-left">
            <component
              :is="item.icon"
              :size="18"
              :stroke-width="1.8"
              class="w3-margin-right"
            />
          </span>
          <div class="w3-rest w3-left-align">{{ item.label }}</div>
        </RouterLink>
      </nav>
    </aside>
    <dialog
      ref="navigationSheet"
      class="navigation-sheet w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
      aria-label="More Navigation"
    >
      <div class="w3-bar">
        <header class="w3-container">
          <button
            class="icon-button w3-button w3-right"
            aria-label="Close Navigation"
            @click="navigationSheet?.close()"
          >
            <X :size="19" />
          </button>
          <h2 class="w3-sans-serif w3-large">Workspace</h2>
        </header>
        <nav class="w3-bar-block">
          <RouterLink
            v-for="item in staffLinks.slice(4)"
            :key="item.to"
            :to="item.to"
            class="sheet-link w3-bar-item w3-button w3-padding-16"
            @click="navigationSheet?.close()"
          >
            <component
              :is="item.icon"
              class="w3-margin-right"
              :size="19"
            />
            {{ item.label }}
          </RouterLink>
        </nav>
      </div>
    </dialog>
    <dialog
      ref="appearanceSheet"
      class="navigation-sheet appearance-sheet w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
      aria-label="Appearance"
    >
      <div class="w3-bar">
        <header class="w3-container">
          <button
            class="icon-button w3-button w3-right"
            aria-label="Close Appearance"
            @click="appearanceSheet?.close()"
          >
            <X :size="19" />
          </button>
          <h2 class="w3-sans-serif w3-large">Appearance</h2>
        </header>
        <div
          class="appearance-options w3-bar-block"
          role="group"
          aria-label="Choose Appearance"
        >
          <button
            v-for="option in [
              { value: 'light', label: 'Light', icon: Sun },
              { value: 'dark', label: 'Dark', icon: Moon },
              { value: 'system', label: 'System', icon: Monitor }
            ] as const"
            :key="option.value"
            class="w3-bar-item w3-button w3-padding-16"
            :class="{ 'w3-light-grey': preference === option.value }"
            :aria-pressed="preference === option.value"
            @click="preference = option.value"
          >
            <component
              :is="option.icon"
              class="w3-margin-right"
              :size="22"
            />
            {{ option.label }}
            <Check
              v-if="preference === option.value"
              :size="15"
            />
          </button>
        </div>
      </div>
    </dialog>
    <div
      class="main-column w3-col w3-right"
      :class="conversation ? 's12 m12 l12' : 's12 m12 l10'"
    >
      <header
        v-if="!conversation"
        class="topbar w3-top w3-col s12 m12 l10 w3-container w3-white w3-border-bottom w3-padding-16"
      >
        <div class="w3-row">
          <div class="w3-col s8 l6 w3-padding-small">
            <RouterLink
              class="mobile-brand w3-button w3-padding-small w3-large w3-hide-large"
              to="/"
              aria-label="Dispute Portal Overview"
            >
              <img
                src="/app-icon.svg"
                width="36"
                height="36"
                alt=""
                class="w3-margin-right"
              />
              <b>Dispute Portal</b>
            </RouterLink>
            <p class="breadcrumbs w3-hide-small w3-hide-medium w3-small w3-text-grey">
              <strong class="header-product">Dispute Portal</strong>
              <span class="w3-margin-left w3-margin-right">/</span>
              <b>{{ isAdmin ? 'Staff Workspace' : 'Personal Accounts' }}</b>
            </p>
          </div>
          <div class="w3-col s4 l2 w3-bar w3-padding-small">
            <RouterLink
              class="icon-button notification-button w3-bar-item w3-button w3-round-large w3-right w3-padding-small w3-display-container w3-padding-16"
              to="/notifications"
              :aria-label="`Notifications, ${unreadCount} unread`"
              :aria-current="route.path === '/notifications' ? 'page' : undefined"
            >
              <Bell :size="18" />
              <span
                v-if="unreadCount"
                class="notification-unread-count w3-badge w3-theme-d5 w3-display-topright w3-tiny"
              >
                {{ unreadCount > 99 ? '99+' : unreadCount }}
              </span>
            </RouterLink>
            <button
              class="icon-button appearance-button w3-bar-item w3-button w3-round-large w3-right w3-padding-small w3-padding-16"
              type="button"
              aria-label="Appearance"
              aria-haspopup="dialog"
              title="Light, Dark Or System Appearance"
              @click="appearanceSheet?.showModal()"
            >
              <Sun
                v-if="isDark"
                :size="18"
              />
              <Moon
                v-else
                :size="18"
              />
            </button>
          </div>
          <div
            class="profile-controls w3-col s12 l4 w3-padding-small"
            @keydown.esc="closeProfileMenu"
          >
            <div class="profile-menu w3-dropdown-click w3-block">
              <button
                class="profile-menu-toggle w3-button w3-block w3-left-align w3-round-large"
                type="button"
                aria-label="Profile Menu"
                :aria-expanded="profileMenuOpen"
                @click="profileMenuOpen = !profileMenuOpen"
              >
                <ChevronDown
                  :size="17"
                  class="w3-right w3-margin-left"
                />
                <span
                  class="avatar w3-tag w3-theme-l4 w3-circle w3-padding-small w3-margin-right w3-left"
                >
                  {{ initials }}
                </span>
                <div class="profile-copy w3-rest">
                  <b class="w3-block">{{ auth.user?.displayName }}</b>
                  <small class="w3-block w3-text-grey w3-hide-small w3-hide-medium">
                    {{ workspaceRole }}
                  </small>
                </div>
              </button>
              <div
                v-if="profileMenuOpen"
                class="profile-menu-panel w3-dropdown-content w3-show w3-block w3-bar-block w3-card w3-white w3-border w3-round-large"
              >
                <RouterLink
                  to="/profile"
                  class="w3-bar-item w3-button w3-padding-16"
                  @click="closeProfileMenu"
                >
                  <ShieldCheck
                    class="w3-margin-right"
                    :size="17"
                  />
                  Profile &amp; Security
                </RouterLink>
                <button
                  v-if="auth.isAdmin && auth.user?.roles.includes('customer')"
                  type="button"
                  class="w3-bar-item w3-button w3-padding-16"
                  @click="switchWorkspace"
                >
                  <ArrowLeftRight
                    class="w3-margin-right"
                    :size="17"
                  />
                  {{ isAdmin ? 'Switch To Customer' : 'Switch To Staff' }}
                </button>
              </div>
            </div>
          </div>
        </div>
      </header>
      <div
        v-if="!conversation"
        class="w3-hide-large w3-padding-64"
        aria-hidden="true"
      />
      <div
        v-if="!conversation"
        class="w3-hide-small w3-hide-medium w3-padding-48"
        aria-hidden="true"
      />
      <main class="content-area w3-container w3-bar w3-padding-16 w3-sans-serif">
        <slot />
      </main>
      <div
        v-if="!conversation"
        class="w3-padding-64 w3-hide-large"
        aria-hidden="true"
      />
    </div>
    <nav
      v-if="!conversation"
      class="mobile-navigation w3-bottom w3-cell-row w3-bar w3-border-top w3-hide-large"
      :class="isDark ? 'w3-black w3-text-white' : 'w3-white w3-text-black'"
      aria-label="Mobile Navigation"
    >
      <RouterLink
        v-for="item in isAdmin ? staffLinks.slice(0, 4) : customerLinks"
        :key="item.to"
        :to="item.to"
        class="w3-cell w3-button w3-center w3-small w3-padding-small w3-padding-16"
        :class="{
          'w3-text-blue-grey w3-theme-l4 w3-round-xlarge': isCurrent(item.to),
          'primary-disputes-navigation': item.to === '/disputes'
        }"
        :aria-current="isCurrent(item.to) ? 'page' : undefined"
      >
        <component
          :is="item.icon"
          :size="item.to === '/disputes' ? 28 : 20"
          class="w3-block"
        />
        {{ item.label }}
      </RouterLink>
      <button
        v-if="isAdmin && staffLinks.length > 4"
        class="w3-cell w3-button w3-center w3-small w3-padding-small"
        aria-haspopup="dialog"
        @click="navigationSheet?.showModal()"
      >
        <Ellipsis
          :size="20"
          class="w3-block"
        />
        More
      </button>
    </nav>
  </div>
</template>
