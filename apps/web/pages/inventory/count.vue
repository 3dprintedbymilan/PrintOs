<script setup lang="ts">
import { changedRows, rowState, signedNumber } from '~/utils/stockCount'
import type { BulkResponse } from '~/utils/stockCount'
import type { Spool } from '~/utils/inventory'
import type { FinishedStockList } from '~/utils/finishedStock'

definePageMeta({ titleKey: 'stockTools.count.title' })

const { t, n } = useI18n()
const auth = useAuthStore()

type Mode = 'filament' | 'finished'
const mode = ref<Mode>('filament')
const spools = ref<Spool[]>([])
const finished = ref<FinishedStockList['items']>([])
const reason = ref(t('stockTools.count.defaultReason'))
const allowBelowReserved = ref(false)
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const message = ref('')
const saveError = ref('')
const reasonError = ref('')
const form = reactive<{ inputs: Record<string, string>, errors: Record<string, string> }>({ inputs: {}, errors: {} })

const modeOptions = computed(() => [
  { value: 'filament', label: t('stockTools.count.filament') },
  { value: 'finished', label: t('stockTools.count.finished') },
])

const maxValue = 100_000
const rows = computed(() => mode.value === 'filament'
  ? spools.value.map(spool => ({
      id: spool.id,
      title: `${spool.materialName} · ${spool.colorName}`,
      subtitle: spool.supplierName ?? '',
      current: spool.remainingWeightGrams,
      unit: 'g',
      swatch: spool.colorHex,
      reserved: 0,
    }))
  : finished.value.map(item => ({
      id: item.variantId,
      title: `${item.productName} · ${item.variantName}`,
      subtitle: item.sku ?? '',
      current: item.onHand,
      unit: '',
      swatch: null as string | null,
      reserved: item.reserved,
    })))

const pending = computed(() => changedRows(rows.value.map(row => ({ id: row.id, current: row.current, input: form.inputs[row.id] ?? '' })), maxValue))

function state(row: { id: string, current: number }) {
  return rowState({ id: row.id, current: row.current, input: form.inputs[row.id] ?? '' }, maxValue)
}

function resetInputs() {
  form.inputs = {}
  form.errors = {}
}

async function load() {
  status.value = 'loading'
  try {
    const [loadedSpools, loadedFinished] = await Promise.all([
      auth.authorizedFetch<Spool[]>('/api/spools'),
      auth.authorizedFetch<FinishedStockList>('/api/finished-stock'),
    ])
    spools.value = loadedSpools
    finished.value = loadedFinished.items.filter(item => !item.productArchived || item.onHand > 0)
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function submit() {
  message.value = ''
  saveError.value = ''
  reasonError.value = reason.value.trim() === '' ? t('stockTools.count.errors.reason') : ''
  form.errors = {}
  for (const id of pending.value.invalidIds) form.errors[id] = t('stockTools.count.errors.number')
  if (reasonError.value || pending.value.invalidIds.length > 0) {
    await nextTick()
    document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  const lines = pending.value.lines
  saving.value = true
  try {
    const body = mode.value === 'filament'
      ? { reason: reason.value.trim(), lines: lines.map(line => ({ spoolId: line.id, remainingGrams: line.counted })) }
      : { reason: reason.value.trim(), allowBelowReserved: allowBelowReserved.value, lines: lines.map(line => ({ variantId: line.id, counted: line.counted })) }
    const url = mode.value === 'filament' ? '/api/spools/bulk-adjustments' : '/api/finished-stock/bulk-adjustments'
    const result = await auth.authorizedFetch<BulkResponse>(url, { method: 'POST', body })
    message.value = t('stockTools.count.saved', { count: result.changed })
    resetInputs()
    await load()
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation') {
      for (const [key, messages] of Object.entries(parsed.fieldErrors)) {
        const match = /^lines\[(\d+)\]/.exec(key)
        const text = localizeServerMessage(messages[0] ?? '', t) ?? t('stockTools.count.errors.line')
        const line = match ? lines[Number(match[1])] : undefined
        if (line) form.errors[line.id] = text
        else if (key === 'reason') reasonError.value = t('stockTools.count.errors.reason')
        else saveError.value = text
      }
      await nextTick()
      document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    }
    else {
      saveError.value = parsed.kind === 'network' ? t('stockTools.count.errors.network') : t('stockTools.count.errors.save')
    }
  }
  finally {
    saving.value = false
  }
}

watch(mode, () => {
  message.value = ''
  saveError.value = ''
  form.errors = {}
})
onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/inventory"
      class="back"
    >
      {{ $t('finished.backToInventory') }}
    </NuxtLink>
    <p class="meta">
      {{ $t('stockTools.count.intro') }}
    </p>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <form
      v-else
      class="stack"
      novalidate
      @submit.prevent="submit"
    >
      <UiSegmentedControl
        v-model="mode"
        :label="$t('stockTools.count.mode')"
        :options="modeOptions"
      />

      <StateEmpty
        v-if="rows.length === 0"
        :message="$t(mode === 'filament' ? 'stockTools.count.emptyFilament' : 'stockTools.count.emptyFinished')"
      />
      <ul
        v-else
        class="list"
      >
        <li
          v-for="row in rows"
          :key="row.id"
          class="row"
        >
          <span
            v-if="row.swatch"
            class="swatch"
            :style="{ background: row.swatch }"
            aria-hidden="true"
          />
          <span class="name">
            <strong>{{ row.title }}</strong>
            <span class="meta">{{ $t(mode === 'filament' ? 'stockTools.count.currentGrams' : 'stockTools.count.currentUnits', { count: n(row.current) }) }}<template v-if="row.reserved > 0"> · {{ $t('finished.reserved', { count: row.reserved }) }}</template></span>
          </span>
          <span class="entry">
            <input
              v-model="form.inputs[row.id]"
              :aria-label="$t('stockTools.count.countedFor', { name: row.title })"
              :placeholder="$t('stockTools.count.counted')"
              :aria-invalid="form.errors[row.id] ? 'true' : undefined"
              :aria-describedby="form.errors[row.id] ? `err-${row.id}` : undefined"
              inputmode="numeric"
              autocomplete="off"
            >
            <span
              v-if="state(row).kind === 'change'"
              class="delta"
              :class="{ neg: (state(row) as { delta: number }).delta < 0 }"
            >{{ signedNumber((state(row) as { delta: number }).delta) }}{{ row.unit && ` ${row.unit}` }}</span>
          </span>
          <p
            v-if="form.errors[row.id]"
            :id="`err-${row.id}`"
            class="error full"
          >
            {{ form.errors[row.id] }}
          </p>
        </li>
      </ul>

      <template v-if="rows.length > 0">
        <UiInput
          v-model="reason"
          :label="$t('stockTools.count.reason')"
          :error="reasonError"
        />
        <label
          v-if="mode === 'finished'"
          class="toggle"
        >
          <input
            v-model="allowBelowReserved"
            type="checkbox"
          >
          {{ $t('finished.allowBelowReserved') }}
        </label>
        <p
          class="status"
          role="status"
        >
          {{ message }}
        </p>
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
          :disabled="pending.lines.length === 0 && pending.invalidIds.length === 0"
        >
          {{ $t('stockTools.count.save', { count: pending.lines.length }) }}
        </UiButton>
      </template>
    </form>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.list { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.row { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.row:not(:has(.swatch)) { grid-template-columns: minmax(0, 1fr) auto; }
.swatch { display: block; width: 28px; height: 28px; border-radius: var(--radius-pill); border: 1px solid var(--border); }
.name { display: grid; min-width: 0; overflow-wrap: anywhere; }
.entry { display: grid; justify-items: end; gap: 2px; }
.entry input { width: 104px; min-height: var(--tap-size); padding: 0 var(--space-3); text-align: end; background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); font: inherit; caret-color: var(--accent); }
.entry input[aria-invalid='true'] { border-color: var(--danger); }
.delta { color: var(--accent); font-size: var(--font-size-sm); font-weight: 600; font-variant-numeric: tabular-nums; }
.delta.neg { color: var(--warning); }
.full { grid-column: 1 / -1; }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
.status { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
