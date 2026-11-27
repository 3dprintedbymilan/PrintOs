<script setup lang="ts">
import { licenseStatusTone } from '~/utils/licenses'
import type { LicenseStatusName } from '~/utils/licenses'

defineProps<{ status: LicenseStatusName }>()
</script>

<template>
  <UiStatusChip :status="licenseStatusTone(status)">
    {{ $t(`licenses.status.${status}`) }}
  </UiStatusChip>
</template>
