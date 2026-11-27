<script setup lang="ts">
import { availabilityTone, signedUnits, validateStockAdjustment, validateThreshold } from '~/utils/finishedStock'
import type { FinishedStockDetail, FinishedStockMovement } from '~/utils/finishedStock'
import { localizeServerMessage, parseApiError } from '~/utils/apiError'

definePageMeta({ titleKey: 'finished.detailTitle' })

const { t, n, d } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const variantId = String(route.params.variantId)

const detail = ref<FinishedStockDetail | null>(null)
const movements = ref<FinishedStockMovement[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const currency = ref('EUR')
const notice = ref('')

const showAdjust = ref(false)
const direction = ref<'add' | 'remove'>('add')
const amount = ref('')
const reason = ref('')
const allowBelowReserved = ref(false)
const needsOverride = ref(false)
const amountError = ref('')
const reasonError = ref('')
const adjustError = ref('')
const saving = ref(false)

const threshold = ref('')
const thresholdError = ref('')
const savingThreshold = ref(false)

const item = computed(() => detail.value?.item ?? null)
const money = (value: number) => n(value, { style: 'currency', currency: currency.value })
const directionOptions = computed(() => [
  { value: 'add', label: t('finished.add') },
  { value: 'remove', label: t('finished.remove') },
])

async function load() {
  status.value = 'loading'
  try {
    const [loaded, list, settings] = await Promise.all([
      auth.authorizedFetch<FinishedStockDetail>(`/api/finished-stock/${variantId}`),
      auth.authorizedFetch<FinishedStockMovement[]>(`/api/finished-stock/${variantId}/movements`),
      auth.authorizedFetch<{ currency: string }>('/api/workspace/settings').catch(() => ({ currency: 'EUR' })),
    ])
    detail.value = loaded
    movements.value = list
    currency.value = settings.currency
    threshold.value = loaded.item.lowStockThreshold === null ? '' : String(loaded.item.lowStockThreshold)
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function openAdjust() {
  direction.value = 'add'
  amount.value = ''
  reason.value = ''
  allowBelowReserved.value = false
  needsOverride.value = false
  amountError.value = ''
  reasonError.value = ''
  adjustError.value = ''
  showAdjust.value = true
}

async function reload() {
  const [loaded, list] = await Promise.all([
    auth.authorizedFetch<FinishedStockDetail>(`/api/finished-stock/${variantId}`),
    auth.authorizedFetch<FinishedStockMovement[]>(`/api/finished-stock/${variantId}/movements`),
  ])
  detail.value = loaded
  movements.value = list
}

async function adjust() {
  adjustError.value = ''
  const { adjustment, errors } = validateStockAdjustment(direction.value, amount.value, reason.value, allowBelowReserved.value)
  amountError.value = errors.amount ? t('finished.errors.amount') : ''
  reasonError.value = errors.reason ? t('finished.errors.reason') : ''
  if (!adjustment) {
    nextTick(() => document.querySelector<HTMLElement>('dialog [aria-invalid="true"]')?.focus())
    return
  }

  saving.value = true
  try {
    await auth.authorizedFetch(`/api/finished-stock/${variantId}/adjustments`, { method: 'POST', body: adjustment })
    await reload()
    showAdjust.value = false
    notice.value = t('finished.adjusted')
  }
  catch (error) {
    const parsed = parseApiError(error)
    const first = parsed.kind === 'validation' ? Object.values(parsed.fieldErrors)[0]?.[0] ?? '' : ''
    needsOverride.value = /^Only \d+ available/.test(first)
    adjustError.value = (first ? localizeServerMessage(first, t) : undefined)
      ?? (parsed.kind === 'network' ? t('finished.errors.network') : t('finished.errors.save'))
  }
  finally {
    saving.value = false
  }
}

async function saveThreshold() {
  const { threshold: value, valid } = validateThreshold(threshold.value)
  thresholdError.value = valid ? '' : t('finished.errors.threshold')
  if (!valid) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }
  savingThreshold.value = true
  try {
    await auth.authorizedFetch(`/api/finished-stock/${variantId}/low-stock`, { method: 'PUT', body: { threshold: value } })
    await reload()
    notice.value = t('finished.thresholdSaved')
  }
  catch {
    thresholdError.value = t('finished.errors.save')
  }
  finally {
    savingThreshold.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/inventory/finished"
      class="back"
    >
      {{ $t('finished.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <template v-else-if="item && detail">
      <p
        class="notice"
        role="status"
      >
        {{ notice }}
      </p>

      <UiCard class="group">
        <h2 class="heading">
          {{ item.productName }} · {{ item.variantName }}
        </h2>
        <p
          v-if="item.sku"
          class="meta"
        >
          {{ item.sku }}
        </p>
        <div class="figures">
          <div>
            <span class="num">{{ n(item.onHand) }}</span>
            <span class="meta">{{ $t('finished.onHandLabel') }}</span>
          </div>
          <div>
            <span class="num">{{ n(item.reserved) }}</span>
            <span class="meta">{{ $t('finished.reservedLabel') }}</span>
          </div>
          <div>
            <span class="num">{{ n(item.available) }}</span>
            <span class="meta">{{ $t('finished.availableLabel') }}</span>
          </div>
        </div>
        <div class="chips">
          <UiStatusChip :status="availabilityTone(item)">
            {{ $t('finished.availableCount', { count: item.available }) }}
          </UiStatusChip>
          <UiStatusChip
            v-if="item.isLow"
            status="warning"
          >
            {{ $t('finished.low') }}
          </UiStatusChip>
          <UiStatusChip
            v-if="item.available < 0"
            status="danger"
          >
            {{ $t('finished.shortage', { count: -item.available }) }}
          </UiStatusChip>
        </div>
        <p
          v-if="item.averageUnitCost !== null"
          class="meta"
        >
          {{ $t('finished.unitCost', { amount: money(item.averageUnitCost) }) }}
          <template v-if="item.stockValue !== null">
            · {{ $t('finished.stockValue', { amount: money(item.stockValue) }) }}
          </template>
        </p>
        <UiButton
          block
          @click="openAdjust"
        >
          {{ $t('finished.adjust') }}
        </UiButton>
      </UiCard>

      <form
        class="group card-form"
        novalidate
        @submit.prevent="saveThreshold"
      >
        <UiCard class="group">
          <h3 class="heading">
            {{ $t('finished.lowStockTitle') }}
          </h3>
          <UiInput
            v-model="threshold"
            :label="$t('finished.threshold')"
            :hint="$t('finished.thresholdHint')"
            :error="thresholdError"
            inputmode="numeric"
          />
          <UiButton
            type="submit"
            variant="secondary"
            block
            :loading="savingThreshold"
          >
            {{ $t('finished.saveThreshold') }}
          </UiButton>
        </UiCard>
      </form>

      <section
        v-if="detail.reservations.length > 0"
        aria-labelledby="reservations-heading"
      >
        <h3
          id="reservations-heading"
          class="heading"
        >
          {{ $t('finished.reservationsTitle') }}
        </h3>
        <ul class="plain">
          <li
            v-for="reservation in detail.reservations"
            :key="reservation.orderLineId"
          >
            <NuxtLink
              :to="`/orders/${reservation.orderId}`"
              class="row"
            >
              <strong>{{ $t('finished.orderNumber', { number: reservation.orderNumber }) }}</strong>
              <span class="meta">{{ $t('finished.units', { count: reservation.quantity }) }}</span>
            </NuxtLink>
          </li>
        </ul>
      </section>

      <section aria-labelledby="history-heading">
        <h3
          id="history-heading"
          class="heading"
        >
          {{ $t('finished.history') }}
        </h3>
        <p
          v-if="movements.length === 0"
          class="meta"
        >
          {{ $t('finished.noHistory') }}
        </p>
        <ul class="plain">
          <li
            v-for="movement in movements"
            :key="movement.id"
            class="movement"
          >
            <span class="what">
              <strong>{{ $t(`finished.kinds.${movement.kind}`) }}</strong>
              <span class="meta">{{ movement.reason }}</span>
              <span class="meta">{{ d(new Date(movement.createdAt), { dateStyle: 'medium', timeStyle: 'short' }) }}</span>
            </span>
            <span class="numbers">
              <strong v-if="movement.delta !== 0">{{ signedUnits(movement.delta) }}</strong>
              <strong v-else>{{ $t('finished.reservedDelta', { count: signedUnits(movement.reservedDelta) }) }}</strong>
              <span class="meta">{{ $t('finished.balance', { count: movement.balanceAfter }) }}</span>
            </span>
          </li>
        </ul>
      </section>
    </template>

    <UiDialog
      v-model="showAdjust"
      :title="$t('finished.adjust')"
    >
      <form
        class="form"
        novalidate
        @submit.prevent="adjust"
      >
        <UiSegmentedControl
          v-model="direction"
          :label="$t('finished.direction')"
          :options="directionOptions"
        />
        <UiInput
          v-model="amount"
          :label="$t('finished.amount')"
          :error="amountError"
          inputmode="numeric"
        />
        <UiInput
          v-model="reason"
          :label="$t('finished.reason')"
          :hint="$t('finished.reasonHint')"
          :error="reasonError"
        />
        <label
          v-if="needsOverride"
          class="toggle"
        >
          <input
            v-model="allowBelowReserved"
            type="checkbox"
          >
          {{ $t('finished.allowBelowReserved') }}
        </label>
        <p
          v-if="adjustError"
          class="error"
          role="alert"
        >
          {{ adjustError }}
        </p>
        <UiButton
          type="submit"
          block
          :loading="saving"
        >
          {{ $t('finished.saveAdjustment') }}
        </UiButton>
      </form>
    </UiDialog>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.card-form { display: block; }
.heading { margin: 0 0 var(--space-2); font-size: var(--font-size-md); }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
.notice { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.figures { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-2); text-align: center; }
.figures div { display: grid; gap: 2px; padding: var(--space-3) var(--space-2); background: var(--surface-2); border-radius: var(--radius-md); }
.num { font-size: var(--font-size-lg); font-weight: 700; }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.plain { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.row, .movement { display: flex; justify-content: space-between; gap: var(--space-3); padding: var(--space-3) var(--space-4); color: var(--text); text-decoration: none; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.row { align-items: center; min-height: var(--tap-size); }
.what, .numbers { display: grid; gap: 2px; }
.numbers { text-align: end; }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
</style>
