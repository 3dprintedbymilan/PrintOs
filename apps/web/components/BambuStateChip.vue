<script setup lang="ts">
import type { BambuPrinterState } from '~/utils/bambu'
import { stateTone } from '~/utils/bambu'

defineProps<{ state: BambuPrinterState }>()
</script>

<template>
  <UiStatusChip :status="stateTone(state)">
    {{ $t(`bambu.state.${state}`) }}
  </UiStatusChip>
</template>
