<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { loadPortalReference, referenceState } from './portalReference'
import { loadPlatform } from './platform'
import ToastHost from './components/ToastHost.vue'
import { useOnline } from './composables/useOnline'
const router = useRouter()
const route = useRoute()
const { online } = useOnline()
const referenceError = ref('')
const loadingReference = ref(false)
async function loadReference() {
  if (loadingReference.value) return
  loadingReference.value = true
  referenceError.value = ''
  try {
    await loadPortalReference()
    // Financial pages handle their own availability; sign-in remains usable during a service outage.
    void loadPlatform().catch(() => {})
  } catch {
    referenceError.value = 'Portal settings are temporarily unavailable. Please try again.'
  } finally {
    loadingReference.value = false
  }
}
onMounted(async () => {
  // Initial route guards establish or clear the session. Finish that check before
  // starting requests protected against responses from a previous session.
  await router.isReady()
  await loadReference()
})
</script>

<template>
  <div
    v-if="!online"
    class="offline-ribbon w3-panel w3-pale-yellow"
    role="status"
  >
    <span class="online-dot" />
    You're offline. Private transaction and dispute data is unavailable until you reconnect.
  </div>
  <!-- Password authentication has no dependency on dispute/reference policies. -->
  <RouterView v-if="route.name === 'sign-in' || (referenceState.ready && !referenceError)" />
  <main
    v-else
    class="surface-panel portal-startup w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    aria-live="polite"
  >
    <p>{{ referenceError || 'Loading Your Portal…' }}</p>
    <button
      v-if="referenceError"
      class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
      :disabled="loadingReference"
      @click="loadReference"
    >
      Try Again
    </button>
  </main>
  <ToastHost />
</template>
