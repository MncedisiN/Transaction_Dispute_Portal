<script setup lang="ts">
import { ShieldCheck } from '@lucide/vue'
</script>
<template>
  <div class="auth-page w3-white w3-sans-serif">
    <header class="w3-container w3-border-bottom w3-padding-16">
      <div class="w3-auto">
        <RouterLink
          class="w3-button w3-large w3-round-large"
          to="/sign-in"
          aria-label="Dispute Portal Sign In"
        >
          <span class="w3-tag w3-red w3-round-large w3-padding-small w3-margin-right">
            <ShieldCheck :size="22" />
          </span>
          <b>Dispute Portal</b>
        </RouterLink>
      </div>
    </header>
    <div
      class="w3-padding-16 w3-hide-small"
      aria-hidden="true"
    ></div>
    <main class="w3-auto w3-container w3-padding-32">
      <div class="w3-row">
        <div
          class="w3-col l4 m3 w3-hide-small"
          aria-hidden="true"
        >
          &nbsp;
        </div>
        <section
          class="auth-panel w3-col l4 m6 s12 w3-round-xlarge w3-container w3-padding-large w3-card"
        >
          <div class="auth-form-wrap w3-padding-16"><slot /></div>
        </section>
      </div>
    </main>
  </div>
</template>
