<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { statusReferences } from '../portalReference'
import ActionActor from './ActionActor.vue'
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { api } from '../api'
import { formatDate } from '../utils/format'
import type { CaseActivity } from '../financial'
const props = defineProps<{ caseId: string; version?: number }>(),
  items = ref<CaseActivity[]>([]),
  error = ref('')
const activityLabels: Record<string, string> = {
  CaseTargetsChanged: 'Target Dates Updated',
  CaseAssigned: 'Specialist Assigned',
  CaseTriaged: 'Review Details Updated',
  CaseEscalated: 'Senior Review Requested',
  CustomerUpdate: 'Case Update',
  Reopened: 'Dispute Reopened',
  EvidenceSubmitted: 'Evidence Submitted',
  EvidenceRemoved: 'Evidence Removed',
  CustomerMessage: 'Customer Message',
  AgentReply: 'Specialist Reply',
  AutomaticDecision: 'Automatic Decision',
  DisputeRefundApproved: 'Refund Approved',
  DisputeRefundPosted: 'Refund Posted',
  DisputeRefundRecalled: 'Refund Recalled',
  DisputeReinstated: 'Dispute Reinstated',
  CaseUnassigned: 'Case Unassigned'
}
let controller: AbortController | undefined,
  generation = 0,
  disposed = false
async function load() {
  const n = ++generation
  controller?.abort()
  controller = new AbortController()
  try {
    const result = await api.get<CaseActivity[]>(
      '/api/v1/finance/disputes/' + props.caseId + '/activity',
      controller.signal
    )
    if (!disposed && n === generation) {
      items.value = result
      error.value = ''
    }
  } catch {
    if (!disposed && n === generation && !controller.signal.aborted)
      error.value = 'Activity is temporarily unavailable.'
  }
}
onMounted(load)
watch(() => [props.caseId, props.version], load)
onUnmounted(() => {
  disposed = true
  generation++
  controller?.abort()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <div class="case-activity w3-section">
    <h2 class="w3-sans-serif w3-large">Case History</h2>
    <p
      v-if="error"
      role="alert"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
    >
      {{ error }}
      <button
        class="text-button w3-button w3-text-black w3-round-large"
        @click="load"
      >
        Retry
      </button>
    </p>
    <ol class="staff-timeline w3-ul w3-section">
      <li
        v-for="item in items"
        :key="item.action + item.id"
        class="w3-padding-16 w3-leftbar w3-border-blue-grey"
      >
        <span class="staff-timeline-dot w3-hide" />
        <div>
          <b>
            {{
              statusReferences[item.action]?.label ||
              activityLabels[item.action] ||
              item.action.replace(/([a-z])([A-Z])/g, '$1 $2')
            }}
          </b>
          <time
            class="w3-block w3-small w3-text-grey w3-margin-top"
            :datetime="item.occurredAt"
          >
            {{ formatDate(item.occurredAt) }}
          </time>
          <p v-if="item.detail">
            {{ item.detail }}
          </p>
          <ActionActor
            :identity="item.actorIdentity"
            compact
            :fallback="item.actor"
          />
        </div>
      </li>
    </ol>
  </div>
</template>
