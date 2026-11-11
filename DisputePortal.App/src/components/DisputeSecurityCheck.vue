<script setup lang="ts">
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'
import type { DisputeIntake } from '../types'

import PasswordInput from './PasswordInput.vue'
import { onMounted, onUnmounted, ref } from 'vue'
import { useAuthStore } from '../stores/auth'
import { readVerification, saveVerification, clearVerification } from '../utils/pendingVerification'
import { ShieldCheck } from '@lucide/vue'
import { api, ApiError } from '../api'
const auth = useAuthStore()
const props = defineProps<{ transactionId: string; initialDecision?: DisputeIntake }>(),
  emit = defineEmits<{ ready: [id: string | null, specialistReview?: boolean] }>()
const manualReview = ref(false)
const decision = ref<{
    allowed: boolean
    requiresVerification: boolean
    assistedReview: boolean
    message: string
  } | null>(null),
  password = ref(''),
  code = ref(''),
  challenge = ref<{
    challengeId: string
    channel: string
    maskedDestination: string
    expiresAt: string
  } | null>(null),
  error = ref(''),
  busy = ref(false),
  verified = ref(false)
let disposed = false,
  timer: ReturnType<typeof setTimeout> | undefined
onMounted(async () => {
  try {
    const result =
      props.initialDecision ??
      (await api.get<typeof decision.value>(
        '/api/v1/transactions/' + props.transactionId + '/dispute-check'
      ))
    if (disposed) return
    decision.value = result
    if (result?.allowed && result.requiresVerification) {
      const saved = readVerification('dispute', auth.user?.id, props.transactionId)
      if (saved)
        challenge.value = {
          challengeId: saved.id,
          expiresAt: saved.expiresAt,
          maskedDestination: saved.maskedDestination,
          channel: saved.channel ?? 'email'
        }
    }
    if (result?.allowed && !result.requiresVerification) {
      clearVerification('dispute', auth.user?.id, props.transactionId)
      emit('ready', null)
    }
  } catch {
    error.value = 'Please reload to check this transaction.'
  }
})
onUnmounted(() => {
  disposed = true
  if (timer) clearTimeout(timer)
})
async function verify() {
  if (busy.value) return
  const owner = auth.user?.id
  busy.value = true
  error.value = ''
  try {
    if (!challenge.value) {
      challenge.value = await api.post(
        '/api/v1/transactions/' + props.transactionId + '/verification/start',
        { currentPassword: password.value }
      )
      if (disposed || owner !== auth.user?.id) return
      if (challenge.value)
        saveVerification(
          'dispute',
          {
            id: challenge.value.challengeId,
            expiresAt: challenge.value.expiresAt,
            maskedDestination: challenge.value.maskedDestination,
            channel: challenge.value.channel
          },
          auth.user?.id,
          props.transactionId
        )
      password.value = ''
    } else {
      await api.post('/api/v1/transactions/' + props.transactionId + '/verification/complete', {
        challengeId: challenge.value.challengeId,
        code: code.value
      })
      code.value = ''
      clearVerification('dispute', auth.user?.id, props.transactionId)
      verified.value = true
      emit('ready', challenge.value.challengeId)
      timer = setTimeout(
        () => {
          verified.value = false
          challenge.value = null
          error.value = 'Verification expired. Verify again before submitting.'
          emit('ready', '')
        },
        Math.max(0, new Date(challenge.value.expiresAt).getTime() - Date.now())
      )
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
  <section
    v-if="
      !manualReview &&
      (!decision || (decision.requiresVerification && !verified) || !decision.allowed)
    "
    class="surface-panel risk-check w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <div class="risk-heading">
      <ShieldCheck :size="24" />
      <h2 class="w3-sans-serif w3-large">
        {{
          decision?.assistedReview && !decision.allowed
            ? 'Specialist Review'
            : verified
              ? 'Identity Verified'
              : 'Security Check'
        }}
      </h2>
    </div>
    <p
      v-if="error"
      role="alert"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
    >
      {{ error }}
    </p>
    <p v-if="decision && !decision.allowed">
      {{ decision.message }}
    </p>
    <template v-if="decision && !decision.allowed">
      <button
        v-if="decision.assistedReview"
        type="button"
        class="button button-primary full-width w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        @click="
          () => {
            manualReview = true
            emit('ready', null, true)
          }
        "
      >
        Request Specialist Review
      </button>
    </template>
    <form
      v-else-if="decision?.requiresVerification && !verified"
      class="ops-form w3-bar"
      @submit.prevent="verify"
    >
      <label
        v-if="!challenge"
        class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
      >
        Current Password
        <PasswordInput
          v-model="password"
          autocomplete="current-password"
          required
          :maxlength="reference.password?.maximum ?? undefined"
        />
      </label>
      <template v-else>
        <p>{{ 'Code sent to ' + challenge.maskedDestination }}</p>
        <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
          Verification Code
          <input
            v-model.trim="code"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            inputmode="numeric"
            autocomplete="one-time-code"
            pattern="[0-9]{6}"
            maxlength="6"
            required
          />
        </label>
      </template>
      <button
        class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        :disabled="busy"
      >
        {{ busy ? 'Checking…' : challenge ? 'Verify Identity' : 'Continue Securely' }}
      </button>
      <button
        v-if="challenge"
        type="button"
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        :disabled="busy"
        @click="
          () => {
            clearVerification('dispute', auth.user?.id, transactionId)
            challenge = null
            code = ''
          }
        "
      >
        Start Again
      </button>
    </form>
  </section>
  <p
    v-else-if="decision?.assistedReview"
    class="info-notice w3-panel w3-pale-blue w3-leftbar w3-border-blue"
  >
    A specialist will investigate your request. A refund is subject to review.
  </p>
</template>
