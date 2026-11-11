<script setup lang="ts">
import { ref, watch, useId } from 'vue'
import { ChevronDown, ChevronUp } from '@lucide/vue'

const props = defineProps<{ open?: boolean; inverted?: boolean }>()
const expanded = ref(props.open ?? false)
const contentId = useId()
const container = ref<HTMLElement | null>(null)
watch(
  () => props.open,
  (value) => {
    if (value !== undefined) expanded.value = value
  }
)
defineExpose({
  get open() {
    return expanded.value
  },
  set open(value: boolean) {
    expanded.value = value
  },
  scrollIntoView(options?: ScrollIntoViewOptions) {
    container.value?.scrollIntoView(options)
  }
})
</script>
<template>
  <section
    ref="container"
    class="disclosure-panel w3-bar w3-section"
  >
    <button
      type="button"
      class="w3-button w3-block w3-left-align w3-padding-16 w3-round-large"
      :class="inverted ? 'w3-text-white' : 'w3-theme-l5 w3-text-blue-grey'"
      :aria-expanded="expanded"
      :aria-controls="contentId"
      @click="expanded = !expanded"
    >
      <ChevronUp
        v-if="expanded"
        :size="17"
        class="w3-right"
      />
      <ChevronDown
        v-else
        :size="17"
        class="w3-right"
      />
      <slot name="heading" />
    </button>
    <div
      v-if="expanded"
      :id="contentId"
      class="w3-padding-16"
    >
      <slot />
    </div>
  </section>
</template>
