<script setup lang="ts">
import { computed } from 'vue'
import type { ActionActor } from '../types'
import { userRoles } from '../utils/userAccess'
const props = defineProps<{
  identity?: ActionActor | null
  fallback?: string | null
  compact?: boolean
}>()
const name = computed(
  () =>
    props.identity?.fullName ||
    props.identity?.accountName ||
    props.identity?.label ||
    props.fallback ||
    'Actor not recorded'
)
const role = computed(() =>
  (props.identity?.roles ?? [])
    .map((key) => userRoles[key]?.label ?? key.replaceAll('_', ' '))
    .join(' · ')
)
</script>
<template>
  <div
    class="action-actor w3-small w3-text-grey w3-section"
    :class="{ 'actor-compact': compact }"
  >
    <span class="actor-name w3-block">{{ name }}</span>
    <small
      v-if="role"
      class="w3-small"
    >
      {{ role }}
    </small>
  </div>
</template>
