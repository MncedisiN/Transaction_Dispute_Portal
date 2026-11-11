<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { onUnmounted, ref, watch } from 'vue'
import { Eye, EyeOff } from '@lucide/vue'
import { api, ApiError } from '../api'
const props = defineProps<{ owner: string; masked: string }>()
const identity = ref(''),
  busy = ref(false),
  error = ref('')
let controller: AbortController | undefined,
  timer: ReturnType<typeof setTimeout> | undefined,
  generation = 0,
  disposed = false
function hide() {
  generation++
  controller?.abort()
  clearTimeout(timer)
  identity.value = ''
  busy.value = false
  error.value = ''
}
async function toggle() {
  if (identity.value) {
    hide()
    return
  }
  if (busy.value) return
  const current = ++generation
  controller = new AbortController()
  busy.value = true
  error.value = ''
  try {
    const result = await api.get<{ identityNumber: string }>(
      '/api/v1/profile/identity/number',
      controller.signal
    )
    if (disposed || generation !== current) return
    identity.value = result.identityNumber
    timer = setTimeout(hide, 30000)
  } catch (cause) {
    if (!disposed && generation === current)
      error.value = cause instanceof ApiError ? cause.message : 'Identity Details are unavailable.'
  } finally {
    if (generation === current) busy.value = false
  }
}
function visibility() {
  if (document.visibilityState !== 'visible') hide()
}
watch(() => props.owner, hide)
document.addEventListener('visibilitychange', visibility)
onUnmounted(() => {
  disposed = true
  hide()
  document.removeEventListener('visibilitychange', visibility)
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <div class="identity-number-control">
    <div>
      <strong>{{ identity || masked }}</strong>
      <button
        :class="{ 'w3-theme-l4 w3-text-black': !!identity === true }"
        type="button"
        class="icon-button w3-button w3-round-large"
        :disabled="busy"
        :aria-label="identity ? 'Hide ID number' : 'Show ID number'"
        :aria-pressed="!!identity"
        @click="toggle"
      >
        <EyeOff
          v-if="identity"
          :size="19"
        />
        <Eye
          v-else
          :size="19"
        />
      </button>
      <span class="muted w3-text-grey">Locked</span>
    </div>
    <p
      v-if="error"
      role="alert"
    >
      {{ error }}
    </p>
  </div>
</template>
