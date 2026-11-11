<script setup lang="ts">
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import PasswordInput from './PasswordInput.vue'
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
import { api, ApiError } from '../api'
import { formatDate } from '../utils/format'
import { useAuthStore } from '../stores/auth'
import { readVerification, saveVerification, clearVerification } from '../utils/pendingVerification'
const auth = useAuthStore()
interface DeletionRequest {
  id: string
  reference: string
  status: string
  createdAt: string
  decision?: string
}
const requests = ref<DeletionRequest[]>([]),
  dialog = ref<HTMLDialogElement>(),
  password = ref(''),
  reason = ref(''),
  code = ref(''),
  challenge = ref<{ id: string; maskedDestination: string; expiresAt: string } | null>(null),
  error = ref(''),
  busy = ref(false)
let controller: AbortController | undefined,
  disposed = false
async function load() {
  controller?.abort()
  controller = new AbortController()
  try {
    const result = await api.get<DeletionRequest[]>(
      '/api/v1/account-management/deletion',
      controller.signal
    )
    if (!disposed) requests.value = result
  } catch (cause) {
    if (!disposed && !controller.signal.aborted)
      error.value = cause instanceof ApiError ? cause.message : 'Requests are unavailable.'
  }
}
function requestStatusLabel(status: string) {
  return status.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function open() {
  password.value = ''
  code.value = ''
  reason.value = ''
  challenge.value = null
  error.value = ''
  dialog.value?.showModal()
}
function close() {
  clearVerification('deletion', auth.user?.id)
  password.value = ''
  code.value = ''
  challenge.value = null
  dialog.value?.close()
}
async function submit() {
  if (busy.value) return
  const owner = auth.user?.id
  busy.value = true
  error.value = ''
  try {
    if (!challenge.value) {
      challenge.value = await api.post('/api/v1/account-management/deletion/start', {
        currentPassword: password.value
      })
      if (disposed || owner !== auth.user?.id) return
      if (challenge.value)
        saveVerification(
          'deletion',
          {
            id: challenge.value.id,
            expiresAt: challenge.value.expiresAt,
            maskedDestination: challenge.value.maskedDestination
          },
          auth.user?.id
        )
      password.value = ''
    } else {
      await api.post('/api/v1/account-management/deletion/confirm', {
        challengeId: challenge.value.id,
        code: code.value,
        reason: reason.value.trim() || null
      })
      close()
      await load()
    }
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : 'Please try again.'
  } finally {
    busy.value = false
  }
}
async function cancel(id: string) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await api.post('/api/v1/account-management/deletion/' + id + '/cancel')
    await load()
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : 'Please try again.'
  } finally {
    busy.value = false
  }
}
onMounted(async () => {
  await load()
  if (disposed) return
  const saved = readVerification('deletion', auth.user?.id)
  if (saved && !requests.value.some((item) => ['Pending', 'UnderReview'].includes(item.status))) {
    challenge.value = {
      id: saved.id,
      maskedDestination: saved.maskedDestination,
      expiresAt: saved.expiresAt
    }
    await nextTick()
    dialog.value?.showModal()
  }
})
onUnmounted(() => {
  disposed = true
  controller?.abort()
  password.value = ''
  code.value = ''
  dialog.value?.close()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <section
    class="surface-panel ops-panel deletion-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <h2 class="w3-sans-serif w3-large">Account Closure Requests</h2>
    <p
      v-if="error && !dialog?.open"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <article
      v-for="request in requests"
      :key="request.id"
      class="deletion-request w3-panel w3-light-grey w3-padding-large w3-round-large"
    >
      <div>
        <strong class="w3-block w3-margin-bottom">{{ request.reference }}</strong>
        <span class="request-status w3-tag w3-light-grey w3-round-large w3-small w3-padding-small">
          {{ requestStatusLabel(request.status) }}
        </span>
      </div>
      <time
        class="w3-block w3-small w3-text-grey w3-margin-top"
        :datetime="request.createdAt"
      >
        {{ formatDate(request.createdAt) }}
      </time>
      <p v-if="request.decision">
        {{ request.decision }}
      </p>
      <button
        v-if="['Pending', 'UnderReview'].includes(request.status)"
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        :disabled="busy"
        @click="cancel(request.id)"
      >
        Cancel Request
      </button>
    </article>
    <button
      class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      :disabled="requests.some((item) => ['Pending', 'UnderReview'].includes(item.status))"
      @click="open"
    >
      Request Account Closure
    </button>
  </section>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="account-request-dialog w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
      aria-label="Request Account Closure"
      @cancel="close"
    >
      <div class="w3-bar">
        <header>
          <button
            class="icon-button w3-button w3-round-large w3-right"
            type="button"
            aria-label="Close Closure Request"
            :disabled="busy"
            @click="close"
          >
            ×
          </button>
          <h2 class="w3-sans-serif w3-large">Close Your Account</h2>
        </header>
        <p>
          We’ll verify the request and review any linked cases before closing access. Financial and
          security records may need to be retained.
        </p>
        <p
          v-if="error"
          class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
          role="alert"
        >
          {{ error }}
        </p>
        <form
          class="ops-form w3-bar"
          @submit.prevent="submit"
        >
          <template v-if="!challenge">
            <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
              Current Password
              <PasswordInput
                v-model="password"
                autocomplete="current-password"
                required
                :maxlength="reference.password?.maximum ?? undefined"
              />
            </label>
            <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
              Reason
              <small class="w3-small">(Optional)</small>
              <textarea
                v-model="reason"
                class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                :maxlength="reference.evidenceReason?.maximum ?? undefined"
                rows="2"
              />
            </label>
          </template>
          <template v-else>
            <p>
              Enter the code sent to
              {{ challenge.maskedDestination }}.
            </p>
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
            {{ busy ? 'Please Wait…' : challenge ? 'Submit Closure Request' : 'Continue Securely' }}
          </button>
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            type="button"
            :disabled="busy"
            @click="close"
          >
            Keep My Account
          </button>
        </form>
      </div>
    </dialog>
  </Teleport>
</template>
