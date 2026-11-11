<script setup lang="ts">
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
import CaseStatusCatalogue from '../components/CaseStatusCatalogue.vue'
import PolicyVersionAdministration from '../components/PolicyVersionAdministration.vue'
</script>
<template>
  <AppShell>
    <PageHeading
      title="Policy"
      description="Review Effective Settings And Publish New Versions."
    />
    <PolicyVersionAdministration kind="routing" />
    <PolicyVersionAdministration kind="automatic" />
    <CaseStatusCatalogue />
  </AppShell>
</template>
