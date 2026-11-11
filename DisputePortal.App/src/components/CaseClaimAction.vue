<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { computed, ref } from 'vue'
import { Check, LockKeyhole, UserRoundPlus } from '@lucide/vue'
import { api, ApiError } from '../api'
import type { CaseOperations, StaffUser } from '../operations'
import { caseClaimEligibility } from '../utils/caseClaim'
const props = defineProps<{ item: CaseOperations; staff?: StaffUser }>()
const emit = defineEmits<{ claimed: []; refresh: [] }>()
const saving = ref(false),
  error = ref('')
const eligibility = computed(() => caseClaimEligibility(props.item))
async function claim() {
  if (saving.value || !eligibility.value.allowed) return
  saving.value = true
  error.value = ''
  try {
    await api.post('/api/v1/operations/cases/' + props.item.id + '/claim', {
      expectedVersion: props.item.version,
      reason: 'Self-assigned to review and respond to this case.'
    })
    emit('claimed')
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : 'Please refresh and try again.'
    if (cause instanceof ApiError && [403, 404, 409].includes(cause.status)) emit('refresh')
  } finally {
    saving.value = false
  }
}
useFeedback(error, undefined, undefined)
</script>
<template>
  <div class="case-claim-action">
    <button
      v-if="eligibility.state === 'ready' || eligibility.state === 'locked'"
      class="button button-secondary claim-button w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      :disabled="saving || !eligibility.allowed"
      @click="claim"
    >
      <UserRoundPlus
        v-if="eligibility.allowed"
        :size="16"
      />
      <LockKeyhole
        v-else
        class="w3-margin-right"
        :size="16"
      />
      {{ saving ? 'Assigning…' : eligibility.label }}
    </button>
    <p
      v-else
      class="claim-status w3-small w3-text-grey"
    >
      <Check
        v-if="eligibility.state === 'owned'"
        :size="15"
      />
      <LockKeyhole
        v-else
        class="w3-margin-right"
        :size="15"
      />
      {{ eligibility.label }}
    </p>
    <p
      v-if="eligibility.reason"
      class="claim-reason w3-panel w3-light-grey w3-padding w3-round-large"
    >
      {{ eligibility.reason }}
    </p>
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
  </div>
</template>
