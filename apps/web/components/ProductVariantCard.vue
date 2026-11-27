<script setup lang="ts">
import { emptyBomLineForm, lineToForm, marginTone, variantToForm } from '~/utils/products'
import type { BomLine, BomLineFormValues, BomLineRequest, Variant, VariantCostSnapshot, VariantField, VariantRequest } from '~/utils/products'
import type { Material, Spool } from '~/utils/inventory'
import type { Printer } from '~/utils/printerForm'
import type { ModelSummary } from '~/utils/modelLibrary'

const props = defineProps<{
  productId: string
  variant: Variant
  currency: string
  printers: Printer[]
  spools: Spool[]
  materials: Material[]
  models: ModelSummary[]
}>()
const emit = defineEmits<{ updated: [variant: Variant], removed: [id: string] }>()

const { t, n, d } = useI18n()
const auth = useAuthStore()

const base = computed(() => `/api/products/${props.productId}/variants/${props.variant.id}`)
const cost = computed(() => props.variant.cost)
const tone = computed(() => marginTone(cost.value.marginPercent))
const hasPrintLine = computed(() => props.variant.bomLines.some(line => line.kind === 'print'))
const priceDiffers = computed(() => cost.value.suggestedPrice !== props.variant.price)

const variantDialog = ref(false)
const variantSaving = ref(false)
const variantError = ref('')
const variantServerErrors = ref<Partial<Record<VariantField, string>>>({})

const lineDialog = ref(false)
const editingLine = ref<BomLine | null>(null)
const lineForm = ref<BomLineFormValues>(emptyBomLineForm())
const lineSaving = ref(false)
const lineError = ref('')

const actionError = ref('')
const applying = ref(false)
const snapshotSaving = ref(false)
const snapshotMessage = ref('')
const snapshots = ref<VariantCostSnapshot[] | null>(null)
const snapshotsFailed = ref(false)

function money(amount: number, currency = props.currency, precise = false) {
  return n(amount, { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: precise ? 4 : 2 })
}

function percent(value: number | null) {
  return value === null ? '–' : `${n(value, { maximumFractionDigits: 1 })}%`
}

function errorText(error: unknown, fallbackKey: string) {
  return parseApiError(error).kind === 'network' ? t('products.errors.network') : t(fallbackKey)
}

function lineTitle(line: BomLine) {
  if (line.kind === 'filament') return line.spoolLabel ?? line.materialName ?? t('products.kinds.filament')
  if (line.kind === 'print') return line.printerName ?? t('products.kinds.print')
  if (line.kind === 'labour') return t('products.kinds.labour')
  return line.label || t(`products.kinds.${line.kind}`)
}

function lineDetail(line: BomLine) {
  if (line.kind === 'filament') return `${n(line.grams ?? 0, { maximumFractionDigits: 2 })} g`
  if (line.kind === 'print' || line.kind === 'labour') return t('products.minutes', { count: line.minutes ?? 0 })
  return money(line.amount ?? 0)
}

function costLabel(kind: string, label: string) {
  return kind === 'extra' ? label : t(`calculator.lines.${kind}`)
}

function openVariantEdit() {
  variantError.value = ''
  variantServerErrors.value = {}
  variantDialog.value = true
}

async function saveVariant(request: VariantRequest) {
  variantSaving.value = true
  variantError.value = ''
  variantServerErrors.value = {}
  try {
    const updated = await auth.authorizedFetch<Variant>(base.value, { method: 'PUT', body: request })
    emit('updated', updated)
    variantDialog.value = false
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && parsed.fieldErrors.sku) variantServerErrors.value = { sku: t('products.errors.skuTaken') }
    else variantError.value = errorText(error, 'products.errors.save')
  }
  finally {
    variantSaving.value = false
  }
}

async function removeVariant() {
  variantSaving.value = true
  try {
    await auth.authorizedFetch(base.value, { method: 'DELETE' })
    variantDialog.value = false
    emit('removed', props.variant.id)
  }
  catch (error) {
    variantError.value = errorText(error, 'products.errors.save')
  }
  finally {
    variantSaving.value = false
  }
}

function openLine(line: BomLine | null) {
  editingLine.value = line
  lineForm.value = line ? lineToForm(line) : emptyBomLineForm('filament')
  lineError.value = ''
  lineDialog.value = true
}

async function saveLine(request: BomLineRequest) {
  lineSaving.value = true
  lineError.value = ''
  const line = editingLine.value
  try {
    const updated = await auth.authorizedFetch<Variant>(line ? `${base.value}/bom-lines/${line.id}` : `${base.value}/bom-lines`, {
      method: line ? 'PUT' : 'POST',
      body: request,
    })
    emit('updated', updated)
    lineDialog.value = false
  }
  catch (error) {
    const parsed = parseApiError(error)
    lineError.value = parsed.kind === 'validation' && parsed.fieldErrors.kind ? t('products.errors.onePrintLine') : errorText(error, 'products.errors.save')
  }
  finally {
    lineSaving.value = false
  }
}

async function removeLine(line: BomLine) {
  actionError.value = ''
  try {
    emit('updated', await auth.authorizedFetch<Variant>(`${base.value}/bom-lines/${line.id}`, { method: 'DELETE' }))
  }
  catch (error) {
    actionError.value = errorText(error, 'products.errors.save')
  }
}

async function applySuggestedPrice() {
  applying.value = true
  actionError.value = ''
  const request: VariantRequest = {
    name: props.variant.name,
    sku: props.variant.sku,
    price: cost.value.suggestedPrice,
    targetMarginPercent: props.variant.targetMarginPercent,
    feePercent: props.variant.feePercent,
  }
  try {
    emit('updated', await auth.authorizedFetch<Variant>(base.value, { method: 'PUT', body: request }))
  }
  catch (error) {
    actionError.value = errorText(error, 'products.errors.save')
  }
  finally {
    applying.value = false
  }
}

async function loadSnapshots() {
  snapshotsFailed.value = false
  try {
    snapshots.value = await auth.authorizedFetch<VariantCostSnapshot[]>(`${base.value}/cost-snapshots`)
  }
  catch {
    snapshotsFailed.value = true
  }
}

function onHistoryToggle(event: Event) {
  if ((event.target as HTMLDetailsElement).open && snapshots.value === null) loadSnapshots()
}

async function saveSnapshot() {
  snapshotSaving.value = true
  snapshotMessage.value = ''
  actionError.value = ''
  try {
    const snapshot = await auth.authorizedFetch<VariantCostSnapshot>(`${base.value}/cost-snapshots`, { method: 'POST' })
    if (snapshots.value) snapshots.value = [snapshot, ...snapshots.value]
    snapshotMessage.value = t('products.snapshotSaved')
  }
  catch (error) {
    actionError.value = errorText(error, 'products.errors.snapshot')
  }
  finally {
    snapshotSaving.value = false
  }
}
</script>

<template>
  <UiCard
    class="variant"
    :aria-label="variant.name"
  >
    <header class="head">
      <div class="title">
        <h3>{{ variant.name }}</h3>
        <p v-if="variant.sku">
          {{ variant.sku }}
        </p>
      </div>
      <UiStatusChip :status="tone">
        {{ $t('products.margin') }} {{ percent(cost.marginPercent) }}
      </UiStatusChip>
    </header>

    <dl class="metrics">
      <div>
        <dt>{{ $t('products.price') }}</dt>
        <dd>{{ money(variant.price) }}</dd>
      </div>
      <div>
        <dt>{{ $t('products.cost') }}</dt>
        <dd>{{ money(cost.totalCost) }}</dd>
      </div>
      <div>
        <dt>{{ $t('products.profit') }}</dt>
        <dd :class="{ negative: cost.profit < 0 }">
          {{ money(cost.profit) }}
        </dd>
      </div>
      <div>
        <dt>{{ $t('products.suggested', { percent: variant.targetMarginPercent }) }}</dt>
        <dd>{{ money(cost.suggestedPrice) }}</dd>
      </div>
    </dl>

    <p
      v-if="cost.feePercent > 0"
      class="note"
    >
      {{ $t('products.feeNote', { percent: cost.feePercent, amount: money(cost.feeAmount) }) }}
    </p>
    <p
      v-if="cost.unpricedMaterials.length > 0"
      class="warning"
      role="note"
    >
      {{ $t('products.unpriced', { materials: cost.unpricedMaterials.join(', ') }) }}
    </p>

    <div class="actions">
      <UiButton
        variant="secondary"
        @click="openVariantEdit"
      >
        {{ $t('products.editVariant') }}
      </UiButton>
      <UiButton
        v-if="priceDiffers && variant.bomLines.length > 0"
        variant="secondary"
        :loading="applying"
        @click="applySuggestedPrice"
      >
        {{ $t('products.applySuggested') }}
      </UiButton>
    </div>

    <section
      class="bom"
      :aria-label="$t('products.bom')"
    >
      <h4>{{ $t('products.bom') }}</h4>
      <p
        v-if="variant.bomLines.length === 0"
        class="note"
      >
        {{ $t('products.bomEmpty') }}
      </p>
      <ul
        v-else
        class="lines"
      >
        <li
          v-for="line in variant.bomLines"
          :key="line.id"
          class="line"
        >
          <span class="line-text">
            <span class="kind">{{ $t(`products.kinds.${line.kind}`) }}</span>
            <span class="line-title">{{ lineTitle(line) }} · {{ lineDetail(line) }}</span>
            <NuxtLink
              v-if="line.modelId && line.modelName"
              :to="`/models/${line.modelId}`"
              class="model-link"
            >
              {{ $t('products.model', { name: line.modelName }) }}
            </NuxtLink>
          </span>
          <span class="line-actions">
            <button
              type="button"
              class="icon-btn"
              :aria-label="$t('products.editLineAria', { name: lineTitle(line) })"
              @click="openLine(line)"
            >
              <svg
                viewBox="0 0 24 24"
                width="20"
                height="20"
                fill="none"
                stroke="currentColor"
                stroke-width="1.8"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              ><path d="M4 20h4L19 9l-4-4L4 16v4z" /></svg>
            </button>
            <button
              type="button"
              class="icon-btn danger"
              :aria-label="$t('products.removeLineAria', { name: lineTitle(line) })"
              @click="removeLine(line)"
            >
              <svg
                viewBox="0 0 24 24"
                width="20"
                height="20"
                fill="none"
                stroke="currentColor"
                stroke-width="1.8"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              ><path d="M5 7h14M10 11v6M14 11v6M6 7l1 12h10l1-12M9 7V4h6v3" /></svg>
            </button>
          </span>
        </li>
      </ul>
      <UiButton
        variant="secondary"
        block
        @click="openLine(null)"
      >
        {{ $t('products.addLine') }}
      </UiButton>
    </section>

    <details
      v-if="variant.bomLines.length > 0"
      class="more"
    >
      <summary>{{ $t('products.breakdown') }}</summary>
      <ul class="breakdown">
        <li
          v-for="(line, index) in cost.lines"
          :key="`${line.kind}-${index}`"
        >
          <span>{{ costLabel(line.kind, line.label) }}</span>
          <span class="amount">{{ money(line.amount) }}</span>
        </li>
      </ul>
    </details>

    <div class="snapshot">
      <UiButton
        variant="secondary"
        block
        :loading="snapshotSaving"
        :disabled="variant.bomLines.length === 0"
        @click="saveSnapshot"
      >
        {{ $t('products.saveSnapshot') }}
      </UiButton>
      <p
        class="status"
        role="status"
      >
        {{ snapshotMessage }}
      </p>
    </div>

    <details
      class="more"
      @toggle="onHistoryToggle"
    >
      <summary>{{ $t('products.history') }}</summary>
      <p
        v-if="snapshotsFailed"
        class="warning"
      >
        {{ $t('state.error') }}
        <button
          type="button"
          class="link-btn"
          @click="loadSnapshots"
        >
          {{ $t('state.retry') }}
        </button>
      </p>
      <p
        v-else-if="snapshots === null"
        class="note"
      >
        {{ $t('state.loading') }}
      </p>
      <p
        v-else-if="snapshots.length === 0"
        class="note"
      >
        {{ $t('products.historyEmpty') }}
      </p>
      <ul
        v-else
        class="history"
      >
        <li
          v-for="snapshot in snapshots"
          :key="snapshot.id"
        >
          <span class="when">{{ d(new Date(snapshot.createdAt), { dateStyle: 'medium', timeStyle: 'short' }) }}</span>
          <span class="history-values">
            {{ $t('products.historyLine', {
              cost: money(snapshot.totalCost, snapshot.currency),
              price: money(snapshot.price, snapshot.currency),
              margin: percent(snapshot.marginPercent),
            }) }}
          </span>
        </li>
      </ul>
    </details>

    <p
      v-if="actionError"
      class="warning"
      role="alert"
    >
      {{ actionError }}
    </p>

    <VariantDialog
      v-model="variantDialog"
      :title="$t('products.editVariantTitle')"
      :initial="variantToForm(variant)"
      :saving="variantSaving"
      :error="variantError"
      :server-errors="variantServerErrors"
      can-delete
      @submit="saveVariant"
      @remove="removeVariant"
    />
    <BomLineDialog
      v-model="lineDialog"
      :initial="lineForm"
      :editing="editingLine !== null"
      :printers="printers"
      :spools="spools"
      :materials="materials"
      :models="models"
      :has-print-line="hasPrintLine"
      :saving="lineSaving"
      :error="lineError"
      @submit="saveLine"
    />
  </UiCard>
</template>

<style scoped>
.variant { display: grid; gap: var(--space-3); }
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); }
.title { min-width: 0; }
h3 { margin: 0; font-size: var(--font-size-md); overflow-wrap: anywhere; }
h4 { margin: 0; font-size: var(--font-size-sm); color: var(--muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em; }
.title p { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.metrics { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); margin: 0; }
.metrics div { display: grid; gap: 2px; padding: var(--space-3); background: var(--surface-2); border-radius: var(--radius-md); }
dt { color: var(--muted); font-size: var(--font-size-sm); }
dd { margin: 0; font-size: var(--font-size-lg); font-weight: 700; font-variant-numeric: tabular-nums; }
dd.negative { color: var(--danger); }
.note { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.warning { margin: 0; color: var(--warning); font-size: var(--font-size-sm); }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.bom { display: grid; gap: var(--space-2); }
.lines { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.line { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); padding: var(--space-2) var(--space-3); background: var(--surface-2); border-radius: var(--radius-md); }
.line-text { display: grid; min-width: 0; }
.kind { color: var(--muted); font-size: var(--font-size-sm); }
.line-title { font-weight: 600; overflow-wrap: anywhere; }
.model-link { display: inline-flex; align-items: center; min-height: 32px; color: var(--accent-text); font-size: var(--font-size-sm); }
.line-actions { display: flex; flex-shrink: 0; }
.icon-btn { display: grid; place-items: center; width: var(--tap-size); height: var(--tap-size); color: var(--muted); background: transparent; border: 0; border-radius: var(--radius-pill); cursor: pointer; -webkit-tap-highlight-color: transparent; }
@media (hover: hover) {
  .icon-btn:hover { color: var(--text); }
  .icon-btn.danger:hover { color: var(--danger); }
}
.more summary { display: flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; cursor: pointer; }
.breakdown, .history { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.breakdown li { display: flex; justify-content: space-between; gap: var(--space-3); }
.amount { font-variant-numeric: tabular-nums; font-weight: 600; }
.history li { display: grid; padding: var(--space-2) 0; border-bottom: 1px solid var(--border); }
.when { font-weight: 600; }
.history-values { color: var(--muted); font-size: var(--font-size-sm); }
.snapshot { display: grid; gap: var(--space-1); }
.status { margin: 0; min-height: 1.25em; color: var(--accent-text); font-size: var(--font-size-sm); }
.link-btn { min-height: var(--tap-size); padding: 0 var(--space-2); color: var(--accent-text); background: transparent; border: 0; font: inherit; font-weight: 600; cursor: pointer; }
</style>
