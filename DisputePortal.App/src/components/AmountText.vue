<script setup lang="ts">
import { computed } from 'vue'
import { formatMoney } from '../utils/format'
const props = defineProps<{ amount: number; currency: string; direction?: string }>()
const value = computed(
  () => (props.direction === 'Credit' ? '+' : '−') + formatMoney(props.amount, props.currency)
)
</script>

<template>
  <span
    class="amount"
    :class="direction === 'Credit' ? 'amount-credit' : ''"
  >
    {{ value }}
  </span>
</template>
