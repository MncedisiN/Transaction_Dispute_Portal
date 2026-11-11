<script setup lang="ts">
defineProps<{ eyebrow?: string; title: string; description?: string }>()
</script>
<template>
  <header class="page-heading w3-row w3-margin-bottom w3-padding-16">
    <div class="w3-col s12 m8">
      <p
        v-if="eyebrow"
        class="eyebrow w3-small w3-wide w3-text-grey"
      >
        {{ eyebrow }}
      </p>
      <h1 class="w3-sans-serif w3-xlarge w3-text-black">
        <b>
          <span class="w3-hide-medium w3-hide-large">{{ title }}</span>
          <span class="w3-hide-small w3-xxlarge">{{ title }}</span>
        </b>
      </h1>
      <p
        v-if="description"
        class="page-description w3-text-grey"
      >
        {{ description }}
      </p>
    </div>
    <div
      v-if="$slots.default"
      class="page-heading-actions w3-col s12 m4 w3-section w3-right-align w3-bar"
    >
      <slot />
    </div>
  </header>
</template>
