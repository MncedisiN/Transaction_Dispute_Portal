<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
const auth = useAuthStore(),
  router = useRouter(),
  busy = ref(false),
  error = ref('')
async function signOut() {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await auth.signOut()
    await router.replace('/sign-in')
  } catch {
    error.value = 'You are still signed in. Please try again.'
  } finally {
    busy.value = false
  }
}
useFeedback(error, undefined, undefined)
</script>
<template>
  <section class="profile-sign-out">
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <button
      class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      type="button"
      :disabled="busy"
      @click="signOut"
    >
      {{ busy ? 'Signing Out…' : 'Sign Out' }}
    </button>
  </section>
</template>
