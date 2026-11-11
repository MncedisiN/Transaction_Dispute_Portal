<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft } from '@lucide/vue'
import { previousDestination, destinationLabel } from '../utils/backNavigation'
const props = withDefaults(defineProps<{ fallback: string; label?: string }>(), { label: 'Back' })
const router = useRouter(),
  route = useRoute()
const previous = computed(() => {
  // Reading fullPath also refreshes the destination when a reused view changes routes.
  return previousDestination(router.options.history.state.back, route.fullPath, props.fallback)
})
const text = computed(() => (previous.value ? destinationLabel(previous.value) : props.label))
function back() {
  if (previous.value) router.back()
  else void router.replace(props.fallback)
}
</script>
<template>
  <button
    type="button"
    class="back-link w3-button w3-round-large w3-text-blue-grey w3-small w3-padding-small w3-margin-bottom"
    @click="back"
  >
    <ArrowLeft
      :size="16"
      class="w3-margin-right"
    />
    <span>{{ text }}</span>
  </button>
</template>
