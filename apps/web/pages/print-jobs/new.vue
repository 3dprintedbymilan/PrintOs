<script setup lang="ts">
import type { PrintJob } from '~/utils/printJobs'

definePageMeta({ titleKey: 'printJobs.newTitle' })

async function saved(job: PrintJob) {
  await navigateTo(`/print-jobs/${job.id}`)
}
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/prints"
      class="back"
    >
      {{ $t('printJobs.back') }}
    </NuxtLink>
    <PrintJobForm
      mode="create"
      @saved="saved"
      @cancel="navigateTo('/prints')"
    />
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
</style>
