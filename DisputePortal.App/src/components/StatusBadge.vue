<script setup lang="ts">
import { computed } from 'vue'
import { statusReferences } from '../portalReference'
const props = defineProps<{ status: string; staff?: boolean }>()
const tone = computed(() => statusReferences[props.status]?.tone ?? 'neutral')
const label = computed(() => {
  const value = statusReferences[props.status]
  return value ? (props.staff ? value.label : value.customerLabel) : props.status
})
</script>

<template>
  <span
    class="status-badge w3-tag w3-round-xxlarge w3-padding-small w3-small"
    :class="
      (
        {
          positive: 'w3-pale-green',
          success: 'w3-pale-green',
          danger: 'w3-pale-red',
          negative: 'w3-pale-red',
          warning: 'w3-pale-yellow',
          neutral: 'w3-light-grey',
          active: 'w3-theme-l4 w3-text-black'
        } as Record<string, string>
      )[tone] ?? 'w3-light-grey'
    "
  >
    <span class="status-dot" />
    {{ label }}
  </span>
</template>
