<script setup lang="ts">
import { remainingPercent, validateAdjustment } from '~/utils/inventory'
import type { Movement, Spool } from '~/utils/inventory'

definePageMeta({ titleKey: 'inventory.spoolTitle' })

const { t, n, d } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const id = String(route.params.id)

const spool = ref<Spool | null>(null)
const movements = ref<Movement[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

const direction = ref<'add' | 'remove'>('remove')
const amount = ref('')
const reason = ref('')
const amountError = ref('')
const reasonError = ref('')
const saving = ref(false)
const message = ref('')
const saveError = ref('')

const directionOptions = computed(() => [
  { value: 'remove', label: t('inventory.remove') },
  { value: 'add', label: t('inventory.add') },
])

async function load() {
  status.value = 'loading'
  try {
    const [loadedSpool, loadedMovements] = await Promise.all([
      auth.authorizedFetch<Spool>(`/api/spools/${id}`),
      auth.authorizedFetch<Movement[]>(`/api/spools/${id}/movements`),
    ])
    spool.value = loadedSpool
    movements.value = loadedMovements
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function adjust() {
  message.value = ''
  saveError.value = ''
  const { adjustment, errors } = validateAdjustment(direction.value, amount.value, reason.value)
  amountError.value = errors.amount ? t('inventory.errors.amount') : ''
  reasonError.value = errors.reason ? t('inventory.errors.reason') : ''
  if (!adjustment) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }

  saving.value = true
  try {
    spool.value = await auth.authorizedFetch<Spool>(`/api/spools/${id}/adjustments`, { method: 'POST', body: adjustment })
    movements.value = await auth.authorizedFetch<Movement[]>(`/api/spools/${id}/movements`)
    amount.value = ''
    reason.value = ''
    message.value = t('inventory.adjusted')
  }
  catch (error) {
    const parsed = parseApiError(error)
    saveError.value = parsed.kind === 'validation'
      ? (Object.values(parsed.fieldErrors)[0]?.[0] ?? t('inventory.errors.save'))
      : t('inventory.errors.save')
  }
  finally {
    saving.value = false
  }
}

async function setArchived(value: boolean) {
  try {
    spool.value = await auth.authorizedFetch<Spool>(`/api/spools/${id}/${value ? 'archive' : 'restore'}`, { method: 'POST' })
  }
  catch {
    saveError.value = t('inventory.errors.save')
  }
}

function kindLabel(kind: Movement['kind']) {
  return t(`inventory.kinds.${kind}`)
}

function signed(value: number) {
  return `${value > 0 ? '+' : ''}${n(value)} g`
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

    <template v-else-if="spool">
      <UiCard class="group">
        <h2 class="heading">
          {{ spool.materialName }} · {{ spool.colorName }}
        </h2>
        <p class="meta">
          {{ $t('inventory.remaining', { grams: n(spool.remainingWeightGrams), percent: remainingPercent(spool) }) }}
        </p>
        <p class="meta">
          {{ spool.supplierName ?? $t('inventory.noSupplier') }} · {{ $t('inventory.costPerKg', { amount: n(spool.costPerGram * 1000, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }) }}
        </p>
        <UiStatusChip
          v-if="spool.isArchived"
          status="neutral"
        >
          {{ $t('inventory.archived') }}
        </UiStatusChip>
      </UiCard>

      <form
        class="form"
        novalidate
        @submit.prevent="adjust"
      >
        <UiCard class="group">
          <h2 class="heading">
            {{ $t('inventory.adjustStock') }}
          </h2>
          <UiSegmentedControl
            v-model="direction"
            :label="$t('inventory.direction')"
            :options="directionOptions"
          />
          <UiInput
            v-model="amount"
            :label="$t('inventory.amount')"
            :error="amountError"
            inputmode="numeric"
          />
          <UiInput
            v-model="reason"
            :label="$t('inventory.reason')"
            :hint="$t('inventory.hints.reason')"
            :error="reasonError"
          />
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
          >
            {{ $t('inventory.saveAdjustment') }}
          </UiButton>
        </UiCard>
      </form>

      <section aria-labelledby="history-heading">
        <h2
          id="history-heading"
          class="heading"
        >
          {{ $t('inventory.history') }}
        </h2>
        <ul class="history">
          <li
            v-for="movement in movements"
            :key="movement.id"
          >
            <span class="what">
              <strong>{{ kindLabel(movement.kind) }}</strong>
              <span class="why">{{ movement.reason }}</span>
              <span class="when">{{ d(new Date(movement.createdAt), { dateStyle: 'medium', timeStyle: 'short' }) }}</span>
            </span>
            <span class="numbers">
              <strong>{{ signed(movement.deltaGrams) }}</strong>
              <span class="balance">{{ $t('inventory.balance', { grams: n(movement.balanceAfterGrams) }) }}</span>
            </span>
          </li>
        </ul>
      </section>

      <UiButton
        variant="secondary"
        block
        @click="setArchived(!spool.isArchived)"
      >
        {{ spool.isArchived ? $t('inventory.restore') : $t('inventory.archive') }}
      </UiButton>
    </template>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.heading { margin: 0; font-size: var(--font-size-md); }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.status { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.history { display: grid; gap: var(--space-2); margin: var(--space-2) 0 0; padding: 0; list-style: none; }
.history li { display: flex; justify-content: space-between; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.what, .numbers { display: grid; gap: 2px; }
.numbers { text-align: end; }
.why, .when, .balance { color: var(--muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
</style>
