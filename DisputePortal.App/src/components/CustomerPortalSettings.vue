<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Search } from '@lucide/vue'
import { api } from '../api'
import { loadPortalReference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

interface SettingsVersion {
  version: number
  settings: Record<string, unknown> & {
    customerSearchEnabled: boolean
    customerAccountOpeningEnabled: boolean
  }
}
const current = ref<SettingsVersion | null>(null)
const hideSearch = ref(false)
const accountOpening = ref(false)
const loading = ref(true)
const saving = ref(false)
const reason = ref('')
const error = ref('')
const notice = ref('')
async function load() {
  loading.value = true
  error.value = ''
  try {
    current.value = await api.get<SettingsVersion>('/api/v1/admin/portal-settings')
    hideSearch.value = !current.value.settings.customerSearchEnabled
    accountOpening.value = current.value.settings.customerAccountOpeningEnabled
  } catch (cause) {
    error.value = (cause as Error).message
  } finally {
    loading.value = false
  }
}
async function save() {
  if (!current.value || saving.value) return
  saving.value = true
  error.value = ''
  try {
    current.value = await api.post<SettingsVersion>('/api/v1/admin/portal-settings', {
      expectedVersion: current.value.version,
      settings: {
        ...current.value.settings,
        customerSearchEnabled: !hideSearch.value,
        customerAccountOpeningEnabled: accountOpening.value
      },
      reason: reason.value.trim()
    })
    await loadPortalReference(true)
    reason.value = ''
    notice.value = 'Customer portal settings saved.'
  } catch (cause) {
    error.value = (cause as Error).message
  } finally {
    saving.value = false
  }
}
onMounted(load)
useFeedback(error, undefined, notice)
</script>

<template>
  <section
    class="surface-panel customer-search-settings w3-white w3-section"
    aria-label="Customer Portal Settings"
  >
    <h2 class="w3-large">
      <Search
        :size="19"
        class="w3-margin-right"
      />
      Customer Controls
    </h2>
    <p class="w3-text-grey">
      Manage customer account opening and search controls. Staff filters remain available.
    </p>
    <p
      v-if="loading"
      role="status"
    >
      Loading Settings…
    </p>
    <form
      v-else-if="current"
      @submit.prevent="save"
    >
      <label class="settings-switch-row">
        <span>Hide Customer Search And Filters</span>
        <input
          v-model="hideSearch"
          type="checkbox"
          role="switch"
          :disabled="saving"
        />
      </label>
      <label class="settings-switch-row">
        <span>Allow Customers To Open Accounts</span>
        <input
          v-model="accountOpening"
          type="checkbox"
          role="switch"
          :disabled="saving"
        />
      </label>
      <label class="w3-block w3-margin-top">
        Reason For Change
        <textarea
          v-model.trim="reason"
          class="w3-input"
          rows="2"
          required
          minlength="5"
          maxlength="500"
          :disabled="saving"
        />
      </label>
      <button
        class="button button-primary w3-theme-d5 w3-margin-top"
        :disabled="
          saving ||
          (!hideSearch === current.settings.customerSearchEnabled &&
            accountOpening === current.settings.customerAccountOpeningEnabled)
        "
      >
        {{ saving ? 'Saving…' : 'Save Customer Settings' }}
      </button>
    </form>
    <button
      v-else
      class="button button-secondary"
      @click="load"
    >
      Try Again
    </button>
  </section>
</template>
