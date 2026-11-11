<script setup lang="ts">
import { ArrowUpRight } from '@lucide/vue'
import { useFeedback } from '../composables/useFeedback'

import { onMounted, watch, ref } from 'vue'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
defineProps<{ compact?: boolean }>()
const accounts = useAccountsStore()
const auth = useAuthStore()
const error = ref('')
async function load() {
  try {
    if (auth.user?.roles.includes('customer')) await accounts.load(auth.user.id)
    else accounts.reset()
  } catch {
    error.value = 'Accounts could not be loaded. Refresh to try again.'
  }
}
onMounted(load)
watch(() => auth.user?.id, load)
useFeedback(error, undefined, undefined)
</script>
<template>
  <div
    class="account-switcher"
    :class="{ 'switcher-compact': compact }"
  >
    <label
      class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
      for="active-account"
      :class="{ 'sr-only': compact }"
    >
      Account
    </label>
    <select
      id="active-account"
      v-model="accounts.selectedId"
      class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
    >
      <option value="">All my accounts</option>
      <option
        v-for="account in accounts.items"
        :key="account.id"
        :value="account.id"
      >
        {{ account.accountType }} · {{ account.accountNumber || 'Number unavailable' }}
      </option>
    </select>
    <RouterLink
      v-if="accounts.selectedId && !compact"
      :to="'/accounts/' + accounts.selectedId"
    >
      View Details
      <ArrowUpRight :size="15" />
    </RouterLink>
    <small
      v-if="error"
      class="w3-small"
      role="alert"
    >
      {{ error }}
    </small>
  </div>
</template>
