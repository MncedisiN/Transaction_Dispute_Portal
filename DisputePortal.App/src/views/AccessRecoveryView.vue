<script setup lang="ts">
import AuthLayout from '../components/AuthLayout.vue'
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import PasswordInput from '../components/PasswordInput.vue'
import BackLink from '../components/BackLink.vue'
import { readVerification, saveVerification, clearVerification } from '../utils/pendingVerification'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ShieldCheck } from '@lucide/vue'
import { api, ApiError } from '../api'
import { useAuthStore, type IdentityChallenge } from '../stores/auth'
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const registering = computed(() => route.path === '/register')
const confirmPassword = ref('')
const firstName = ref('')
const lastName = ref('')
const contact = ref('')
const channel = ref('email')
const password = ref('')
const code = ref('')
const challenge = ref<IdentityChallenge | null>(null)
const busy = ref(false)
const error = ref('')
const channels = ref<string[]>([])
const ready = ref(false)
let flowGeneration = 0
onUnmounted(() => {
  flowGeneration++
  password.value = ''
  code.value = ''
})
function restore() {
  const value = readVerification(registering.value ? 'registration' : 'recovery')
  challenge.value = value
    ? {
        challengeId: value.id,
        expiresAt: value.expiresAt,
        maskedDestination: value.maskedDestination,
        channel: value.channel ?? 'email',
        requiresVerification: true
      }
    : null
}
restore()
async function loadChannels() {
  ready.value = false
  try {
    const result = await api.get<{ channels: string[] }>('/api/v1/identity/channels')
    channels.value = ['email', 'sms', 'whatsapp'].filter((value) => result.channels.includes(value))
  } catch {
    channels.value = []
  } finally {
    if (!channels.value.includes(channel.value)) channel.value = channels.value[0] ?? 'email'
    ready.value = true
  }
}
onMounted(loadChannels)
watch(
  () => route.path,
  () => {
    flowGeneration++
    challenge.value = null
    code.value = ''
    password.value = ''
    error.value = ''
    restore()
  }
)
async function submit() {
  if (busy.value || (!challenge.value && (!ready.value || !channels.value.includes(channel.value))))
    return
  const generation = flowGeneration
  const isRegistering = registering.value
  busy.value = true
  error.value = ''
  try {
    if (
      (registering.value ? !challenge.value : !!challenge.value) &&
      password.value !== confirmPassword.value
    ) {
      error.value = 'Passwords must match.'
      return
    }
    if (!challenge.value) {
      const requested = await api.post<IdentityChallenge>(
        registering.value ? '/api/v1/identity/register' : '/api/v1/identity/password/forgot',
        registering.value
          ? {
              firstName: firstName.value,
              lastName: lastName.value,
              contact: contact.value,
              channel: channel.value,
              password: password.value
            }
          : { contact: contact.value, channel: channel.value }
      )
      if (generation !== flowGeneration || isRegistering !== registering.value) return
      challenge.value = requested
      if (challenge.value)
        saveVerification(registering.value ? 'registration' : 'recovery', {
          id: challenge.value.challengeId,
          expiresAt: challenge.value.expiresAt,
          maskedDestination: challenge.value.maskedDestination,
          channel: challenge.value.channel
        })
      password.value = ''
      confirmPassword.value = ''
    } else {
      await api.post(
        registering.value ? '/api/v1/identity/register/verify' : '/api/v1/identity/password/reset',
        registering.value
          ? { challengeId: challenge.value.challengeId, code: code.value }
          : { challengeId: challenge.value.challengeId, code: code.value, password: password.value }
      )
      clearVerification(registering.value ? 'registration' : 'recovery')
      if (!registering.value) auth.clearSession()
      await router.replace({
        path: '/sign-in',
        query: registering.value ? { registered: '1' } : { reset: '1' }
      })
    }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Please check your connection and try again.'
  } finally {
    busy.value = false
  }
}
useFeedback(error, undefined, undefined)
</script>
<template>
  <AuthLayout>
    <BackLink
      fallback="/sign-in"
      label="Back To Sign-In"
    />

    <h2 class="w3-center w3-sans-serif w3-large">
      <b>
        {{
          challenge
            ? 'Verify It Is You'
            : registering
              ? 'Let’s Get You Started'
              : 'Forgot Your Password?'
        }}
      </b>
    </h2>
    <p class="auth-intro w3-center w3-small w3-text-grey w3-margin-bottom">
      {{
        challenge
          ? registering
            ? 'Enter the code sent to ' + challenge.maskedDestination + '.'
            : 'Request received. Enter the code from your registered contact and choose a new password.'
          : registering
            ? 'Just a few details, followed by a security check.'
            : 'Use your registered email, mobile number or username.'
      }}
    </p>
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-if="ready && !channels.length && !challenge"
      class="info-notice verification-unavailable w3-panel w3-pale-blue w3-leftbar w3-border-blue"
      role="status"
    >
      Verification is temporarily unavailable. Please try again shortly.
      <button
        class="w3-button w3-round-large"
        type="button"
        @click="loadChannels"
      >
        Try Again
      </button>
    </p>
    <form
      class="auth-form"
      @submit.prevent="submit"
    >
      <template v-if="!challenge">
        <template v-if="registering">
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="new-first-name"
          >
            First Name
          </label>
          <input
            id="new-first-name"
            v-model.trim="firstName"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            required
            maxlength="100"
            autocomplete="given-name"
          />
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="new-last-name"
          >
            Surname
          </label>
          <input
            id="new-last-name"
            v-model.trim="lastName"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            required
            maxlength="100"
            autocomplete="family-name"
          />
        </template>
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="channel"
        >
          Verify Using
        </label>
        <select
          id="channel"
          v-model="channel"
          class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
          :disabled="!ready || !channels.length"
        >
          <option
            v-if="!channels.length"
            :value="channel"
          >
            {{ ready ? 'No verification methods available' : 'Checking availability…' }}
          </option>
          <option
            v-for="method in channels"
            :key="method"
            :value="method"
          >
            {{ method === 'email' ? 'Email' : method === 'sms' ? 'SMS' : 'WhatsApp' }}
          </option>
        </select>
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="contact"
        >
          {{
            registering
              ? channel === 'email'
                ? 'Email Address'
                : 'Mobile Number'
              : 'Email, Mobile Or Username'
          }}
        </label>
        <input
          id="contact"
          v-model.trim="contact"
          class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          :type="registering ? (channel === 'email' ? 'email' : 'tel') : 'text'"
          :autocomplete="registering ? (channel === 'email' ? 'email' : 'tel') : 'username'"
          :inputmode="registering ? (channel === 'email' ? 'email' : 'tel') : 'text'"
          :placeholder="
            registering
              ? channel === 'email'
                ? 'you@example.com'
                : '082 123 4567'
              : 'Email, mobile or username'
          "
          required
          maxlength="320"
        />
      </template>
      <template v-else>
        <div class="verification-step w3-section">
          <span>1 · Request received</span>
          <strong>2 · Verify ownership</strong>
        </div>
        <p
          v-if="!registering"
          class="auth-hint w3-small w3-text-grey w3-section"
          role="status"
        >
          Codes expire after 5 minutes. If yours does not arrive, choose Start again and try another
          verified channel.
        </p>
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="verification-code"
        >
          Six-Digit Verification Code
        </label>
        <input
          id="verification-code"
          v-model.trim="code"
          class="otp-input w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          inputmode="numeric"
          autocomplete="one-time-code"
          pattern="[0-9]{6}"
          maxlength="6"
          required
          placeholder="000000"
        />
      </template>
      <template v-if="registering ? !challenge : !!challenge">
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="new-password"
        >
          {{ registering ? 'Create A Password' : 'New Password' }}
        </label>
        <PasswordInput
          id="new-password"
          v-model="password"
          autocomplete="new-password"
          required
          :minlength="reference.password?.minimum"
          :maxlength="reference.password?.maximum ?? undefined"
        />
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="confirm-password"
        >
          Confirm Password
        </label>
        <PasswordInput
          id="confirm-password"
          v-model="confirmPassword"
          autocomplete="new-password"
          required
          :minlength="reference.password?.minimum"
          :maxlength="reference.password?.maximum ?? undefined"
        />
        <small class="auth-hint w3-small">
          At least 12 characters, with upper and lower case letters, a number and a symbol.
        </small>
      </template>
      <button
        class="button button-primary auth-submit w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        :disabled="busy || (!challenge && !channels.includes(channel))"
      >
        {{
          busy
            ? 'Please Wait…'
            : challenge
              ? registering
                ? 'Verify & Create Account'
                : 'Verify & Reset Password'
              : 'Continue Securely'
        }}
      </button>
      <button
        v-if="challenge"
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        type="button"
        :disabled="busy"
        @click="
          () => {
            clearVerification(registering ? 'registration' : 'recovery')
            challenge = null
            code = ''
            password = ''
          }
        "
      >
        Start Again
      </button>
    </form>
    <div class="auth-legal w3-small w3-section">
      <ShieldCheck :size="15" />
      <span>
        Your contact must be verified before access is granted. We never send passwords in messages.
      </span>
    </div>
  </AuthLayout>
</template>
