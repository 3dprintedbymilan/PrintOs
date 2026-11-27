<script setup lang="ts">
import { emptyVariantForm, validateVariantForm } from '~/utils/products'
import type { VariantField, VariantFormValues, VariantRequest } from '~/utils/products'

const props = defineProps<{
  title: string
  initial?: VariantFormValues
  saving?: boolean
  error?: string
  serverErrors?: Partial<Record<VariantField, string>>
  canDelete?: boolean
}>()
const emit = defineEmits<{ submit: [request: VariantRequest], remove: [] }>()
const open = defineModel<boolean>({ required: true })

const { t } = useI18n()
const formId = useId()
const form = reactive<VariantFormValues>(emptyVariantForm())
const fieldErrors = ref<Partial<Record<VariantField, string>>>({})
const confirmingRemove = ref(false)
const shownErrors = computed(() => ({ ...props.serverErrors, ...fieldErrors.value }))

watch(open, (isOpen) => {
  if (!isOpen) return
  Object.assign(form, props.initial ?? emptyVariantForm())
  fieldErrors.value = {}
  confirmingRemove.value = false
})

function submit() {
  const { request, errors } = validateVariantForm(form)
  fieldErrors.value = Object.fromEntries(
    Object.entries(errors).map(([field, kind]) => [field, t(`products.errors.${kind}`, { field: t(`products.fields.${field}`) })]),
  )
  if (!request) {
    nextTick(() => document.querySelector<HTMLElement>(`[id="${formId}"] [aria-invalid="true"]`)?.focus())
    return
  }
  emit('submit', request)
}
</script>

<template>
  <UiDialog
    v-model="open"
    :title="title"
  >
    <form
      :id="formId"
      class="form"
      novalidate
      @submit.prevent="submit"
    >
      <UiInput
        v-model="form.name"
        :label="$t('products.fields.variantName')"
        :hint="$t('products.hints.variantName')"
        :error="shownErrors.name"
        autocomplete="off"
      />
      <UiInput
        v-model="form.sku"
        :label="$t('products.fields.sku')"
        :error="shownErrors.sku"
        autocomplete="off"
      />
      <UiInput
        v-model="form.price"
        :label="$t('products.fields.price')"
        :error="shownErrors.price"
        inputmode="decimal"
      />
      <div class="pair">
        <UiInput
          v-model="form.targetMargin"
          :label="$t('products.fields.targetMargin')"
          :error="shownErrors.targetMargin"
          inputmode="decimal"
        />
        <UiInput
          v-model="form.fee"
          :label="$t('products.fields.fee')"
          :error="shownErrors.fee"
          inputmode="decimal"
        />
      </div>
      <p class="note">
        {{ $t('products.hints.pricing') }}
      </p>
      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>
    </form>
    <template #actions>
      <template v-if="confirmingRemove">
        <UiButton
          variant="secondary"
          @click="confirmingRemove = false"
        >
          {{ $t('products.cancel') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="saving"
          @click="emit('remove')"
        >
          {{ $t('products.confirmRemoveVariant') }}
        </UiButton>
      </template>
      <template v-else>
        <UiButton
          v-if="canDelete"
          variant="ghost"
          @click="confirmingRemove = true"
        >
          {{ $t('products.removeVariant') }}
        </UiButton>
        <UiButton
          type="submit"
          :form="formId"
          :loading="saving"
        >
          {{ $t('products.saveVariant') }}
        </UiButton>
      </template>
    </template>
  </UiDialog>
</template>

<style scoped>
.form { display: grid; gap: var(--space-3); }
.pair { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-2); }
.note { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
