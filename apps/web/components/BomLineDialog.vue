<script setup lang="ts">
import { bomKinds, emptyBomLineForm, validateBomLineForm } from '~/utils/products'
import type { BomKind, BomLineField, BomLineFormValues, BomLineRequest } from '~/utils/products'
import type { Material, Spool } from '~/utils/inventory'
import type { Printer } from '~/utils/printerForm'
import type { ModelSummary } from '~/utils/modelLibrary'

const props = defineProps<{
  initial?: BomLineFormValues
  editing?: boolean
  printers: Printer[]
  spools: Spool[]
  materials: Material[]
  models: ModelSummary[]
  hasPrintLine?: boolean
  saving?: boolean
  error?: string
}>()
const emit = defineEmits<{ submit: [request: BomLineRequest] }>()
const open = defineModel<boolean>({ required: true })

const { t } = useI18n()
const formId = useId()
const form = reactive<BomLineFormValues>(emptyBomLineForm())
const fieldErrors = ref<Partial<Record<BomLineField, string>>>({})

const availableKinds = computed(() => bomKinds.filter(kind => props.editing || kind !== 'print' || !props.hasPrintLine))
const kindOptions = computed(() => availableKinds.value.map(kind => ({ value: kind, label: t(`products.kinds.${kind}`) })))
const sourceOptions = computed(() => [
  { value: 'spool', label: t('products.sources.spool') },
  { value: 'material', label: t('products.sources.material') },
])
const spoolOptions = computed(() => [
  { value: '', label: t('products.choose') },
  ...props.spools.filter(spool => !spool.isArchived || spool.id === form.spoolId).map(spool => ({ value: spool.id, label: `${spool.materialName} · ${spool.colorName}` })),
])
const materialOptions = computed(() => [{ value: '', label: t('products.choose') }, ...props.materials.map(material => ({ value: material.id, label: material.name }))])
const printerOptions = computed(() => [{ value: '', label: t('products.choose') }, ...props.printers.map(printer => ({ value: printer.id, label: printer.name }))])
const modelOptions = computed(() => [{ value: '', label: t('products.noModel') }, ...props.models.map(model => ({ value: model.id, label: model.name }))])

watch(open, (isOpen) => {
  if (!isOpen) return
  Object.assign(form, props.initial ?? emptyBomLineForm(availableKinds.value[0]))
  fieldErrors.value = {}
})

function selectKind(kind: string) {
  form.kind = kind as BomKind
  fieldErrors.value = {}
}

function submit() {
  const { request, errors } = validateBomLineForm(form)
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
    :title="editing ? $t('products.editLine', { kind: $t(`products.kinds.${form.kind}`) }) : $t('products.addLine')"
  >
    <form
      :id="formId"
      class="form"
      novalidate
      @submit.prevent="submit"
    >
      <UiSelect
        v-if="!editing"
        :model-value="form.kind"
        :label="$t('products.fields.kind')"
        :options="kindOptions"
        @update:model-value="selectKind"
      />

      <template v-if="form.kind === 'filament'">
        <UiSegmentedControl
          v-model="form.source"
          :label="$t('products.fields.source')"
          :options="sourceOptions"
        />
        <UiSelect
          v-if="form.source === 'spool'"
          v-model="form.spoolId"
          :label="$t('products.fields.spoolId')"
          :options="spoolOptions"
          :error="fieldErrors.spoolId"
        />
        <UiSelect
          v-else
          v-model="form.materialId"
          :label="$t('products.fields.materialId')"
          :options="materialOptions"
          :error="fieldErrors.materialId"
        />
        <UiInput
          v-model="form.grams"
          :label="$t('products.fields.grams')"
          :hint="$t('products.hints.grams')"
          :error="fieldErrors.grams"
          inputmode="decimal"
        />
      </template>

      <template v-else-if="form.kind === 'print'">
        <UiSelect
          v-model="form.printerId"
          :label="$t('products.fields.printerId')"
          :options="printerOptions"
          :error="fieldErrors.printerId"
        />
        <UiInput
          v-model="form.minutes"
          :label="$t('products.fields.printMinutes')"
          :error="fieldErrors.minutes"
          inputmode="numeric"
        />
        <UiSelect
          v-model="form.modelId"
          :label="$t('products.fields.modelId')"
          :options="modelOptions"
        />
      </template>

      <UiInput
        v-else-if="form.kind === 'labour'"
        v-model="form.minutes"
        :label="$t('products.fields.labourMinutes')"
        :error="fieldErrors.minutes"
        inputmode="numeric"
      />

      <template v-else>
        <UiInput
          v-model="form.label"
          :label="$t('products.fields.label')"
          :hint="form.kind === 'packaging' ? $t('products.hints.packagingLabel') : undefined"
          :error="fieldErrors.label"
          autocomplete="off"
        />
        <UiInput
          v-model="form.amount"
          :label="$t('products.fields.amount')"
          :error="fieldErrors.amount"
          inputmode="decimal"
        />
      </template>

      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>
    </form>
    <template #actions>
      <UiButton
        variant="secondary"
        @click="open = false"
      >
        {{ $t('products.cancel') }}
      </UiButton>
      <UiButton
        type="submit"
        :form="formId"
        :loading="saving"
      >
        {{ $t('products.saveLine') }}
      </UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.form { display: grid; gap: var(--space-3); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
