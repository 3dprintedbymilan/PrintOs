<script setup lang="ts">
import { emptyProductForm, validateProductForm } from '~/utils/products'
import type { ProductField, ProductFormValues, ProductRequest } from '~/utils/products'

const props = defineProps<{
  initial?: ProductFormValues
  saving?: boolean
  submitLabel: string
  error?: string
  serverErrors?: Partial<Record<ProductField, string>>
}>()
const emit = defineEmits<{ submit: [request: ProductRequest] }>()

const { t } = useI18n()
const form = reactive<ProductFormValues>({ ...emptyProductForm(), ...props.initial })
const fieldErrors = ref<Partial<Record<ProductField, string>>>({})
const shownErrors = computed(() => ({ ...props.serverErrors, ...fieldErrors.value }))

function submit() {
  const { request, errors } = validateProductForm(form)
  fieldErrors.value = Object.fromEntries(
    Object.entries(errors).map(([field, kind]) => [field, t(`products.errors.${kind}`, { field: t(`products.fields.${field}`) })]),
  )
  if (!request) {
    nextTick(() => document.querySelector<HTMLElement>('form [aria-invalid="true"]')?.focus())
    return
  }
  emit('submit', request)
}
</script>

<template>
  <form
    class="form"
    novalidate
    @submit.prevent="submit"
  >
    <UiInput
      v-model="form.name"
      :label="$t('products.fields.name')"
      :error="shownErrors.name"
      autocomplete="off"
    />
    <UiInput
      v-model="form.sku"
      :label="$t('products.fields.sku')"
      :hint="$t('products.hints.sku')"
      :error="shownErrors.sku"
      autocomplete="off"
    />
    <div class="field">
      <label for="product-description">{{ $t('products.fields.description') }}</label>
      <textarea
        id="product-description"
        v-model="form.description"
        rows="3"
        :aria-invalid="shownErrors.description ? 'true' : undefined"
      />
      <p
        v-if="shownErrors.description"
        class="error"
      >
        {{ shownErrors.description }}
      </p>
    </div>
    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <UiButton
      type="submit"
      block
      :loading="saving"
    >
      {{ submitLabel }}
    </UiButton>
  </form>
</template>

<style scoped>
.form { display: grid; gap: var(--space-3); }
.field { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
textarea { width: 100%; padding: var(--space-3) var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); font: inherit; resize: vertical; }
textarea[aria-invalid='true'] { border-color: var(--danger); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
