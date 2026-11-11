<script setup lang="ts">
import { ChevronDown } from '@lucide/vue'
import { computed, onMounted, onUnmounted, ref } from 'vue'
const flags = import.meta.glob<string>('/node_modules/country-flag-icons/3x2/*.svg', {
  eager: true,
  query: '?url',
  import: 'default'
})
const flagSource = (code: string) => flags['/node_modules/country-flag-icons/3x2/' + code + '.svg']
const props = defineProps<{
  modelValue: string
  countries: { code: string; name: string; dialCode?: string }[]
  id: string
  label: string
  dial?: boolean
  disabled?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const open = ref(false),
  query = ref(''),
  root = ref<HTMLElement | null>(null)
const selected = computed(() => props.countries.find((c) => c.code === props.modelValue))
const filtered = computed(() =>
  props.countries.filter((c) =>
    (c.name + ' ' + c.code + ' ' + (c.dialCode ?? ''))
      .toLocaleLowerCase()
      .includes(query.value.toLocaleLowerCase())
  )
)
function choose(code: string) {
  if (props.disabled) return
  emit('update:modelValue', code)
  open.value = false
  query.value = ''
}
function outside(e: PointerEvent) {
  if (!root.value?.contains(e.target as Node)) open.value = false
}
onMounted(() => document.addEventListener('pointerdown', outside))
onUnmounted(() => document.removeEventListener('pointerdown', outside))
</script>
<template>
  <div
    ref="root"
    class="country-picker w3-bar"
    @keydown.esc="open = false"
  >
    <label
      class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
      :for="id"
    >
      {{ label }}
    </label>
    <button
      :id="id"
      type="button"
      class="country-trigger w3-button w3-block w3-border w3-round-large w3-left-align w3-padding w3-large w3-white"
      aria-haspopup="listbox"
      :aria-expanded="open"
      :disabled="disabled"
      @click="open = !open"
    >
      <img
        v-if="selected"
        :src="flagSource(selected.code)"
        width="24"
        height="16"
        alt=""
        class="w3-margin-right"
      />
      <span>
        {{ selected?.name ?? 'Choose A Country' }}
        <span v-if="dial && selected">({{ selected.dialCode }})</span>
      </span>
      <ChevronDown
        :size="18"
        aria-hidden="true"
      />
    </button>
    <div
      v-if="open"
      class="country-menu w3-container w3-padding-16 w3-section w3-white w3-border w3-round-large"
    >
      <input
        v-model="query"
        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
        type="search"
        :aria-label="'Search ' + label.toLowerCase()"
        placeholder="Search countries"
        autocomplete="off"
      />
      <ul
        class="w3-ul w3-bar-block"
        role="listbox"
        :aria-label="label"
      >
        <li
          v-for="country in filtered"
          :key="country.code"
        >
          <button
            :class="{ 'w3-theme-l4 w3-text-black': (modelValue === country.code) === true }"
            class="w3-bar-item w3-button w3-round-large"
            type="button"
            role="option"
            :aria-selected="modelValue === country.code"
            @click="choose(country.code)"
          >
            <img
              :src="flagSource(country.code)"
              width="24"
              height="16"
              alt=""
              class="w3-margin-right"
            />
            {{ country.name }}
            <small
              v-if="dial"
              class="w3-small"
            >
              {{ country.dialCode }}
            </small>
          </button>
        </li>
      </ul>
      <p v-if="!filtered.length">No countries found.</p>
    </div>
  </div>
</template>
