<script setup lang="ts">
import { requestPushPermissionOnSignIn } from '../pushNotifications'
import AuthLayout from '../components/AuthLayout.vue'
import PasswordInput from '../components/PasswordInput.vue'
import { formatDate } from '../utils/format'
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowRight } from '@lucide/vue'
import { useAuthStore } from '../stores/auth'
import { ApiError } from '../api'
import { previousDestination } from '../utils/backNavigation'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const loginIdentifier = ref('')
const password = ref('')
const otpCode = ref('')
const rememberDevice = ref(false)
const resending = ref(false)
const notice = ref('')
const now = ref(Date.now())
const resendUntil = ref(0)
const resendSeconds = computed(() =>
  Math.max(
    0,
    Math.ceil(
      (Math.max(
        resendUntil.value,
        Date.parse(auth.pendingChallenge?.resendAvailableAt ?? '') || 0
      ) -
        now.value) /
        1000
    )
  )
)
const resendClock = computed(
  () =>
    String(Math.floor(resendSeconds.value / 60)).padStart(2, '0') +
    ':' +
    String(resendSeconds.value % 60).padStart(2, '0')
)
let timer: ReturnType<typeof setInterval> | undefined
function updateClock() {
  now.value = Date.now()
}
onMounted(() => {
  timer = setInterval(updateClock, 1000)
  document.addEventListener('visibilitychange', updateClock)
})
onUnmounted(() => {
  clearInterval(timer)
  document.removeEventListener('visibilitychange', updateClock)
})
async function resend() {
  if (resending.value || loading.value || resendSeconds.value) return
  resending.value = true
  error.value = ''
  notice.value = ''
  otpCode.value = ''
  try {
    await auth.resendSignIn()
    notice.value = 'A new code has been sent.'
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : 'Please try again shortly.'
    if (cause instanceof ApiError && (cause.status === 409 || cause.status === 429))
      resendUntil.value = Date.now() + 60000
  } finally {
    resending.value = false
    updateClock()
  }
}
const verifying = computed(() => !!auth.pendingChallenge)
function restart() {
  auth.cancelVerification()
  otpCode.value = ''
  password.value = ''
  error.value = ''
  notice.value = ''
  rememberDevice.value = false
  resendUntil.value = 0
}
const loading = ref(false)
const error = ref('')

async function submit() {
  error.value = ''
  loading.value = true
  void requestPushPermissionOnSignIn()
  try {
    const user = verifying.value
      ? await auth.verifySignIn(otpCode.value, rememberDevice.value)
      : await auth.signIn(loginIdentifier.value, password.value)
    password.value = ''
    if (!user) return
    const fallback = auth.isStaffWorkspace ? '/admin/disputes' : '/personal'
    const destination = previousDestination(route.query.redirect, '/sign-in', fallback) ?? fallback
    await router.replace(destination)
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) {
      error.value = verifying.value
        ? 'Check your code and try again.'
        : 'Check your sign-in details and try again.'
    } else if (cause instanceof ApiError && cause.status === 429) {
      error.value = 'For your security, wait a moment before trying again.'
    } else if (cause instanceof ApiError && cause.status >= 500) {
      error.value =
        'Sign-in is temporarily unavailable. Your information remains protected. Please try again shortly.'
    } else if (cause instanceof ApiError) {
      error.value = cause.message
    } else {
      error.value = 'We are having trouble connecting right now. Please try again shortly.'
    }
  } finally {
    loading.value = false
  }
}
useFeedback(error, undefined, notice)
</script>

<template>
  <AuthLayout>
    <h2 class="w3-center w3-sans-serif w3-large">
      <b>{{ verifying ? 'Two-Step Verification' : 'Welcome Back' }}</b>
    </h2>
    <p
      v-if="route.query.registered"
      class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
      role="status"
    >
      Your account is verified. Sign in to continue.
    </p>
    <p
      v-if="route.query.reset"
      class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
      role="status"
    >
      Your password has been updated. All devices have been signed out. Sign in with your new
      password.
    </p>
    <p
      v-if="route.query.updated === 'contact'"
      class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
      role="status"
    >
      Your contact details are verified and saved. Sign in with your registered contact.
    </p>
    <p class="auth-intro w3-center w3-small w3-text-grey w3-margin-bottom">
      {{
        verifying
          ? `Enter the verification code sent to ${auth.pendingChallenge?.maskedDestination}.`
          : 'Your secure dispute workspace.'
      }}
    </p>
    <form
      class="auth-form"
      @submit.prevent="submit"
    >
      <template v-if="!verifying">
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="login"
        >
          Email Or Mobile Number
        </label>
        <input
          id="login"
          v-model.trim="loginIdentifier"
          class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          type="text"
          autocomplete="username"
          maxlength="320"
          placeholder="Email Or Mobile Number"
          required
        />
        <div class="password-label w3-bar w3-margin-top">
          <label
            class="w3-left w3-padding-small w3-text-dark-grey w3-medium"
            for="password"
          >
            Password
          </label>
          <RouterLink
            class="w3-right w3-small w3-button w3-padding-small w3-round-large"
            to="/forgot-password"
          >
            Forgot Password?
          </RouterLink>
        </div>
        <PasswordInput
          id="password"
          v-model="password"
          autocomplete="current-password"
          :maxlength="reference.password?.maximum ?? undefined"
          placeholder="Enter Your Password"
          required
        />
      </template>
      <template v-else>
        <div class="verification-step w3-section">
          <span>1 · Details verified</span>
          <strong>2 · Security check</strong>
        </div>
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="otp"
        >
          Verification Code
        </label>
        <input
          id="otp"
          v-model.trim="otpCode"
          class="otp-input w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          inputmode="numeric"
          autocomplete="one-time-code"
          pattern="[0-9]{6}"
          maxlength="6"
          placeholder="000000"
          required
          autofocus
        />
        <p class="auth-hint w3-small w3-text-grey w3-section">
          Your code expires at
          {{
            formatDate(auth.pendingChallenge!.expiresAt, {
              hour: 'numeric',
              minute: '2-digit'
            })
          }}. Never share your code.
        </p>
        <label
          v-if="auth.pendingChallenge?.canRememberDevice"
          class="remember-device-option w3-block w3-margin-top w3-text-dark-grey w3-medium"
        >
          <input
            v-model="rememberDevice"
            class="w3-check"
            type="checkbox"
          />
          <span>
            Don’t Require An OTP When Using This Device
            <small class="w3-small">
              Personal Devices Only ·
              {{ auth.pendingChallenge.rememberedDeviceDays }}
              Days
            </small>
          </span>
        </label>
        <p
          v-if="notice"
          class="verification-notice w3-panel w3-light-grey w3-padding w3-round-large"
          role="status"
        >
          {{ notice }}
        </p>
      </template>
      <div
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
      </div>
      <button
        class="button button-primary auth-submit w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        type="submit"
        :disabled="loading || resending"
      >
        <span>
          {{
            loading ? 'Verifying Securely…' : verifying ? 'Verify & Sign In' : 'Sign In Securely'
          }}
        </span>
        <ArrowRight
          v-if="!loading"
          :size="18"
        />
      </button>
      <div
        v-if="verifying"
        class="verification-resend w3-section"
      >
        <span>Didn’t receive the OTP?</span>
        <button
          class="w3-button w3-round-large"
          type="button"
          :disabled="loading || resending || resendSeconds > 0"
          @click="resend"
        >
          {{ resending ? 'Sending…' : resendSeconds ? `Resend In ${resendClock}` : 'Resend OTP' }}
        </button>
      </div>
      <button
        v-if="verifying"
        type="button"
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        :disabled="loading"
        @click="restart"
      >
        Back To Sign-In
      </button>
    </form>
    <p
      v-if="!verifying"
      class="auth-switch w3-center w3-section"
    >
      New here?
      <RouterLink to="/register">Create Account</RouterLink>
    </p>
  </AuthLayout>
</template>
