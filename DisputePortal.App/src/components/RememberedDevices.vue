<script setup lang="ts">
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { onMounted, onUnmounted, ref } from 'vue'
import PasswordInput from './PasswordInput.vue'
import { api, ApiError } from '../api'
import { formatDate } from '../utils/format'
interface Device {
  id: string
  deviceType: string
  browser: string
  createdAt: string
  expiresAt: string
  lastUsedAt: string
  current: boolean
}
interface Settings {
  canRememberDevice: boolean
  requiresTwoStep: boolean
  rememberedDeviceDays: number
  devices: Device[]
  canChangeVerification?: boolean
  loginOtpEnabled?: boolean | null
  verificationPolicyMessage?: string | null
}
const settings = ref<Settings | null>(null),
  pending = ref<string | null>(null),
  error = ref(''),
  notice = ref('')
const confirming = ref(false),
  nextEnabled = ref(false),
  currentPassword = ref('')
function changeVerification() {
  if (!settings.value?.canChangeVerification || pending.value) return
  nextEnabled.value = !settings.value.requiresTwoStep
  currentPassword.value = ''
  error.value = ''
  notice.value = ''
  confirming.value = true
}
async function saveVerification() {
  if (
    pending.value ||
    !confirming.value ||
    !settings.value?.canChangeVerification ||
    !currentPassword.value
  )
    return
  pending.value = 'verification'
  error.value = ''
  notice.value = ''
  try {
    settings.value = await api.post<Settings>('/api/v1/auth/login-verification', {
      enabled: nextEnabled.value,
      currentPassword: currentPassword.value
    })
    confirming.value = false
    notice.value =
      'Two-step verification is ' +
      (settings.value.requiresTwoStep ? 'on.' : 'off.') +
      ' Remembered sign-ins have been removed.'
  } catch (cause) {
    showError(cause)
  } finally {
    currentPassword.value = ''
    pending.value = null
  }
}
onUnmounted(() => {
  currentPassword.value = ''
})
function showError(cause: unknown) {
  error.value = cause instanceof ApiError ? cause.message : 'Please try again shortly.'
}
async function load() {
  settings.value = await api.get<Settings>('/api/v1/auth/devices')
}
onMounted(() => {
  void load().catch(showError)
})
async function revoke(id?: string) {
  if (pending.value) return
  const thisDevice = !!settings.value?.devices.some((device) => device.id === id && device.current)
  pending.value = id ?? 'all'
  error.value = ''
  notice.value = ''
  try {
    await api.post(
      id ? '/api/v1/auth/devices/' + id + '/revoke' : '/api/v1/auth/devices/revoke-all'
    )
    await load()
    notice.value = id
      ? thisDevice && settings.value?.requiresTwoStep
        ? 'This device will ask for a code the next time you sign in.'
        : 'That remembered sign-in has been removed.'
      : settings.value?.requiresTwoStep
        ? 'A code is now required the next time you sign in on any device.'
        : 'All remembered sign-ins have been removed.'
  } catch (cause) {
    showError(cause)
  } finally {
    pending.value = null
  }
}
useFeedback(error, undefined, notice)
</script>
<template>
  <section
    class="surface-panel ops-panel device-settings w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <h2 class="w3-sans-serif w3-large">Two-Step Verification</h2>
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-if="notice"
      class="device-notice w3-small w3-text-grey"
      role="status"
    >
      {{ notice }}
    </p>
    <p
      v-if="!settings && !error"
      class="field-hint w3-small w3-text-grey w3-block"
    >
      Loading remembered devices…
    </p>
    <template v-if="settings">
      <div class="verification-setting w3-section">
        <div>
          <strong>{{ settings.requiresTwoStep ? 'On' : 'Off' }}</strong>
          <span>
            {{
              settings.verificationPolicyMessage ||
              (settings.requiresTwoStep
                ? 'Use a code after your password when you sign in.'
                : 'Sign in with your password.')
            }}
          </span>
        </div>
        <button
          type="button"
          role="switch"
          class="verification-switch w3-button w3-round-large"
          :aria-checked="settings.requiresTwoStep"
          :aria-label="
            settings.requiresTwoStep
              ? 'Turn Off Two-Step Verification'
              : 'Turn On Two-Step Verification'
          "
          :disabled="!settings.canChangeVerification || pending !== null || confirming"
          @click="changeVerification"
        >
          <span>
            {{
              settings.requiresTwoStep
                ? 'Turn Off Two-Step Verification'
                : 'Turn On Two-Step Verification'
            }}
          </span>
        </button>
      </div>
      <form
        v-if="confirming"
        class="ops-form verification-confirmation w3-bar"
        @submit.prevent="saveVerification"
      >
        <h3 class="w3-large w3-sans-serif">
          {{ nextEnabled ? 'Turn On Two-Step Verification?' : 'Turn Off Two-Step Verification?' }}
        </h3>
        <p>
          {{
            nextEnabled
              ? 'A code will be requested after your password on your next sign-in.'
              : 'Your password will be required without a sign-in code.'
          }}
          Remembered sign-ins will be removed.
        </p>
        <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
          Current Password
          <PasswordInput
            v-model="currentPassword"
            required
            autocomplete="current-password"
            :maxlength="reference.password?.maximum ?? undefined"
            :disabled="pending !== null"
          />
        </label>
        <div class="verification-actions w3-section">
          <button
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="pending !== null"
            @click="
              () => {
                confirming = false
                currentPassword = ''
              }
            "
          >
            Cancel
          </button>
          <button
            type="submit"
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="pending !== null || !currentPassword"
          >
            {{ pending === 'verification' ? 'Saving…' : nextEnabled ? 'Turn On' : 'Turn Off' }}
          </button>
        </div>
      </form>
      <p
        v-if="settings.requiresTwoStep && !settings.canRememberDevice"
        class="field-hint w3-small w3-text-grey w3-block"
      >
        A code is required each time you sign in.
      </p>
      <template v-if="settings.canRememberDevice || settings.devices.length">
        <p v-if="settings.canRememberDevice">
          Your password is always required. Remove a remembered sign-in to require a code next time.
        </p>
        <h3 class="w3-large w3-sans-serif">Remembered Devices</h3>
        <p
          v-if="!settings.devices.length"
          class="field-hint w3-small w3-text-grey w3-block"
        >
          No remembered devices.
        </p>
        <ul
          v-else
          class="remembered-device-list w3-ul w3-section"
        >
          <li
            v-for="device in settings.devices"
            :key="device.id"
          >
            <div class="device-summary w3-section">
              <strong>{{ device.browser }} · {{ device.deviceType }}</strong>
              <span
                class="device-context w3-block w3-small w3-text-grey"
                :class="{ 'device-current': device.current }"
              >
                {{ device.current ? 'This device' : 'Another remembered sign-in' }}
              </span>
              <small class="w3-small">Last used {{ formatDate(device.lastUsedAt) }}</small>
              <small class="w3-small">Expires {{ formatDate(device.expiresAt) }}</small>
            </div>
            <button
              type="button"
              class="button button-secondary device-remove w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
              :disabled="pending !== null"
              :aria-busy="pending === device.id"
              :aria-label="
                device.current
                  ? 'Remove This Remembered Device'
                  : 'Remove ' +
                    device.browser +
                    ' ' +
                    device.deviceType +
                    ' sign-in last used ' +
                    formatDate(device.lastUsedAt)
              "
              @click="revoke(device.id)"
            >
              {{ pending === device.id ? 'Removing…' : 'Remove' }}
            </button>
          </li>
        </ul>
      </template>
      <div
        v-if="settings.devices.length"
        class="all-device-action w3-section"
      >
        <button
          type="button"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="pending !== null"
          :aria-busy="pending === 'all'"
          @click="revoke()"
        >
          {{
            pending === 'all'
              ? 'Updating Devices…'
              : settings.requiresTwoStep
                ? 'Require Codes On All Devices'
                : 'Remove All Remembered Devices'
          }}
        </button>
        <small class="w3-small">
          {{
            settings.requiresTwoStep
              ? 'All remembered sign-ins will ask for a code next time.'
              : 'Remove saved sign-in trust from every device.'
          }}
        </small>
      </div>
    </template>
  </section>
</template>
