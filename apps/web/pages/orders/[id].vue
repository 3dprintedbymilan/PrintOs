<script setup lang="ts">
import type { Order, OrderFormValues, OrderStatus, PaymentMethod } from '~/utils/orders'
import {
  orderErrorMessages,
  orderLabel,
  orderToForm,
  orderedTransitions,
  paymentMethods,
  orderPaymentTone,
  progressIndex,
  progressSteps,
  serverOrderErrors,
  orderStatusTone,
  transitionKind,
  transitionLabelKey,
  validateOrderForm,
} from '~/utils/orders'
import { parseApiError, localizeServerMessage } from '~/utils/apiError'
import type { OrderReservations } from '~/utils/finishedStock'
import { parseDecimal } from '~/utils/settingsForm'

definePageMeta({ titleKey: 'orders.detailTitle' })

const { t, n, d } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const lookups = useOrderLookups()

const id = String(route.params.id)

const order = ref<Order | null>(null)
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const notice = ref('')
const actionError = ref('')
const busy = ref(false)

const editing = ref(false)
const form = ref<OrderFormValues | null>(null)
const errors = ref<Record<string, string>>({})
const saving = ref(false)

const reservations = ref<OrderReservations | null>(null)
const reserveError = ref('')
const offerOverReserve = ref(false)
const reserving = ref(false)

const confirmCancel = ref(false)
const confirmDelete = ref(false)
const rightsPrompt = ref<OrderStatus | null>(null)
const showPayment = ref(false)
const paymentAmount = ref('')
const paymentMethod = ref('')
const paymentNote = ref('')
const paymentError = ref('')

const money = (amount: number) => n(amount, { style: 'currency', currency: order.value?.currency ?? 'EUR' })
const day = (value: string) => d(new Date(value), { dateStyle: 'medium' })
const dueDay = (value: string) => d(new Date(`${value}T00:00:00`), { dateStyle: 'medium' })

const actions = computed(() => order.value ? orderedTransitions(order.value.status, order.value.allowedTransitions) : [])
const currentStep = computed(() => order.value ? progressIndex(order.value.status) : -1)
const methodOptions = computed(() => [
  { value: '', label: t('orders.methods.none') },
  ...paymentMethods.map(method => ({ value: method, label: t(`orders.methods.${method}`) })),
])
const canDelete = computed(() => order.value?.status === 'Draft' && order.value.payments.length === 0)

async function load() {
  status.value = 'loading'
  try {
    order.value = await auth.authorizedFetch<Order>(`/api/orders/${id}`)
    status.value = 'ready'
    loadReservations()
  }
  catch {
    status.value = 'failed'
  }
}

async function loadReservations() {
  try {
    reservations.value = await auth.authorizedFetch<OrderReservations>(`/api/orders/${id}/reservations`)
  }
  catch {
    reservations.value = null
  }
}

async function reserveStock(allowOverReserve = false) {
  reserving.value = true
  reserveError.value = ''
  offerOverReserve.value = false
  try {
    reservations.value = await auth.authorizedFetch<OrderReservations>(`/api/orders/${id}/reservations`, { method: 'POST', body: { allowOverReserve } })
    notice.value = t('finished.order.reserved')
  }
  catch (error) {
    const parsed = parseApiError(error)
    const first = parsed.kind === 'validation' ? Object.values(parsed.fieldErrors)[0]?.[0] ?? '' : ''
    const detail = first.includes(': ') ? first.slice(first.lastIndexOf(': ') + 2) : first
    reserveError.value = (detail ? localizeServerMessage(detail, t) : undefined) ?? t('finished.errors.save')
    offerOverReserve.value = /Only \d+ available/.test(detail)
  }
  finally {
    reserving.value = false
  }
}

async function releaseStock() {
  reserving.value = true
  reserveError.value = ''
  offerOverReserve.value = false
  try {
    reservations.value = await auth.authorizedFetch<OrderReservations>(`/api/orders/${id}/reservations`, { method: 'DELETE' })
    notice.value = t('finished.order.released')
  }
  catch {
    reserveError.value = t('finished.errors.save')
  }
  finally {
    reserving.value = false
  }
}

const hasActiveReservation = computed(() => reservations.value?.lines.some(line => line.status === 'Active') ?? false)

watch(() => order.value?.status, (current, previous) => {
  if (previous !== undefined && current !== previous) loadReservations()
})

async function runAction(task: () => Promise<Order>, message: string) {
  busy.value = true
  actionError.value = ''
  notice.value = ''
  try {
    order.value = await task()
    notice.value = message
    return true
  }
  catch (error) {
    actionError.value = errorMessage(error)
    return false
  }
  finally {
    busy.value = false
  }
}

function errorMessage(error: unknown) {
  const parsed = parseApiError(error)
  const first = Object.values(parsed.fieldErrors)[0]?.[0]
  return parsed.kind === 'validation' && first ? t('orders.errors.rejectedWith', { reason: first }) : t('orders.errors.save')
}

async function move(target: OrderStatus, acknowledge = false) {
  if (!order.value) return
  busy.value = true
  actionError.value = ''
  notice.value = ''
  try {
    order.value = await auth.authorizedFetch<Order>(`/api/orders/${id}/status`, { method: 'POST', body: { status: target, acknowledgeRisk: acknowledge } })
    notice.value = t('orders.statusChanged', { status: t(`orders.status.${target}`) })
    rightsPrompt.value = null
    confirmCancel.value = false
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && Object.keys(parsed.fieldErrors).some(key => key === 'commercialRights')) rightsPrompt.value = target
    else actionError.value = errorMessage(error)
  }
  finally {
    busy.value = false
  }
}

function startAction(target: OrderStatus) {
  if (transitionKind(order.value!.status, target) === 'cancel') confirmCancel.value = true
  else move(target)
}

async function startEdit() {
  if (!order.value) return
  actionError.value = ''
  try {
    await lookups.load()
  }
  catch {
    actionError.value = t('orders.errors.save')
    return
  }
  form.value = orderToForm(order.value)
  errors.value = {}
  editing.value = true
}

async function saveEdit() {
  if (!order.value || !form.value) return
  const { request, errors: found } = validateOrderForm(order.value.linesEditable ? form.value : { ...form.value, lines: [] })
  errors.value = orderErrorMessages(found, t)
  if (!request) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }
  const body = order.value.linesEditable ? request : { ...request, lines: null, discountAmount: order.value.discountAmount }
  saving.value = true
  actionError.value = ''
  try {
    order.value = await auth.authorizedFetch<Order>(`/api/orders/${id}`, { method: 'PUT', body })
    editing.value = false
    notice.value = t('orders.saved')
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && Object.keys(parsed.fieldErrors).length > 0) {
      errors.value = orderErrorMessages(serverOrderErrors(parsed.fieldErrors), t)
      nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    }
    else actionError.value = t('orders.errors.save')
  }
  finally {
    saving.value = false
  }
}

function openPayment() {
  paymentAmount.value = order.value ? String(order.value.amountDue).replace('.', ',') : ''
  paymentMethod.value = ''
  paymentNote.value = ''
  paymentError.value = ''
  showPayment.value = true
}

async function savePayment() {
  if (!order.value) return
  const amount = parseDecimal(paymentAmount.value)
  if (amount === null || amount <= 0) {
    paymentError.value = t('orders.errors.paymentAmount')
  }
  else if (amount > order.value.amountDue) {
    paymentError.value = t('orders.errors.paymentTooHigh')
  }
  else {
    paymentError.value = ''
  }
  if (paymentError.value) {
    nextTick(() => document.querySelector<HTMLElement>('dialog [aria-invalid="true"]')?.focus())
    return
  }
  const ok = await runAction(
    () => auth.authorizedFetch<Order>(`/api/orders/${id}/payments`, {
      method: 'POST',
      body: { amount, method: (paymentMethod.value || null) as PaymentMethod | null, note: paymentNote.value.trim() || null },
    }),
    t('orders.paymentRecorded'),
  )
  if (ok) showPayment.value = false
}

async function removePayment(paymentId: string) {
  await runAction(() => auth.authorizedFetch<Order>(`/api/orders/${id}/payments/${paymentId}`, { method: 'DELETE' }), t('orders.paymentRemoved'))
}

async function removeOrder() {
  busy.value = true
  try {
    await auth.authorizedFetch(`/api/orders/${id}`, { method: 'DELETE' })
    await navigateTo('/orders')
  }
  catch (error) {
    actionError.value = errorMessage(error)
    confirmDelete.value = false
  }
  finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/orders"
      class="back"
    >
      {{ $t('orders.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <template v-else-if="order">
      <p
        class="notice"
        role="status"
      >
        {{ notice }}
      </p>
      <p
        v-if="actionError"
        class="error"
        role="alert"
      >
        {{ actionError }}
      </p>

      <form
        v-if="editing && form"
        class="stack"
        novalidate
        @submit.prevent="saveEdit"
      >
        <h2 class="heading">
          {{ $t('orders.editTitle', { number: orderLabel(order.number) }) }}
        </h2>
        <p
          v-if="!order.linesEditable"
          class="hint"
        >
          {{ $t('orders.linesLocked') }}
        </p>
        <OrderForm
          v-model="form"
          :customers="lookups.customers.value"
          :variants="lookups.variants.value"
          :list-prices="lookups.listPrices.value"
          :currency="lookups.currency.value"
          :errors="errors"
          :lines-locked="!order.linesEditable"
          @customer-created="lookups.addCustomer"
        />
        <UiButton
          type="submit"
          block
          :loading="saving"
        >
          {{ $t('orders.save') }}
        </UiButton>
        <UiButton
          variant="secondary"
          block
          @click="editing = false"
        >
          {{ $t('orders.cancelEdit') }}
        </UiButton>
      </form>

      <template v-else>
        <UiCard class="head">
          <div class="title-row">
            <h2 class="heading">
              {{ orderLabel(order.number) }}
            </h2>
            <span class="chips">
              <UiStatusChip :status="orderStatusTone[order.status]">
                {{ $t(`orders.status.${order.status}`) }}
              </UiStatusChip>
              <UiStatusChip :status="orderPaymentTone[order.paymentStatus]">
                {{ $t(`orders.payment.${order.paymentStatus}`) }}
              </UiStatusChip>
              <UiStatusChip
                v-if="order.isOverdue"
                status="danger"
              >
                {{ $t('orders.overdue') }}
              </UiStatusChip>
            </span>
          </div>
          <dl class="facts">
            <div>
              <dt>{{ $t('orders.fields.customer') }}</dt>
              <dd>
                <NuxtLink
                  v-if="order.customerId"
                  :to="`/customers/${order.customerId}`"
                >
                  {{ order.customerName }}
                </NuxtLink>
                <template v-else>
                  {{ $t('orders.noCustomer') }}
                </template>
              </dd>
            </div>
            <div>
              <dt>{{ $t('orders.fields.channel') }}</dt>
              <dd>{{ $t(`orders.channels.${order.channel}`) }}</dd>
            </div>
            <div>
              <dt>{{ $t('orders.fields.orderedAt') }}</dt>
              <dd>{{ day(order.orderedAt) }}</dd>
            </div>
            <div v-if="order.dueDate">
              <dt>{{ $t('orders.fields.dueDate') }}</dt>
              <dd>{{ dueDay(order.dueDate) }}</dd>
            </div>
          </dl>
        </UiCard>

        <UiCard
          v-if="order.status !== 'Cancelled'"
          class="progress"
        >
          <h3>{{ $t('orders.progress') }}</h3>
          <ol
            class="steps"
            :aria-label="$t('orders.progress')"
          >
            <li
              v-for="(step, index) in progressSteps"
              :key="step"
              :class="{ done: index < currentStep, current: index === currentStep }"
              :aria-current="index === currentStep ? 'step' : undefined"
            >
              <span
                class="dot"
                aria-hidden="true"
              />
              <span class="step-label">{{ $t(`orders.status.${step}`) }}</span>
            </li>
          </ol>
        </UiCard>

        <div
          v-if="order.hasRightsWarning"
          class="rights"
          role="note"
        >
          <strong>{{ $t('orders.rights.title') }}</strong>
          <span>{{ $t('orders.rights.orderWarning') }}</span>
        </div>

        <div
          v-if="actions.length > 0"
          class="actions"
        >
          <UiButton
            v-for="(target, index) in actions"
            :key="target"
            :variant="index === 0 && transitionKind(order.status, target) !== 'cancel' ? 'primary' : transitionKind(order.status, target) === 'cancel' ? 'danger' : 'secondary'"
            block
            :loading="busy"
            @click="startAction(target)"
          >
            {{ $t(transitionLabelKey(order.status, target)) }}
          </UiButton>
        </div>

        <UiCard class="group">
          <h3>{{ $t('orders.lines') }}</h3>
          <p
            v-if="order.lines.length === 0"
            class="hint"
          >
            {{ $t('orders.noLines') }}
          </p>
          <ul class="line-list">
            <li
              v-for="line in order.lines"
              :key="line.id"
            >
              <span class="line-main">
                <strong>{{ line.description }}</strong>
                <span class="hint">
                  {{ line.quantity }} × {{ money(line.unitPrice) }}
                  <template v-if="line.discountPercent > 0"> · -{{ line.discountPercent }}%</template>
                  <template v-if="line.listUnitPrice !== null && line.listUnitPrice !== line.unitPrice"> · {{ $t('orders.listPrice', { price: money(line.listUnitPrice) }) }}</template>
                </span>
                <span
                  v-if="line.commercialRightsStatus === 'NotCleared'"
                  class="line-warning"
                >
                  {{ line.riskAcknowledged ? $t('orders.rights.acknowledged') : $t('orders.rights.notAcknowledged') }}
                </span>
              </span>
              <span class="amount">{{ money(line.lineTotal) }}</span>
            </li>
          </ul>
          <dl class="totals">
            <div>
              <dt>{{ $t('orders.subtotal') }}</dt>
              <dd>{{ money(order.subtotal) }}</dd>
            </div>
            <div v-if="order.discountAmount > 0">
              <dt>{{ $t('orders.discount') }}</dt>
              <dd>-{{ money(order.discountAmount) }}</dd>
            </div>
            <div class="grand">
              <dt>{{ $t('orders.total') }}</dt>
              <dd>{{ money(order.totalAmount) }}</dd>
            </div>
            <div class="sub">
              <dt>{{ $t('orders.cost') }}</dt>
              <dd>{{ money(order.totalCost) }}</dd>
            </div>
            <div class="sub">
              <dt>{{ $t('orders.profit') }}</dt>
              <dd>
                {{ money(order.profit) }}
                <template v-if="order.marginPercent !== null"> ({{ order.marginPercent }}%)</template>
              </dd>
            </div>
          </dl>
          <p
            v-if="!order.costComplete"
            class="hint"
          >
            {{ $t('orders.costIncomplete') }}
          </p>
        </UiCard>

        <UiCard
          v-if="reservations && reservations.lines.length > 0"
          class="group"
        >
          <h3>{{ $t('finished.order.title') }}</h3>
          <ul class="line-list">
            <li
              v-for="line in reservations.lines"
              :key="line.orderLineId"
            >
              <span class="line-main">
                <strong>{{ line.description }}</strong>
                <span class="hint">{{ $t('finished.order.lineStock', { available: line.available, onHand: line.onHand }) }}</span>
              </span>
              <UiStatusChip :status="line.status === 'Active' ? 'info' : line.status === 'Fulfilled' ? 'success' : 'neutral'">
                {{ $t(`finished.order.status.${line.status}`, { reserved: line.reserved, quantity: line.quantity, fulfilled: line.fulfilledQuantity }) }}
              </UiStatusChip>
            </li>
          </ul>
          <p
            v-if="reserveError"
            class="error"
            role="alert"
          >
            {{ reserveError }}
          </p>
          <template v-if="reservations.canReserve">
            <UiButton
              block
              :loading="reserving"
              @click="reserveStock(false)"
            >
              {{ $t('finished.order.reserve') }}
            </UiButton>
            <UiButton
              v-if="offerOverReserve"
              variant="secondary"
              block
              :loading="reserving"
              @click="reserveStock(true)"
            >
              {{ $t('finished.order.reserveAnyway') }}
            </UiButton>
            <UiButton
              v-if="hasActiveReservation"
              variant="secondary"
              block
              :loading="reserving"
              @click="releaseStock"
            >
              {{ $t('finished.order.release') }}
            </UiButton>
          </template>
        </UiCard>

        <UiCard class="group">
          <h3>{{ $t('orders.payments') }}</h3>
          <p class="paid">
            {{ $t('orders.paidOf', { paid: money(order.amountPaid), total: money(order.totalAmount) }) }}
          </p>
          <ul
            v-if="order.payments.length > 0"
            class="line-list"
          >
            <li
              v-for="payment in order.payments"
              :key="payment.id"
            >
              <span class="line-main">
                <strong>{{ money(payment.amount) }}</strong>
                <span class="hint">
                  {{ day(payment.paidAt) }}
                  <template v-if="payment.method"> · {{ $t(`orders.methods.${payment.method}`) }}</template>
                  <template v-if="payment.note"> · {{ payment.note }}</template>
                </span>
              </span>
              <UiButton
                v-if="order.paymentsEditable"
                variant="ghost"
                :aria-label="$t('orders.removePayment', { amount: money(payment.amount) })"
                @click="removePayment(payment.id)"
              >
                {{ $t('orders.remove') }}
              </UiButton>
            </li>
          </ul>
          <UiButton
            v-if="order.paymentsEditable && order.amountDue > 0"
            variant="secondary"
            block
            @click="openPayment"
          >
            {{ $t('orders.recordPayment') }}
          </UiButton>
        </UiCard>

        <UiCard
          v-if="order.notes"
          class="group"
        >
          <h3>{{ $t('orders.fields.notes') }}</h3>
          <p class="notes">
            {{ order.notes }}
          </p>
        </UiCard>

        <UiButton
          v-if="order.headerEditable"
          variant="secondary"
          block
          @click="startEdit"
        >
          {{ $t('orders.edit') }}
        </UiButton>
        <UiButton
          v-if="canDelete"
          variant="ghost"
          block
          @click="confirmDelete = true"
        >
          {{ $t('orders.delete') }}
        </UiButton>
      </template>
    </template>

    <UiDialog
      v-model="showPayment"
      :title="$t('orders.recordPayment')"
    >
      <form
        class="dialog-form"
        novalidate
        @submit.prevent="savePayment"
      >
        <UiInput
          v-model="paymentAmount"
          :label="$t('orders.fields.paymentAmount')"
          inputmode="decimal"
          :error="paymentError"
        />
        <UiSelect
          v-model="paymentMethod"
          :label="$t('orders.fields.method')"
          :options="methodOptions"
        />
        <UiInput
          v-model="paymentNote"
          :label="$t('orders.fields.paymentNote')"
        />
        <UiButton
          type="submit"
          block
          :loading="busy"
        >
          {{ $t('orders.recordPayment') }}
        </UiButton>
      </form>
    </UiDialog>

    <UiDialog
      v-model="confirmCancel"
      :title="$t('orders.cancelTitle')"
    >
      <p>{{ $t('orders.cancelBody') }}</p>
      <template #actions>
        <UiButton
          variant="secondary"
          @click="confirmCancel = false"
        >
          {{ $t('orders.keep') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="busy"
          @click="move('Cancelled')"
        >
          {{ $t('orders.transitions.Cancelled') }}
        </UiButton>
      </template>
    </UiDialog>

    <UiDialog
      v-model="confirmDelete"
      :title="$t('orders.deleteTitle')"
    >
      <p>{{ $t('orders.deleteBody') }}</p>
      <template #actions>
        <UiButton
          variant="secondary"
          @click="confirmDelete = false"
        >
          {{ $t('orders.keep') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="busy"
          @click="removeOrder"
        >
          {{ $t('orders.delete') }}
        </UiButton>
      </template>
    </UiDialog>

    <UiDialog
      :model-value="rightsPrompt !== null"
      :title="$t('orders.rights.title')"
      @update:model-value="rightsPrompt = null"
    >
      <p>{{ $t('orders.rights.confirmBody') }}</p>
      <template #actions>
        <UiButton
          variant="secondary"
          @click="rightsPrompt = null"
        >
          {{ $t('orders.keep') }}
        </UiButton>
        <UiButton
          :loading="busy"
          @click="rightsPrompt && move(rightsPrompt, true)"
        >
          {{ $t('orders.rights.proceed') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>

<style scoped>
.stack, .dialog-form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.heading { margin: 0; font-size: var(--font-size-lg); }
h3 { margin: 0; font-size: var(--font-size-md); }
.head, .group, .progress { display: grid; gap: var(--space-3); }
.title-row { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); flex-wrap: wrap; }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-1); }
.facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: var(--space-3); margin: 0; }
.facts dt { color: var(--muted); font-size: var(--font-size-sm); }
.facts dd { margin: 0; font-weight: 600; overflow-wrap: anywhere; }
.facts a { color: var(--accent-text); }
.notice { margin: 0; min-height: 1.25em; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.steps { display: grid; grid-template-columns: repeat(5, 1fr); gap: var(--space-1); margin: 0; padding: 0; list-style: none; }
.steps li { display: grid; justify-items: center; gap: var(--space-1); position: relative; color: var(--muted); font-size: var(--font-size-sm); text-align: center; }
.steps li::before { content: ''; position: absolute; top: 7px; right: 50%; width: 100%; height: 2px; background: var(--border); }
.steps li:first-child::before { display: none; }
.steps .dot { position: relative; width: 16px; height: 16px; border-radius: 50%; background: var(--surface-2); border: 2px solid var(--border); }
.steps li.done, .steps li.current { color: var(--text); }
.steps li.done::before, .steps li.current::before { background: var(--accent); }
.steps li.done .dot { background: var(--accent); border-color: var(--accent); }
.steps li.current .dot { border-color: var(--accent); box-shadow: 0 0 0 4px var(--accent-soft); }
.steps li.current .step-label { font-weight: 700; }
.step-label { overflow-wrap: anywhere; hyphens: auto; }
.rights { display: grid; gap: var(--space-1); padding: var(--space-3); border: 1px solid var(--warning); border-radius: var(--radius-md); background: color-mix(in srgb, var(--warning) 12%, transparent); font-size: var(--font-size-sm); }
.actions { display: grid; gap: var(--space-2); }
.line-list { display: grid; gap: var(--space-3); margin: 0; padding: 0; list-style: none; }
.line-list li { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); }
.line-main { display: grid; gap: 2px; }
.line-warning { color: var(--warning); font-size: var(--font-size-sm); }
.amount { font-variant-numeric: tabular-nums; font-weight: 600; white-space: nowrap; }
.totals { display: grid; gap: var(--space-1); margin: 0; padding-top: var(--space-3); border-top: 1px solid var(--border); }
.totals div { display: flex; justify-content: space-between; gap: var(--space-3); }
.totals dt { color: var(--muted); }
.totals dd { margin: 0; font-variant-numeric: tabular-nums; }
.totals .grand { font-weight: 700; font-size: var(--font-size-lg); }
.totals .grand dt { color: var(--text); }
.totals .sub { font-size: var(--font-size-sm); }
.paid { margin: 0; font-weight: 600; }
.notes { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
