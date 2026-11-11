<script setup lang="ts">
import { clientOption } from '../utils/portalContent'
import { useFeedback } from '../composables/useFeedback'

import { computed, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Bell, ChevronRight, CheckCheck } from '@lucide/vue'
import AppShell from '../components/AppShell.vue'
import BackLink from '../components/BackLink.vue'
import PageHeading from '../components/PageHeading.vue'
import { api } from '../api'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { useAuthStore } from '../stores/auth'
import { formatDate } from '../utils/format'
import type { PageResult, PortalNotification } from '../types'
const router = useRouter(),
  auth = useAuthStore(),
  page = ref(1),
  loading = ref(true),
  error = ref(''),
  busy = ref('')
const data = ref<PageResult<PortalNotification>>({
  items: [],
  page: 1,
  pageSize: clientOption('notificationPageSize'),
  total: 0
})
const pages = computed(() => Math.max(1, Math.ceil(data.value.total / data.value.pageSize)))
let generation = 0,
  request: AbortController | undefined
const mutation = new AbortController()
let disposed = false
async function load() {
  const current = ++generation
  request?.abort()
  request = new AbortController()
  loading.value = true
  error.value = ''
  try {
    const result = await api.page<PortalNotification>(
      '/api/v1/notifications',
      { page: page.value, pageSize: clientOption('notificationPageSize') },
      request.signal
    )
    if (!Array.isArray(result.items) || !Number.isFinite(result.total))
      throw Error('Invalid update list')
    if (current === generation) data.value = result
  } catch {
    if (current === generation) error.value = 'Your updates are temporarily unavailable. Try again.'
  } finally {
    if (current === generation) loading.value = false
  }
}
async function markRead(item: PortalNotification) {
  if (item.isRead) return
  await api.post(
    '/api/v1/notifications/' + encodeURIComponent(item.id) + '/read',
    undefined,
    undefined,
    mutation.signal
  )
  if (disposed) return
  item.isRead = true
  window.dispatchEvent(new Event('portal:notifications-read'))
}
async function open(item: PortalNotification) {
  if (busy.value) return
  busy.value = item.id
  try {
    await markRead(item)
  } catch {
    /* Leave unread when acknowledgement was not persisted. */
  } finally {
    busy.value = ''
  }
  if (disposed) return
  await router.push(
    item.url &&
      /^\/(?:support|disputes|admin\/disputes|notifications|accounts|transactions)(?:[/?]|$)/.test(
        item.url
      )
      ? item.url
      : item.disputeId
        ? (auth.isStaffWorkspace ? '/admin/disputes/' : '/disputes/') +
          encodeURIComponent(item.disputeId)
        : '/support'
  )
}
async function acknowledge(item: PortalNotification) {
  if (busy.value) return
  busy.value = item.id
  error.value = ''
  try {
    await markRead(item)
  } catch {
    if (!disposed) error.value = 'This update is still unread. Please try again.'
  } finally {
    busy.value = ''
  }
}
async function changePage(value: number) {
  page.value = value
  await load()
}
useLiveRefresh(load, ['notifications'])
onUnmounted(() => {
  disposed = true
  generation++
  request?.abort()
  mutation.abort()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <AppShell>
    <BackLink :fallback="auth.isStaffWorkspace ? '/admin/disputes' : '/'" />
    <PageHeading title="Notifications" />
    <section
      class="notification-feed surface-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
      aria-label="Your Notifications"
      :aria-busy="loading"
    >
      <div
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
        <button
          type="button"
          class="text-button w3-button w3-text-black w3-round-large"
          @click="load"
        >
          Try Again
        </button>
      </div>
      <div
        v-if="loading && !data.items.length"
        class="notification-loading"
        role="status"
      >
        Loading updates…
      </div>
      <template v-else-if="data.items.length">
        <div class="notification-feed-summary w3-bar w3-padding-16 w3-small w3-text-grey">
          <span>{{ data.total }} {{ data.total === 1 ? 'update' : 'updates' }}</span>
          <span class="w3-right">Newest First</span>
        </div>
        <article
          v-for="item in data.items"
          :key="item.id"
          class="notification-card w3-padding-16 w3-border-bottom w3-bar"
          :class="{ 'is-unread': !item.isRead }"
        >
          <button
            type="button"
            class="notification-open w3-button w3-block w3-left-align w3-padding-16 w3-round-large"
            :disabled="!!busy"
            @click="open(item)"
          >
            <span
              class="notification-symbol w3-left w3-padding-small w3-text-blue-grey w3-margin-right"
            >
              <Bell :size="19" />
              <i
                v-if="!item.isRead"
                aria-label="Unread"
              />
            </span>
            <span class="notification-copy w3-block">
              <strong class="w3-block w3-margin-bottom">{{ item.title }}</strong>
              <span class="w3-block">{{ item.body }}</span>
              <time
                class="w3-block w3-small w3-text-grey w3-margin-top"
                :datetime="item.createdAt"
              >
                {{ formatDate(item.createdAt) }}
              </time>
            </span>
            <ChevronRight
              :size="17"
              class="notification-arrow w3-right w3-text-blue-grey"
            />
          </button>
          <button
            v-if="!item.isRead"
            type="button"
            class="notification-read w3-button w3-round-large"
            :disabled="!!busy"
            :aria-label="'Mark ' + item.title + ' as read'"
            @click="acknowledge(item)"
          >
            <CheckCheck
              class="w3-margin-right"
              :size="15"
            />
            {{ busy === item.id ? 'Saving…' : 'Mark As Read' }}
          </button>
        </article>
      </template>
      <div
        v-else-if="!error"
        class="notification-empty-state w3-center w3-padding-64"
      >
        <span><Bell :size="26" /></span>
        <h2 class="w3-sans-serif w3-large">You’Re Up To Date</h2>
        <p>Case updates will appear here.</p>
      </div>
    </section>
    <nav
      v-if="pages > 1"
      class="pagination w3-container w3-section w3-center"
      aria-label="Notification Pages"
    >
      <span>Page {{ page }} of {{ pages }}</span>
      <div>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="page <= 1 || loading"
          @click="changePage(page - 1)"
        >
          Previous
        </button>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="page >= pages || loading"
          @click="changePage(page + 1)"
        >
          Next
        </button>
      </div>
    </nav>
  </AppShell>
</template>
