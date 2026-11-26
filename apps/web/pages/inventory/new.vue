<script setup lang="ts">
import { newMaterialValue, newSupplierValue, validateSpoolForm } from '~/utils/inventory'
import type { Material, Spool, SpoolErrorKind, SpoolField, SpoolFormValues, Supplier } from '~/utils/inventory'

definePageMeta({ titleKey: 'inventory.addSpool' })

const { t } = useI18n()
const auth = useAuthStore()

const form = reactive<SpoolFormValues>({
  materialId: '',
  materialName: '',
  materialType: '',
  supplierId: '',
  supplierName: '',
  colorName: '',
  colorHex: '',
  cost: '',
  weight: '1000',
})
const materials = ref<Material[]>([])
const suppliers = ref<Supplier[]>([])
const fieldErrors = ref<Partial<Record<SpoolField, string>>>({})
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const saveError = ref('')

const materialOptions = computed(() => [
  ...materials.value.map(material => ({ value: material.id, label: material.name })),
  { value: newMaterialValue, label: t('inventory.newMaterial') },
])
const supplierOptions = computed(() => [
  { value: '', label: t('inventory.noSupplier') },
  ...suppliers.value.map(supplier => ({ value: supplier.id, label: supplier.name })),
  { value: newSupplierValue, label: t('inventory.newSupplier') },
])

async function load() {
  status.value = 'loading'
  try {
    const [loadedMaterials, loadedSuppliers] = await Promise.all([
      auth.authorizedFetch<Material[]>('/api/materials'),
      auth.authorizedFetch<Supplier[]>('/api/suppliers'),
    ])
    materials.value = loadedMaterials
    suppliers.value = loadedSuppliers
    form.materialId = loadedMaterials[0]?.id ?? newMaterialValue
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function errorText(field: SpoolField, kind: SpoolErrorKind) {
  return t(`inventory.errors.${kind}`, { field: t(`inventory.fields.${field}`) })
}

async function save() {
  saveError.value = ''
  const { spool, errors } = validateSpoolForm(form)
  fieldErrors.value = Object.fromEntries(Object.entries(errors).map(([field, kind]) => [field, errorText(field as SpoolField, kind)]))
  if (!spool) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }

  saving.value = true
  try {
    let materialId = spool.materialId
    if (spool.newMaterial) {
      materialId = (await auth.authorizedFetch<Material>('/api/materials', { method: 'POST', body: spool.newMaterial })).id
    }
    let supplierId = spool.supplierId
    if (spool.newSupplier) {
      supplierId = (await auth.authorizedFetch<Supplier>('/api/suppliers', { method: 'POST', body: spool.newSupplier })).id
    }
    const created = await auth.authorizedFetch<Spool>('/api/spools', {
      method: 'POST',
      body: {
        materialId,
        supplierId,
        colorName: spool.colorName,
        colorHex: spool.colorHex,
        cost: spool.cost,
        initialWeightGrams: spool.initialWeightGrams,
      },
    })
    await navigateTo(`/inventory/${created.id}`)
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation') {
      const first = Object.values(parsed.fieldErrors)[0]?.[0]
      saveError.value = first ?? t('inventory.errors.save')
    }
    else {
      saveError.value = parsed.kind === 'network' ? t('inventory.errors.network') : t('inventory.errors.save')
    }
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/inventory"
      class="back"
    >
      {{ $t('inventory.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <form
      v-else
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <UiCard class="group">
        <UiSelect
          v-model="form.materialId"
          :label="$t('inventory.material')"
          :options="materialOptions"
        />
        <template v-if="form.materialId === newMaterialValue">
          <UiInput
            v-model="form.materialName"
            :label="$t('inventory.fields.materialName')"
            :hint="$t('inventory.hints.materialName')"
            :error="fieldErrors.materialName"
          />
          <UiInput
            v-model="form.materialType"
            :label="$t('inventory.fields.materialType')"
            :hint="$t('inventory.hints.materialType')"
            :error="fieldErrors.materialType"
          />
        </template>

        <UiSelect
          v-model="form.supplierId"
          :label="$t('inventory.supplier')"
          :options="supplierOptions"
        />
        <UiInput
          v-if="form.supplierId === newSupplierValue"
          v-model="form.supplierName"
          :label="$t('inventory.fields.supplierName')"
          :error="fieldErrors.supplierName"
        />
      </UiCard>

      <UiCard class="group">
        <UiInput
          v-model="form.colorName"
          :label="$t('inventory.fields.colorName')"
          :hint="$t('inventory.hints.colorName')"
          :error="fieldErrors.colorName"
        />
        <UiInput
          v-model="form.colorHex"
          :label="$t('inventory.fields.colorHex')"
          :hint="$t('inventory.hints.colorHex')"
          :error="fieldErrors.colorHex"
        />
        <UiInput
          v-model="form.weight"
          :label="$t('inventory.fields.weight')"
          :error="fieldErrors.weight"
          inputmode="numeric"
        />
        <UiInput
          v-model="form.cost"
          :label="$t('inventory.fields.cost')"
          :error="fieldErrors.cost"
          inputmode="decimal"
        />
      </UiCard>

      <p
        v-if="saveError"
        class="error"
        role="alert"
      >
        {{ saveError }}
      </p>

      <UiButton
        type="submit"
        block
        :loading="saving"
      >
        {{ $t('inventory.saveSpool') }}
      </UiButton>
    </form>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
