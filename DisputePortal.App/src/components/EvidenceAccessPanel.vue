<script setup lang="ts">
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, onUnmounted, ref } from 'vue'
import { X, LockKeyhole } from '@lucide/vue'
import { api, ApiError } from '../api'
import { formatDate } from '../utils/format'
import { evidenceCategoryLabel, type EvidenceFile } from '../utils/evidence'
import ChoiceSheet from './ChoiceSheet.vue'
const props = defineProps<{ caseId: string; file: EvidenceFile }>()
const emit = defineEmits<{ close: []; changed: [] }>()
interface Grant {
  id: string
  userId: string
  displayName: string
  grantedBy: string
  expiresAt: string
  revokedAt: string | null
}
interface Access {
  grants: Grant[]
  recipients: { id: string; displayName: string }[]
}
const dialog = ref<HTMLDialogElement>(),
  data = ref<Access>({ grants: [], recipients: [] })
const userId = ref(''),
  hours = ref(reference.evidenceDefaultHours ? String(reference.evidenceDefaultHours) : ''),
  reason = ref(''),
  error = ref(''),
  busy = ref(false),
  loading = ref(true)
const options = computed(() =>
  data.value.recipients.map((user) => ({ value: user.id, label: user.displayName }))
)
const durations = computed(() =>
  Object.entries(reference.evidenceDurations ?? {}).map(([value, label]) => ({ value, label }))
)
const base = '/api/v1/disputes/' + props.caseId + '/evidence/' + props.file.id + '/access'
const request = new AbortController()
let disposed = false
const active = (grant: Grant) => !grant.revokedAt && Date.parse(grant.expiresAt) > Date.now()
async function load() {
  data.value = await api.get<Access>(base, request.signal)
}
async function change(grant?: Grant) {
  if (busy.value || reason.value.trim().length < (reference.evidenceReason?.minimum ?? Infinity))
    return
  busy.value = true
  error.value = ''
  try {
    await api.post(
      grant ? base + '/' + grant.id + '/revoke' : base,
      grant
        ? { reason: reason.value }
        : { userId: userId.value, hours: Number(hours.value), reason: reason.value }
    )
    if (disposed) return
    reason.value = ''
    await load()
    emit('changed')
  } catch (e) {
    if (!disposed)
      error.value = e instanceof ApiError ? e.message : 'Access could not be updated. Please retry.'
  } finally {
    if (!disposed) busy.value = false
  }
}
onMounted(async () => {
  dialog.value?.showModal()
  try {
    await load()
  } catch (e) {
    if (!disposed)
      error.value = e instanceof ApiError ? e.message : 'Access details are unavailable.'
  } finally {
    if (!disposed) loading.value = false
  }
})
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
      aria-label="Document Access"
      @close="emit('close')"
    >
      <div class="w3-bar">
        <header class="evidence-modal-header w3-bar w3-padding-16">
          <button
            class="icon-button w3-button w3-round-large w3-right"
            aria-label="Close"
            @click="dialog?.close()"
          >
            <X :size="22" />
          </button>
          <div>
            <h2 class="w3-sans-serif w3-large">Document Access</h2>
            <p>{{ evidenceCategoryLabel(file.category) }}</p>
          </div>
        </header>
        <p class="evidence-hint w3-small w3-text-grey w3-block">
          <LockKeyhole
            class="w3-margin-right"
            :size="16"
          />
          Access applies to this document only. Another supervisor must approve your own access.
        </p>
        <p
          v-if="error"
          class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
          role="alert"
        >
          {{ error }}
        </p>
        <p
          v-if="loading"
          role="status"
        >
          Loading…
        </p>
        <form
          v-else
          class="evidence-access-form w3-bar"
          @submit.prevent="change()"
        >
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Staff Member
            <ChoiceSheet
              v-model="userId"
              label="Choose Staff"
              :options="options"
              :disabled="busy"
            />
          </label>
          <p
            v-if="!options.length"
            class="evidence-hint w3-small w3-text-grey w3-block"
          >
            Assign a qualified specialist to this case or ask another supervisor to review access.
          </p>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Access Expires In
            <ChoiceSheet
              v-model="hours"
              label="Access Duration"
              :options="durations"
              :disabled="busy"
            />
          </label>
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            Reason
            <textarea
              v-model="reason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              :minlength="reference.evidenceReason?.minimum"
              :maxlength="reference.evidenceReason?.maximum ?? undefined"
              rows="2"
              required
              :disabled="busy"
              placeholder="Why is access needed or being revoked?"
            />
          </label>
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="
              busy ||
              !userId ||
              reason.trim().length < (reference.evidenceReason?.minimum ?? Infinity)
            "
          >
            {{ busy ? 'Saving…' : 'Grant Access' }}
          </button>
        </form>
        <ul
          v-if="data.grants.length"
          class="evidence-grants w3-ul w3-section"
        >
          <li
            v-for="grant in data.grants"
            :key="grant.id"
          >
            <div>
              <strong>{{ grant.displayName }}</strong>
              <small class="w3-small">
                {{ grant.revokedAt ? 'Revoked' : active(grant) ? 'Expires' : 'Expired' }} ·
                {{ formatDate(grant.revokedAt ?? grant.expiresAt) }}
              </small>
              <small class="w3-small">Approved by {{ grant.grantedBy }}</small>
            </div>
            <button
              v-if="active(grant)"
              class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
              :disabled="
                busy || reason.trim().length < (reference.evidenceReason?.minimum ?? Infinity)
              "
              @click="change(grant)"
            >
              Revoke
            </button>
          </li>
        </ul>
        <p
          v-else-if="!loading"
          class="evidence-hint w3-small w3-text-grey w3-block"
        >
          No staff access granted.
        </p>
      </div>
    </dialog>
  </Teleport>
</template>
