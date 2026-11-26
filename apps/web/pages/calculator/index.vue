<script setup lang="ts">
import { buildCostRequest, defaultSnapshotName, emptyCostForm } from '~/utils/costCalculator'
import type { CostBreakdown, CostField, CostFormValues, CostSnapshot } from '~/utils/costCalculator'
import type { Printer } from '~/utils/printerForm'
import type { Spool } from '~/utils/inventory'

definePageMeta({ titleKey: 'calculator.title' })

const { t, locale } = useI18n()
const auth = useAuthStore()

const form = reactive<CostFormValues>(emptyCostForm())
const initial = emptyCostForm()
const printers = ref<Printer[]>([])
const spools = ref<Spool[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const breakdown = ref<CostBreakdown | null>(null)
const calculating = ref(false)
const calcError = ref('')
const submitted = ref(false)
const dirty = reactive(new Set<string>())
const snapshotName = ref('')
const saving = ref(false)
const saveError = ref('')
const savedSnapshot = ref<CostSnapshot | null>(null)

const printerOptions = computed(() => printers.value.map(printer => ({ value: printer.id, label: printer.name })))
const spoolOptions = computed(() => spools.value.map(spool => ({
  value: spool.id,
  label: `${spool.materialName} · ${spool.colorName} (${spool.remainingWeightGrams} g)`,
})))
const validation = computed(() => buildCostRequest(form))
const suggestedName = computed(() => {
  const printer = printers.value.find(candidate => candidate.id === form.printerId)
  const request = validation.value.request
  return printer && request ? defaultSnapshotName(printer.name, request.grams) : ''
})
const canCalculate = computed(() => printers.value.length > 0 && spools.value.length > 0)

function visible(field: CostField, ...dirtyKeys: string[]) {
  const kind = validation.value.errors[field]
  if (!kind) return undefined
  const show = submitted.value || dirtyKeys.some(key => dirty.has(key))
  return show ? t(`calculator.errors.${kind}`, { field: t(`calculator.fields.${fieldName(field)}`) }) : undefined
}

function fieldName(field: CostField) {
  return field.startsWith('extraLabel') ? 'extraLabel' : field.startsWith('extraAmount') ? 'extraAmount' : field
}

const trackedFields = ['grams', 'printHours', 'printMinutes', 'labourMinutes', 'packagingCost', 'marginPercent', 'feePercent'] as const

let timer: ReturnType<typeof setTimeout> | undefined
let requestCounter = 0

async function calculate() {
  const { request } = validation.value
  if (!request) {
    breakdown.value = null
    calcError.value = ''
    return
  }
  const current = ++requestCounter
  calculating.value = true
  try {
    const result = await auth.authorizedFetch<CostBreakdown>('/api/costing/calculate', { method: 'POST', body: request })
    if (current !== requestCounter) return
    breakdown.value = result
    calcError.value = ''
  }
  catch (error) {
    if (current !== requestCounter) return
    breakdown.value = null
    calcError.value = parseApiError(error).kind === 'network' ? t('calculator.errors.network') : t('calculator.errors.calculate')
  }
  finally {
    if (current === requestCounter) calculating.value = false
  }
}

watch(form, () => {
  for (const field of trackedFields) {
    if (form[field] !== initial[field]) dirty.add(field)
  }
  savedSnapshot.value = null
  clearTimeout(timer)
  timer = setTimeout(calculate, 250)
}, { deep: true })

onBeforeUnmount(() => clearTimeout(timer))

async function load() {
  status.value = 'loading'
  try {
    const [loadedPrinters, loadedSpools] = await Promise.all([
      auth.authorizedFetch<Printer[]>('/api/printers'),
      auth.authorizedFetch<Spool[]>('/api/spools'),
    ])
    printers.value = loadedPrinters
    spools.value = loadedSpools
    form.printerId = loadedPrinters[0]?.id ?? ''
    form.spoolId = loadedSpools[0]?.id ?? ''
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function addExtra() {
  form.extras.push({ label: '', amount: '' })
}

function removeExtra(index: number) {
  form.extras.splice(index, 1)
}

async function save() {
  submitted.value = true
  saveError.value = ''
  const { request } = validation.value
  if (!request) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }
  saving.value = true
  try {
    savedSnapshot.value = await auth.authorizedFetch<CostSnapshot>('/api/costing/snapshots', {
      method: 'POST',
      body: { name: snapshotName.value.trim() || suggestedName.value, calculation: request },
    })
  }
  catch (error) {
    saveError.value = parseApiError(error).kind === 'network' ? t('calculator.errors.network') : t('calculator.errors.save')
  }
  finally {
    saving.value = false
  }
}

const priceText = computed(() => breakdown.value
  ? new Intl.NumberFormat(locale.value, { style: 'currency', currency: breakdown.value.currency }).format(breakdown.value.suggestedPrice)
  : '')
const profitText = computed(() => breakdown.value
  ? new Intl.NumberFormat(locale.value, { style: 'currency', currency: breakdown.value.currency }).format(breakdown.value.profit)
  : '')

onMounted(load)
</script>

<template>
  <div class="stack">
    <div class="topline">
      <NuxtLink
        to="/business"
        class="link"
      >
        {{ $t('settings.back') }}
      </NuxtLink>
      <NuxtLink
        to="/calculator/saved"
        class="link"
      >
        {{ $t('calculator.savedLink') }}
      </NuxtLink>
    </div>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <UiCard
      v-else-if="!canCalculate"
      class="group"
    >
      <p class="intro">
        {{ $t('calculator.needSetup') }}
      </p>
      <UiButton
        v-if="printers.length === 0"
        block
        @click="navigateTo('/printers/new')"
      >
        {{ $t('printers.add') }}
      </UiButton>
      <UiButton
        v-if="spools.length === 0"
        block
        variant="secondary"
        @click="navigateTo('/inventory/new')"
      >
        {{ $t('inventory.addSpool') }}
      </UiButton>
    </UiCard>

    <template v-else>
      <UiCard
        class="summary"
        :aria-busy="calculating"
      >
        <div
          v-if="breakdown"
          class="figures"
          role="status"
        >
          <div>
            <p class="label">
              {{ $t('calculator.suggestedPrice') }}
            </p>
            <p class="price">
              {{ priceText }}
            </p>
          </div>
          <div class="profit">
            <p class="label">
              {{ $t('calculator.profit') }}
            </p>
            <p class="value">
              {{ profitText }}
            </p>
          </div>
        </div>
        <p
          v-else-if="calcError"
          class="intro error"
          role="alert"
        >
          {{ calcError }}
        </p>
        <p
          v-else
          class="intro"
          role="status"
        >
          {{ $t('calculator.fillIn') }}
        </p>
      </UiCard>

      <form
        class="form"
        novalidate
        @submit.prevent="save"
      >
        <UiCard class="group">
          <h2>{{ $t('calculator.groups.print') }}</h2>
          <UiSelect
            v-model="form.printerId"
            :label="$t('calculator.fields.printerId')"
            :options="printerOptions"
          />
          <UiSelect
            v-model="form.spoolId"
            :label="$t('calculator.fields.spoolId')"
            :options="spoolOptions"
          />
          <UiInput
            v-model="form.grams"
            :label="$t('calculator.fields.grams')"
            :error="visible('grams', 'grams')"
            inputmode="decimal"
          />
          <div class="pair">
            <UiInput
              v-model="form.printHours"
              :label="$t('calculator.fields.printHours')"
              :error="visible('printTime', 'printHours', 'printMinutes')"
              inputmode="numeric"
            />
            <UiInput
              v-model="form.printMinutes"
              :label="$t('calculator.fields.printMinutes')"
              inputmode="numeric"
            />
          </div>
        </UiCard>

        <UiCard class="group">
          <h2>{{ $t('calculator.groups.work') }}</h2>
          <UiInput
            v-model="form.labourMinutes"
            :label="$t('calculator.fields.labourMinutes')"
            :hint="$t('calculator.hints.labourMinutes')"
            :error="visible('labourMinutes', 'labourMinutes')"
            inputmode="numeric"
          />
          <UiInput
            v-model="form.packagingCost"
            :label="$t('calculator.fields.packagingCost')"
            :error="visible('packagingCost', 'packagingCost')"
            inputmode="decimal"
          />

          <fieldset class="extras">
            <legend>{{ $t('calculator.groups.extras') }}</legend>
            <div
              v-for="(extra, index) in form.extras"
              :key="index"
              class="extra"
            >
              <UiInput
                v-model="extra.label"
                :label="$t('calculator.fields.extraLabel')"
                :error="visible(`extraLabel${index}`)"
              />
              <UiInput
                v-model="extra.amount"
                :label="$t('calculator.fields.extraAmount')"
                :error="visible(`extraAmount${index}`, ...(extra.amount ? [`extra${index}`] : []))"
                inputmode="decimal"
              />
              <UiButton
                variant="ghost"
                :aria-label="$t('calculator.removeExtra', { number: index + 1 })"
                @click="removeExtra(index)"
              >
                {{ $t('calculator.remove') }}
              </UiButton>
            </div>
            <UiButton
              variant="secondary"
              block
              @click="addExtra"
            >
              {{ $t('calculator.addExtra') }}
            </UiButton>
          </fieldset>
        </UiCard>

        <UiCard class="group">
          <h2>{{ $t('calculator.groups.price') }}</h2>
          <div class="pair">
            <UiInput
              v-model="form.marginPercent"
              :label="$t('calculator.fields.marginPercent')"
              :error="visible('marginPercent', 'marginPercent')"
              inputmode="decimal"
            />
            <UiInput
              v-model="form.feePercent"
              :label="$t('calculator.fields.feePercent')"
              :error="visible('feePercent', 'feePercent')"
              inputmode="decimal"
            />
          </div>
          <p class="intro">
            {{ $t('calculator.hints.margin') }}
          </p>
        </UiCard>

        <UiCard
          v-if="breakdown"
          class="group"
        >
          <h2>{{ $t('calculator.breakdown') }}</h2>
          <p class="intro">
            {{ breakdown.printerName }} · {{ breakdown.materialLabel }}
          </p>
          <CostBreakdownList :breakdown="breakdown" />
        </UiCard>

        <UiCard class="group">
          <h2>{{ $t('calculator.groups.save') }}</h2>
          <UiInput
            v-model="snapshotName"
            :label="$t('calculator.fields.snapshotName')"
            :placeholder="suggestedName"
            :hint="$t('calculator.hints.snapshotName')"
          />
          <p
            class="feedback"
            role="status"
          >
            <template v-if="savedSnapshot">
              {{ $t('calculator.saved', { name: savedSnapshot.name }) }}
              <NuxtLink
                :to="`/calculator/${savedSnapshot.id}`"
                class="link"
              >
                {{ $t('calculator.viewSaved') }}
              </NuxtLink>
            </template>
          </p>
          <p
            v-if="saveError"
            class="feedback error"
            role="alert"
          >
            {{ saveError }}
          </p>
          <UiButton
            type="submit"
            block
            :loading="saving"
          >
            {{ $t('calculator.save') }}
          </UiButton>
        </UiCard>
      </form>
    </template>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.topline { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
.link { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.group h2 { margin: 0; font-size: var(--font-size-md); }
.intro { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.intro.error { color: var(--danger); }
.summary { position: sticky; top: var(--space-2); z-index: 5; padding: var(--space-3) var(--space-4); border-color: var(--accent); }
.figures { display: flex; align-items: flex-end; justify-content: space-between; gap: var(--space-4); }
.figures p { margin: 0; }
.label { color: var(--muted); font-size: var(--font-size-sm); }
.price { font-size: var(--font-size-xl); font-weight: 800; font-variant-numeric: tabular-nums; line-height: 1.1; }
.profit { text-align: right; }
.value { color: var(--accent-text); font-size: var(--font-size-lg); font-weight: 700; font-variant-numeric: tabular-nums; }
.pair { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); align-items: start; }
.extras { display: grid; gap: var(--space-3); margin: 0; padding: 0; border: 0; min-width: 0; }
.extras legend { padding: 0; margin-bottom: var(--space-2); color: var(--muted); font-size: var(--font-size-sm); }
.extra { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-2) var(--space-3); padding: var(--space-3); background: var(--bg-elevated); border: 1px solid var(--border); border-radius: var(--radius-md); }
.extra > :last-child { grid-column: 1 / -1; }
.feedback { margin: 0; min-height: 1.25em; color: var(--accent-text); font-size: var(--font-size-sm); }
.feedback.error { color: var(--danger); }
</style>
