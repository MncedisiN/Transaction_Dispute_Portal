<script setup lang="ts">
import { computed } from 'vue'
import { statusReferences } from '../portalReference'
import { Check } from '@lucide/vue'
import { caseStage, isClosedStatus } from '../utils/casePresentation'
const props = defineProps<{ status: string }>()
const stage = computed(() => caseStage(props.status))
const steps = computed(() => statusReferences[props.status]?.steps ?? [])
</script>
<template>
  <ol
    class="case-progress w3-ul w3-section"
    aria-label="Case Progress"
  >
    <li
      v-for="(step, index) in steps"
      :key="index"
      class="w3-center w3-border-0 w3-padding-small"
      :aria-current="index === stage ? 'step' : undefined"
    >
      <span
        class="w3-badge w3-padding-small w3-margin-bottom"
        :class="
          index === stage
            ? 'w3-theme-d5'
            : index < stage
              ? 'w3-theme-l4'
              : 'w3-light-grey w3-text-grey'
        "
      >
        <Check
          v-if="index < stage || (index === steps.length - 1 && isClosedStatus(status))"
          :size="14"
        />
        <template v-else>{{ index + 1 }}</template>
      </span>
      <b
        class="w3-block w3-small"
        :class="index === stage ? 'w3-text-blue-grey' : 'w3-text-grey'"
      >
        {{ step }}
      </b>
    </li>
  </ol>
</template>
