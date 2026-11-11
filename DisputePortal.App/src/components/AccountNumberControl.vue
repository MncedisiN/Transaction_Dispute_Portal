<script setup lang="ts">
import { computed, ref } from 'vue'
import { Copy } from '@lucide/vue'
import type { Account } from '../types'
import { copyText } from '../utils/clipboard'
const props = defineProps<{ account: Account }>()
const number = computed(() => props.account.accountNumber?.trim() || '')
const message = ref('')
async function copy(): Promise<void> {
  if (!number.value) return
  const text =
    props.account.accountType + ' account\n' + number.value + '\n' + props.account.currency
  try {
    await copyText(text)
    message.value = 'Account details copied.'
  } catch {
    message.value = 'Select the account number to copy it.'
  }
}
</script>
<template>
  <div class="account-number-control">
    <span class="account-type-label w3-block w3-small w3-text-grey">
      {{ account.accountType }} account
    </span>
    <div class="account-number-line w3-bar w3-section">
      <strong>{{ number || 'Account number unavailable' }}</strong>
      <button
        class="icon-button w3-button w3-round-large"
        type="button"
        aria-label="Copy Account Details"
        :disabled="!number"
        @click="copy"
      >
        <Copy :size="18" />
      </button>
    </div>
    <p
      v-if="message"
      role="status"
    >
      {{ message }}
    </p>
  </div>
</template>
