<script setup lang="ts">
import { formatDate } from '../utils/format'
import { computed } from 'vue'
import type { CaseOperations } from '../operations'
import { useNow } from '../composables/useNow'
const props = defineProps<{ item: CaseOperations; compact?: boolean }>()
const now = useNow()
const clock = computed(() =>
  props.item.slaClosedAt ? Date.parse(props.item.slaClosedAt) : now.value
)
function remaining(target: string | null, completed?: string | null) {
  if (!target) return 'Scheduling review'
  const ms = Date.parse(target) - (completed ? Date.parse(completed) : clock.value)
  const total = Math.ceil(Math.abs(ms) / 60000)
  const hours = Math.floor(total / 60)
  const minutes = total % 60
  return (
    (completed
      ? 'Completed ' + (ms < 0 ? 'late' : 'within target')
      : ms < 0
        ? 'Overdue by'
        : 'Due in') + (completed ? '' : ' ' + hours + 'h ' + minutes + 'm')
  )
}
const late = computed(() => Boolean(props.item.dueAt && clock.value > Date.parse(props.item.dueAt)))
</script>
<template>
  <div
    class="case-timers w3-small w3-panel w3-leftbar w3-border-blue-grey w3-padding w3-light-grey w3-round-large"
    :class="{ 'timer-overdue w3-pale-red w3-border-red': late }"
  >
    <strong class="w3-block">{{ remaining(item.dueAt, item.slaClosedAt) }}</strong>
    <template v-if="!compact">
      <span class="w3-block w3-margin-top">
        Review target: {{ item.dueAt ? formatDate(item.dueAt) : 'Being scheduled' }}
      </span>
      <span class="w3-block w3-margin-top">
        First response: {{ remaining(item.responseDueAt, item.firstResponseAt) }}
      </span>
      <small class="w3-small w3-block w3-margin-top">
        Elapsed {{ Math.max(0, Math.floor((clock - Date.parse(item.createdAt)) / 3600000)) }}h ·
        Clock includes time awaiting customer information.
      </small>
    </template>
  </div>
</template>
