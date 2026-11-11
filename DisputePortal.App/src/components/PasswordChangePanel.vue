<script setup lang="ts">
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '../api'
import { useAuthStore } from '../stores/auth'
import PasswordInput from './PasswordInput.vue'
const current = ref(''),
  password = ref(''),
  confirm = ref(''),
  error = ref(''),
  busy = ref(false),
  auth = useAuthStore(),
  router = useRouter()
async function save() {
  if (busy.value) return
  if (password.value !== confirm.value) {
    error.value = 'Passwords must match.'
    return
  }
  busy.value = true
  error.value = ''
  try {
    await api.post('/api/v1/profile/password', {
      currentPassword: current.value,
      newPassword: password.value,
      confirmPassword: confirm.value
    })
    current.value = ''
    password.value = ''
    confirm.value = ''
    auth.clearSession()
    await router.replace({ path: '/sign-in', query: { reset: '1' } })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Password change was not confirmed.'
    current.value = ''
  } finally {
    busy.value = false
  }
}
useFeedback(error, undefined, undefined)
</script>
<template>
  <section
    class="surface-panel profile-card w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <h2 class="w3-sans-serif w3-large">Change Password</h2>
    <p class="field-hint w3-small w3-text-grey w3-block">
      Changing your password signs out every device.
    </p>
    <p
      v-if="error"
      role="alert"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
    >
      {{ error }}
    </p>
    <form
      class="staff-form w3-bar w3-section"
      @submit.prevent="save"
    >
      <label
        class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
        for="current-password"
      >
        Current Password
      </label>
      <PasswordInput
        id="current-password"
        v-model="current"
        autocomplete="current-password"
        required
        :maxlength="reference.password?.maximum ?? undefined"
      />
      <label
        class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
        for="change-password"
      >
        New Password
      </label>
      <PasswordInput
        id="change-password"
        v-model="password"
        autocomplete="new-password"
        required
        :minlength="reference.password?.minimum"
        :maxlength="reference.password?.maximum ?? undefined"
      />
      <label
        class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
        for="change-confirm"
      >
        Confirm New Password
      </label>
      <PasswordInput
        id="change-confirm"
        v-model="confirm"
        autocomplete="new-password"
        required
        :minlength="reference.password?.minimum"
        :maxlength="reference.password?.maximum ?? undefined"
      />
      <button
        class="button button-primary full-width w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        :disabled="busy"
      >
        {{ busy ? 'Updating…' : 'Update Password' }}
      </button>
    </form>
  </section>
</template>
