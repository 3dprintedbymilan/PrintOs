<script setup lang="ts">
import type { CustomerField, CustomerFormValues } from '~/utils/customers'
import { customerLimits } from '~/utils/customers'

defineProps<{
  errors: Partial<Record<CustomerField, string>>
  compact?: boolean
}>()
const form = defineModel<CustomerFormValues>({ required: true })
</script>

<template>
  <div class="fields">
    <UiInput
      v-model="form.name"
      :label="$t('customers.fields.name')"
      :error="errors.name"
      autocomplete="off"
    />
    <UiInput
      v-model="form.email"
      :label="$t('customers.fields.email')"
      type="email"
      inputmode="email"
      :error="errors.email"
      autocomplete="off"
    />
    <UiInput
      v-model="form.phone"
      :label="$t('customers.fields.phone')"
      type="tel"
      :error="errors.phone"
      autocomplete="off"
    />
    <template v-if="!compact">
      <UiTextarea
        v-model="form.address"
        :label="$t('customers.fields.address')"
        :hint="$t('customers.hints.address')"
        :error="errors.address"
        :maxlength="customerLimits.address"
      />
      <UiInput
        v-model="form.vatNumber"
        :label="$t('customers.fields.vatNumber')"
        :hint="$t('customers.hints.vatNumber')"
        :error="errors.vatNumber"
        autocomplete="off"
      />
      <UiTextarea
        v-model="form.notes"
        :label="$t('customers.fields.notes')"
        :error="errors.notes"
        :maxlength="customerLimits.notes"
      />
    </template>
  </div>
</template>

<style scoped>
.fields { display: grid; gap: var(--space-3); }
</style>
