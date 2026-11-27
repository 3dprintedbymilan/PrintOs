<script setup lang="ts">
import { newMaterialValue, newSupplierValue } from '~/utils/inventory'
import type { Material, Supplier } from '~/utils/inventory'
import { emptyLine, purchaseTotals, todayIso, validatePurchaseForm } from '~/utils/purchases'
import type { LineField, PurchaseDetail, PurchaseErrorKind, PurchaseErrors, PurchaseField, PurchaseFormValues } from '~/utils/purchases'

definePageMeta({ titleKey: 'purchases.addTitle' })

const { t, n } = useI18n()
const auth = useAuthStore()

const form = reactive<PurchaseFormValues>({
  supplierId: '',
  supplierName: '',
  purchasedOn: todayIso(),
  invoiceReference: '',
  notes: '',
  shippingCost: '',
  discount: '',
  receiveNow: true,
  lines: [],
})
const materials = ref<Material[]>([])
const suppliers = ref<Supplier[]>([])
const errors = ref<PurchaseErrors>({})
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const saveError = ref('')
const currency = ref('EUR')
let nextKey = 1

const supplierOptions = computed(() => [
  ...suppliers.value.map(supplier => ({ value: supplier.id, label: supplier.name })),
  { value: newSupplierValue, label: t('inventory.newSupplier') },
])
const materialOptions = computed(() => [
  ...materials.value.map(material => ({ value: material.id, label: material.name })),
  { value: newMaterialValue, label: t('inventory.newMaterial') },
])
const totals = computed(() => purchaseTotals(form))

function money(amount: number) {
  return n(amount, { style: 'currency', currency: currency.value })
}

function addLine() {
  form.lines.push(emptyLine(nextKey++, materials.value[0]?.id ?? newMaterialValue))
}

function removeLine(key: number) {
  form.lines = form.lines.filter(line => line.key !== key)
}

async function load() {
  status.value = 'loading'
  try {
    const [loadedMaterials, loadedSuppliers, settings] = await Promise.all([
      auth.authorizedFetch<Material[]>('/api/materials'),
      auth.authorizedFetch<Supplier[]>('/api/suppliers'),
      auth.authorizedFetch<{ currency: string }>('/api/workspace/settings'),
    ])
    materials.value = loadedMaterials
    suppliers.value = loadedSuppliers
    currency.value = settings.currency
    form.supplierId = loadedSuppliers[0]?.id ?? newSupplierValue
    if (form.lines.length === 0) addLine()
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function fieldError(field: PurchaseField): string | undefined {
  const kind = errors.value[field]
  return kind ? errorText(kind, t(`purchases.fields.${field}`)) : undefined
}

function lineError(key: number, field: LineField): string | undefined {
  const kind = errors.value.lineErrors?.[key]?.[field]
  return kind ? errorText(kind, t(`purchases.fields.${field}`)) : undefined
}

function errorText(kind: PurchaseErrorKind, field: string) {
  return t(`purchases.errors.${kind}`, { field })
}

function focusInvalid() {
  nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"], [data-invalid="true"]')?.focus())
}

async function save() {
  saveError.value = ''
  const result = validatePurchaseForm(form)
  errors.value = result.errors
  if (!result.purchase) {
    focusInvalid()
    return
  }

  const { purchase } = result
  saving.value = true
  try {
    const supplierId = purchase.newSupplier
      ? (await auth.authorizedFetch<Supplier>('/api/suppliers', { method: 'POST', body: purchase.newSupplier })).id
      : purchase.supplierId
    const createdMaterials = new Map<number, string>()
    for (const material of purchase.newMaterials) {
      createdMaterials.set(material.key, (await auth.authorizedFetch<Material>('/api/materials', { method: 'POST', body: { name: material.name, type: material.type } })).id)
    }
    const created = await auth.authorizedFetch<PurchaseDetail>('/api/purchases', {
      method: 'POST',
      body: {
        supplierId,
        ...purchase.request,
        lines: purchase.request.lines.map(({ key, materialId, ...line }) => ({ ...line, materialId: materialId ?? createdMaterials.get(key) })),
      },
    })
    await navigateTo(`/inventory/purchases/${created.id}`)
  }
  catch (error) {
    const parsed = parseApiError(error)
    saveError.value = parsed.kind === 'network' ? t('purchases.errors.network') : t('purchases.errors.save')
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
      to="/inventory/purchases"
      class="back"
    >
      {{ $t('purchases.back') }}
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
          v-model="form.supplierId"
          :label="$t('purchases.fields.supplier')"
          :options="supplierOptions"
        />
        <UiInput
          v-if="form.supplierId === newSupplierValue"
          v-model="form.supplierName"
          :label="$t('inventory.fields.supplierName')"
          :error="fieldError('supplierName')"
        />
        <UiInput
          v-model="form.purchasedOn"
          type="date"
          :label="$t('purchases.fields.purchasedOn')"
          :error="fieldError('purchasedOn')"
        />
        <UiInput
          v-model="form.invoiceReference"
          :label="$t('purchases.fields.invoiceReference')"
          :hint="$t('purchases.hints.invoiceReference')"
          :error="fieldError('invoiceReference')"
        />
      </UiCard>

      <section
        v-for="(line, index) in form.lines"
        :key="line.key"
        :aria-label="$t('purchases.lineTitle', { number: index + 1 })"
      >
        <UiCard class="group">
          <div class="line-head">
            <h2>{{ $t('purchases.lineTitle', { number: index + 1 }) }}</h2>
            <UiButton
              v-if="form.lines.length > 1"
              variant="ghost"
              @click="removeLine(line.key)"
            >
              {{ $t('purchases.removeLine') }}
            </UiButton>
          </div>
          <UiSelect
            v-model="line.materialId"
            :label="$t('inventory.material')"
            :options="materialOptions"
          />
          <template v-if="line.materialId === newMaterialValue">
            <UiInput
              v-model="line.materialName"
              :label="$t('inventory.fields.materialName')"
              :hint="$t('inventory.hints.materialName')"
              :error="lineError(line.key, 'materialName')"
            />
            <UiInput
              v-model="line.materialType"
              :label="$t('inventory.fields.materialType')"
              :hint="$t('inventory.hints.materialType')"
              :error="lineError(line.key, 'materialType')"
            />
          </template>
          <UiInput
            v-model="line.colorName"
            :label="$t('purchases.fields.colorName')"
            :error="lineError(line.key, 'colorName')"
          />
          <UiInput
            v-model="line.colorHex"
            :label="$t('purchases.fields.colorHex')"
            :hint="$t('inventory.hints.colorHex')"
            :error="lineError(line.key, 'colorHex')"
          />
          <div class="pair">
            <UiInput
              v-model="line.quantity"
              :label="$t('purchases.fields.quantity')"
              :error="lineError(line.key, 'quantity')"
              inputmode="numeric"
            />
            <UiInput
              v-model="line.weight"
              :label="$t('purchases.fields.weight')"
              :error="lineError(line.key, 'weight')"
              inputmode="numeric"
            />
          </div>
          <UiInput
            v-model="line.unitPrice"
            :label="$t('purchases.fields.unitPrice')"
            :hint="$t('purchases.hints.unitPrice')"
            :error="lineError(line.key, 'unitPrice')"
            inputmode="decimal"
          />
        </UiCard>
      </section>
      <p
        v-if="errors.lines"
        class="error"
        role="alert"
      >
        {{ $t('purchases.errors.noLines') }}
      </p>
      <UiButton
        variant="secondary"
        block
        @click="addLine"
      >
        {{ $t('purchases.addLine') }}
      </UiButton>

      <UiCard class="group">
        <UiInput
          v-model="form.shippingCost"
          :label="$t('purchases.fields.shippingCost')"
          :hint="$t('purchases.hints.extras')"
          :error="fieldError('shippingCost')"
          inputmode="decimal"
        />
        <UiInput
          v-model="form.discount"
          :label="$t('purchases.fields.discount')"
          :error="fieldError('discount')"
          inputmode="decimal"
        />
        <UiInput
          v-model="form.notes"
          :label="$t('purchases.fields.notes')"
          :error="fieldError('notes')"
        />
        <dl class="totals">
          <div>
            <dt>{{ $t('purchases.subtotal') }}</dt>
            <dd>{{ money(totals.subtotal) }}</dd>
          </div>
          <div class="grand">
            <dt>{{ $t('purchases.total') }}</dt>
            <dd>{{ money(totals.total) }}</dd>
          </div>
        </dl>
        <label class="toggle">
          <input
            v-model="form.receiveNow"
            type="checkbox"
          >
          <span>{{ $t('purchases.receiveNow') }}</span>
        </label>
        <p class="hint">
          {{ form.receiveNow ? $t('purchases.receiveNowOn') : $t('purchases.receiveNowOff') }}
        </p>
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
        {{ $t('purchases.save') }}
      </UiButton>
    </form>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.line-head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); }
.line-head h2 { margin: 0; font-size: var(--font-size-md); }
.pair { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
.totals { margin: 0; display: grid; gap: var(--space-1); }
.totals div { display: flex; justify-content: space-between; gap: var(--space-3); color: var(--muted); }
.totals dd { margin: 0; font-variant-numeric: tabular-nums; }
.totals .grand { color: var(--text); font-weight: 700; }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
.hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
