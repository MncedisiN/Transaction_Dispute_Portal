<script setup lang="ts">
import { ref } from 'vue'
import { Copy, Check } from '@lucide/vue'
import { copyText } from '../utils/clipboard'
const props = withDefaults(defineProps<{ reference: string; label?: string }>(), {
    label: 'Copy Dispute Reference'
  }),
  feedback = ref('')
async function copy() {
  try {
    await copyText(props.reference)
    feedback.value = 'Reference copied'
  } catch {
    feedback.value = 'Select the reference to copy it'
  }
}
</script>
<template>
  <span class="copy-reference w3-margin-left">
    <button
      type="button"
      class="reference-copy-button w3-button w3-round-large w3-light-grey w3-small w3-padding-small"
      :aria-label="label"
      @click="copy"
    >
      <Check
        v-if="feedback === 'Reference copied'"
        :size="16"
      />
      <Copy
        v-else
        :size="16"
      />
      <span class="w3-margin-left">{{ feedback === 'Reference copied' ? 'Copied' : 'Copy' }}</span>
    </button>
    <span
      class="sr-only w3-hide"
      role="status"
    >
      {{ feedback }}
    </span>
  </span>
</template>
