<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue'
import { Eye, EyeOff } from '@lucide/vue'
defineOptions({ inheritAttrs: false })
const props = defineProps<{ modelValue: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>(),
  visible = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined
function toggle() {
  visible.value = !visible.value
  clearTimeout(timer)
  if (visible.value) timer = setTimeout(() => (visible.value = false), 30000)
}
watch(
  () => props.modelValue,
  (value) => {
    if (!value) visible.value = false
  }
)
watch(
  () => visible.value,
  (value) => {
    if (!value) clearTimeout(timer)
  }
)
onUnmounted(() => clearTimeout(timer))
</script>
<template>
  <span class="password-control w3-row w3-block w3-border w3-round-large">
    <input
      class="w3-input w3-col s10 w3-border-0 w3-round-xlarge w3-large"
      v-bind="$attrs"
      :value="modelValue"
      :type="visible ? 'text' : 'password'"
      @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
    />
    <button
      type="button"
      class="password-eye w3-col s2 w3-button w3-round-large w3-large w3-padding"
      :aria-label="visible ? 'Hide password' : 'Show password'"
      :aria-pressed="visible"
      @click.prevent="toggle"
    >
      <EyeOff
        v-if="visible"
        :size="19"
      />
      <Eye
        v-else
        :size="19"
      />
    </button>
  </span>
</template>
