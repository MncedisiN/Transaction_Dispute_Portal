<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, useId } from 'vue'
import { X } from '@lucide/vue'

const props = defineProps<{ title: string; busy?: boolean }>()
const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement>()
const titleId = useId()
let disposed = false

function requestClose() {
  if (!disposed && !props.busy) emit('close')
}

onMounted(() => dialog.value?.showModal())
onBeforeUnmount(() => {
  disposed = true
  dialog.value?.close()
})
</script>

<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="portal-dialog w3-container w3-white w3-round-xlarge"
      :aria-labelledby="titleId"
      :aria-busy="busy"
      @cancel.prevent="requestClose"
      @close="requestClose"
    >
      <header class="portal-dialog-heading">
        <h2 :id="titleId">{{ title }}</h2>
        <button
          class="icon-button w3-button w3-round-large"
          type="button"
          :aria-label="'Close ' + title"
          :disabled="busy"
          @click="requestClose"
        >
          <X :size="20" />
        </button>
      </header>
      <slot />
    </dialog>
  </Teleport>
</template>
