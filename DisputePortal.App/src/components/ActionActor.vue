<script setup lang="ts">
import { computed } from 'vue'
import { useAuthStore } from '../stores/auth'
import { customerActor } from '../utils/customerPresentation'
const auth = useAuthStore()
import type { ActionActor } from '../types'
import { userRoles } from '../utils/userAccess'
const props = defineProps<{
  identity?: ActionActor | null
  fallback?: string | null
  compact?: boolean
  action?: string
}>()
const name = computed(() =>
  !auth.isStaffWorkspace
    ? customerActor(
        props.identity,
        props.fallback,
        auth.user?.id,
        props.action,
        auth.user?.displayName
      )
    : props.identity?.fullName ||
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
      v-if="role && auth.isStaffWorkspace"
      class="w3-small"
    >
      {{ role }}
    </small>
  </div>
</template>
