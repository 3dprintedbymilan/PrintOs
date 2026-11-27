<script setup lang="ts">
import type { MarketFormValues } from '~/utils/markets'

defineProps<{ errors: Record<string, string>, currency: string }>()
const form = defineModel<MarketFormValues>({ required: true })
</script>

<template>
  <div class="fields">
    <UiInput
      v-model="form.name"
      :label="$t('markets.form.name')"
      :hint="$t('markets.form.nameHint')"
      :error="errors.name"
      autocomplete="off"
    />
    <UiInput
      v-model="form.location"
      :label="$t('markets.form.location')"
      :error="errors.location"
      autocomplete="off"
    />
    <div class="dates">
      <UiInput
        v-model="form.startDate"
        type="date"
        :label="$t('markets.form.startDate')"
        :error="errors.startDate"
      />
      <UiInput
        v-model="form.endDate"
        type="date"
        :label="$t('markets.form.endDate')"
        :error="errors.endDate"
      />
    </div>
    <UiInput
      v-model="form.standCost"
      inputmode="decimal"
      :label="$t('markets.form.standCost', { currency })"
      :hint="$t('markets.form.standCostHint')"
      :error="errors.standCost"
    />
    <UiTextarea
      v-model="form.notes"
      :label="$t('markets.form.notes')"
      :error="errors.notes"
      :rows="3"
      :maxlength="2000"
    />
  </div>
</template>

<style scoped>
.fields { display: grid; gap: var(--space-3); }
.dates { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
</style>
