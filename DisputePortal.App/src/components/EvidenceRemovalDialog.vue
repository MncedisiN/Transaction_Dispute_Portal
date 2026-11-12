<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { X } from '@lucide/vue'
import { api, ApiError } from '../api'
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'
import type { EvidenceFile } from '../utils/evidence'

const props = defineProps<{ caseId: string; file: EvidenceFile }>()
const emit = defineEmits<{ close: []; removed: [] }>()
const dialog = ref<HTMLDialogElement>()
const reason = ref(''),
  error = ref(''),
  busy = ref(false)
const request = new AbortController()
let disposed = false
const valid = computed(
  () =>
    reason.value.trim().length >= (reference.evidenceReason?.minimum ?? Infinity) &&
    reason.value.trim().length <= (reference.evidenceReason?.maximum ?? 0)
)

async function remove() {
  if (busy.value || !valid.value) return
  busy.value = true
  error.value = ''
  try {
    await api.post(
      '/api/v1/disputes/' + props.caseId + '/evidence/' + props.file.id + '/remove',
      { reason: reason.value.trim() },
      undefined,
      request.signal
    )
    if (!disposed) emit('removed')
  } catch (failure) {
    if (!disposed && !request.signal.aborted)
      error.value =
        failure instanceof ApiError
          ? failure.message
          : 'The file could not be removed. Please retry.'
  } finally {
    if (!disposed) busy.value = false
  }
}
onMounted(() => dialog.value?.showModal())
onUnmounted(() => {
  disposed = true
  request.abort()
  dialog.value?.close()
})
useFeedback(error, undefined, undefined)
</script>

<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="evidence-dialog w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m10 l8"
      aria-label="Remove Evidence"
      @close="emit('close')"
    >
      <div class="w3-bar">
        <header class="evidence-modal-header w3-bar w3-padding-16">
          <button
            class="icon-button w3-button w3-round-large w3-right"
            aria-label="Close Remove Evidence"
            :disabled="busy"
            @click="dialog?.close()"
          >
            <X :size="22" />
          </button>
          <h2 class="w3-sans-serif w3-large">Remove Evidence</h2>
        </header>
        <p>
          This file will no longer be available to view with your dispute. We keep a secure record
          of this change.
        </p>
        <form
          class="evidence-access-form w3-bar"
          @submit.prevent="remove"
        >
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Reason
            <textarea
              v-model="reason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              rows="3"
              required
              :disabled="busy"
              :minlength="reference.evidenceReason?.minimum"
              :maxlength="reference.evidenceReason?.maximum ?? undefined"
              placeholder="Why are you removing this file?"
            />
          </label>
          <button
            class="button button-primary full-width w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="busy || !valid"
          >
            {{ busy ? 'Removing...' : 'Remove File' }}
          </button>
          <button
            type="button"
            class="button button-secondary full-width w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="busy"
            @click="dialog?.close()"
          >
            Cancel
          </button>
        </form>
      </div>
    </dialog>
  </Teleport>
</template>
