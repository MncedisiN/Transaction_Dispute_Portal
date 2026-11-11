<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { formatDate } from '../utils/format'
import { onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ChevronRight, Monitor, Smartphone, X } from '@lucide/vue'
import { api, ApiError } from '../api'
import { useAuthStore } from '../stores/auth'
interface SignIn {
  id: string
  eventType: string
  outcome: string
  ipAddress: string
  deviceType: string
  browser: string
  location: string
  occurredAt: string
}
interface SignInDetail extends SignIn {
  userAgent: string
  sessionId: string | null
  sessionActive: boolean
  isCurrentSession: boolean
}
const activity = ref<SignIn[]>([]),
  detail = ref<SignInDetail | null>(null),
  error = ref(''),
  detailError = ref(''),
  loading = ref(true),
  opening = ref(false),
  busy = ref(false),
  sheet = ref<HTMLDialogElement | null>(null)
const auth = useAuthStore(),
  router = useRouter()
let request: AbortController | undefined,
  detailRequest: AbortController | undefined,
  generation = 0
const timestamp = (value: string) => formatDate(value)
async function load() {
  request?.abort()
  request = new AbortController()
  loading.value = true
  error.value = ''
  try {
    activity.value = await api.get<SignIn[]>('/api/v1/security/sign-ins', request.signal)
  } catch {
    if (!request.signal.aborted) error.value = 'Please try loading your sign-ins again.'
  } finally {
    loading.value = false
  }
}
async function open(item: SignIn) {
  const current = ++generation
  detailRequest?.abort()
  detailRequest = new AbortController()
  detail.value = null
  detailError.value = ''
  opening.value = true
  sheet.value?.showModal()
  try {
    const result = await api.get<SignInDetail>(
      '/api/v1/security/sign-ins/' + item.id,
      detailRequest.signal
    )
    if (current === generation) detail.value = result
  } catch {
    if (current === generation)
      detailError.value = 'Sign-in details are unavailable. Please try again.'
  } finally {
    if (current === generation) opening.value = false
  }
}
function close() {
  generation++
  detailRequest?.abort()
  sheet.value?.close()
}
async function revoke() {
  if (!detail.value?.sessionId || busy.value) return
  busy.value = true
  detailError.value = ''
  try {
    await api.post('/api/v1/security/sessions/' + detail.value.sessionId + '/revoke')
    if (detail.value.isCurrentSession) {
      auth.user = null
      close()
      await router.replace('/sign-in')
    } else detail.value.sessionActive = false
  } catch (e) {
    detailError.value = e instanceof ApiError ? e.message : 'Please try again.'
  } finally {
    busy.value = false
  }
}
onMounted(load)
onUnmounted(() => {
  generation++
  request?.abort()
  detailRequest?.abort()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <section
    class="surface-panel ops-panel signin-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <div class="ops-heading w3-bar w3-padding-16">
      <div>
        <h2 class="w3-sans-serif w3-large">Last 5 Sign-Ins</h2>
        <p class="muted w3-text-grey">Select a sign-in to review its details.</p>
      </div>
      <button
        class="text-button w3-button w3-text-black w3-round-large"
        :disabled="loading"
        @click="load"
      >
        Refresh
      </button>
    </div>
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-if="loading && !activity.length"
      class="muted w3-text-grey"
    >
      Loading sign-ins…
    </p>
    <ol class="recent-signins w3-ul w3-section">
      <li
        v-for="event in activity"
        :key="event.id"
      >
        <button
          class="signin-row w3-button w3-round-large"
          @click="open(event)"
        >
          <span class="signin-icon">
            <Smartphone
              v-if="['Android', 'iPhone', 'iPad'].includes(event.deviceType)"
              :size="19"
            />
            <Monitor
              v-else
              :size="19"
            />
          </span>
          <span class="signin-copy w3-block w3-section">
            <strong>{{ event.deviceType }} · {{ event.browser }}</strong>
            <time :datetime="event.occurredAt">{{ timestamp(event.occurredAt) }}</time>
          </span>
          <ChevronRight :size="16" />
        </button>
      </li>
    </ol>
    <p
      v-if="!loading && !activity.length && !error"
      class="muted w3-text-grey"
    >
      Your next successful sign-in will appear here.
    </p>
    <dialog
      ref="sheet"
      class="filter-sheet signin-sheet w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
      aria-label="Sign-In Details"
      @cancel="close"
      @click="
        (event) => {
          if (event.target === sheet) close()
        }
      "
    >
      <div class="w3-bar">
        <div class="sheet-heading w3-bar w3-padding-16">
          <button
            class="icon-button w3-button w3-round-large w3-right"
            aria-label="Close Sign-In Details"
            @click="close"
          >
            <X :size="19" />
          </button>
          <h2 class="w3-sans-serif w3-large">Sign-In Details</h2>
        </div>
        <p
          v-if="opening"
          class="muted w3-text-grey"
        >
          Loading security details…
        </p>
        <p
          v-if="detailError"
          class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
          role="alert"
        >
          {{ detailError }}
        </p>
        <template v-if="detail">
          <span class="ops-pill w3-tag w3-light-grey w3-round-large w3-small w3-padding-small">
            {{ detail.isCurrentSession ? 'This device' : detail.outcome }}
          </span>
          <div class="w3-responsive">
            <table class="signin-details w3-table w3-bordered">
              <tbody>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Signed In
                  </th>
                  <td class="w3-mobile">{{ timestamp(detail.occurredAt) }}</td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Device
                  </th>
                  <td class="w3-mobile">{{ detail.deviceType }}</td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Browser
                  </th>
                  <td class="w3-mobile">{{ detail.browser }}</td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    IP Address
                  </th>
                  <td class="mono-value w3-codespan w3-mobile">
                    {{ detail.ipAddress }}
                  </td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Location
                  </th>
                  <td class="w3-mobile">{{ detail.location }}</td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Session
                  </th>
                  <td class="w3-mobile">
                    {{
                      !detail.sessionId
                        ? 'Unavailable for this older sign-in'
                        : detail.sessionActive
                          ? 'Active'
                          : 'Ended'
                    }}
                  </td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Browser Information
                  </th>
                  <td class="user-agent-value w3-mobile">
                    {{ detail.userAgent }}
                  </td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Event Reference
                  </th>
                  <td class="mono-value w3-codespan w3-mobile">
                    {{ detail.id }}
                  </td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    UTC Timestamp
                  </th>
                  <td class="w3-mobile">{{ new Date(detail.occurredAt).toISOString() }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p class="security-detail-note w3-small w3-text-grey">
            Device details are reported by the browser. Location is shown only when available.
          </p>
          <button
            v-if="detail.sessionActive && detail.sessionId"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="busy"
            @click="revoke"
          >
            {{
              busy
                ? 'Signing Out…'
                : detail.isCurrentSession
                  ? 'Sign Out This Device'
                  : 'Sign Out That Device'
            }}
          </button>
        </template>
      </div>
    </dialog>
  </section>
</template>
