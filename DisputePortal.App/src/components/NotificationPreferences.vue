<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { Bell } from '@lucide/vue'
import { api, ApiError } from '../api'
import {
  enablePush,
  existingPush,
  synchronizePush,
  supportsPush,
  type NotificationConfiguration
} from '../pushNotifications'
const enabled = ref(false),
  pushAvailable = ref(false),
  publicKey = ref('')
const busy = ref(false),
  loading = ref(true),
  error = ref(''),
  success = ref('')
const supported = ref(supportsPush()),
  denied = ref(false)
const request = new AbortController()
let disposed = false
const help = computed(() => {
  if (!window.isSecureContext) return 'Open the HTTPS portal to receive device notifications.'
  if (denied.value)
    return 'Allow notifications in your browser or device settings to receive updates.'
  if (!supported.value)
    return 'Install Dispute Portal on your Home Screen or use a browser that supports notifications.'
  if (!pushAvailable.value)
    return 'Device notifications are temporarily unavailable. Your updates remain in the app.'
  return enabled.value
    ? 'Transfer, dispute and support updates are delivered to this device.'
    : 'Allow browser notifications to receive updates on this device.'
})
async function load() {
  if (busy.value || disposed) return
  supported.value = supportsPush()
  denied.value = typeof Notification !== 'undefined' && Notification.permission === 'denied'
  try {
    await synchronizePush()
    const config = await api.get<NotificationConfiguration>(
      '/api/v1/notification-settings',
      request.signal
    )
    if (disposed) return
    pushAvailable.value = config.pushAvailable
    publicKey.value = config.publicKey ?? ''
    enabled.value = !!(
      config.pushAvailable &&
      config.publicKey &&
      (await existingPush(config.publicKey))
    )
    if (config.channel !== 'inapp')
      await api.post(
        '/api/v1/notification-settings',
        { channel: 'inapp' },
        undefined,
        request.signal
      )
  } catch (cause) {
    if (!disposed)
      error.value =
        cause instanceof ApiError ? cause.message : 'Notification settings could not be loaded.'
  } finally {
    if (!disposed) loading.value = false
  }
}
async function allowNotifications() {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await enablePush(publicKey.value)
    if (!disposed) {
      enabled.value = true
      success.value = 'Notifications are enabled on this device.'
    }
  } catch (cause) {
    if (!disposed)
      error.value = cause instanceof Error ? cause.message : 'Please allow browser notifications.'
  } finally {
    if (!disposed) busy.value = false
  }
}
onMounted(() => {
  void load()
  window.addEventListener('focus', load)
})
onUnmounted(() => {
  disposed = true
  request.abort()
  window.removeEventListener('focus', load)
})
useFeedback(error, success, undefined)
</script>
<template>
  <section
    class="surface-panel ops-panel notification-preferences w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <h2 class="w3-sans-serif w3-large">Push Notifications</h2>
    <p>{{ loading ? 'Checking device notifications…' : help }}</p>
    <button
      v-if="!enabled && supported && pushAvailable && !denied"
      type="button"
      class="button button-primary w3-button w3-block w3-round-large"
      :disabled="busy || loading"
      @click="allowNotifications"
    >
      <Bell :size="17" />
      {{ busy ? 'Enabling…' : 'Allow Notifications' }}
    </button>
    <div class="important-message-preference">
      <h3 class="w3-large">Important Messages</h3>
      <p>Always available in the app.</p>
    </div>
  </section>
</template>
