<script setup lang="ts">
import { computed, onBeforeUnmount, ref, useId } from 'vue'
import { Check, ChevronDown, X } from '@lucide/vue'
const props = defineProps<{
  modelValue: string
  label: string
  options: { value: string; label: string; hint?: string; disabled?: boolean }[]
  disabled?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const dialog = ref<HTMLDialogElement>()
const trigger = ref<HTMLButtonElement>()
const titleId = useId()
const search = ref('')
const filtered = computed(() =>
  props.options.filter((option) =>
    option.label.toLowerCase().includes(search.value.trim().toLowerCase())
  )
)
function open() {
  search.value = ''
  dialog.value?.showModal()
}
const selected = computed(
  () => props.options.find((option) => option.value === props.modelValue)?.label ?? props.label
)
function close() {
  dialog.value?.close()
  trigger.value?.focus()
}
function choose(value: string) {
  if (props.options.find((option) => option.value === value)?.disabled) return
  emit('update:modelValue', value)
  close()
}
onBeforeUnmount(() => dialog.value?.close())
</script>
<template>
  <button
    ref="trigger"
    type="button"
    class="choice-trigger w3-button w3-block w3-border w3-left-align w3-round-large w3-large w3-padding w3-white"
    :disabled="disabled"
    :aria-label="label + ': ' + selected"
    aria-haspopup="dialog"
    @click="open"
  >
    <span>{{ selected }}</span>
    <ChevronDown
      class="w3-right"
      :size="18"
    />
  </button>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="choice-dialog w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
      :aria-labelledby="titleId"
      @click="
        (event) => {
          if (event.target === dialog) close()
        }
      "
    >
      <div class="choice-heading w3-bar">
        <header>
          <button
            type="button"
            class="choice-close icon-button w3-button w3-round-large w3-right"
            aria-label="Close"
            @click="close"
          >
            <X :size="22" />
          </button>
          <h2
            :id="titleId"
            class="w3-sans-serif w3-large"
          >
            {{ label }}
          </h2>
        </header>
        <input
          v-if="options.length > 8"
          v-model="search"
          type="search"
          class="choice-search w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          placeholder="Search choices"
          :aria-label="'Search ' + label"
        />
        <p
          v-if="!filtered.length"
          role="status"
        >
          {{ options.length ? 'No Matching Options' : 'No Options Available' }}
        </p>
        <div
          class="choice-options w3-bar-block w3-bar"
          role="group"
          :aria-label="label"
        >
          <button
            v-for="option in filtered"
            :key="option.value"
            type="button"
            class="choice-option w3-bar-item w3-button w3-round-large w3-padding-16"
            :class="{ 'selected w3-pale-blue': modelValue === option.value }"
            :aria-pressed="modelValue === option.value"
            :disabled="option.disabled"
            @click="choose(option.value)"
          >
            <span class="choice-option-copy w3-block w3-left-align">
              <span>{{ option.label }}</span>
              <small
                v-if="option.hint"
                class="w3-small w3-block w3-text-grey"
              >
                {{ option.hint }}
              </small>
            </span>
            <Check
              v-if="modelValue === option.value"
              :size="20"
            />
          </button>
        </div>
      </div>
    </dialog>
  </Teleport>
</template>
