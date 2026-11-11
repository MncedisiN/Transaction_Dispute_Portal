<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api } from '../api'
import HomeView from './HomeView.vue'
import DashboardView from './DashboardView.vue'
const enabled = ref<boolean | null>(null)
onMounted(async () => {
  try {
    enabled.value = (await api.get<{ enabled: boolean }>('/api/v1/platform/status')).enabled
  } catch {
    enabled.value = false
  }
})
</script>
<template>
  <HomeView v-if="enabled" />
  <DashboardView v-else-if="enabled === false" />
  <main
    v-else
    class="home-loading"
    role="status"
  >
    Opening your workspace…
  </main>
</template>
