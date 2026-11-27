<script setup lang="ts">
import type { Market, MarketSale, MarketPaymentMethod } from '~/utils/markets'
import { localDateKey, marketPaymentMethods } from '~/utils/markets'
import type { CartLine, DiscountMode, PosItem } from '~/utils/pos'
import {
  addToCart,
  buildCheckout,
  cartCount,
  cartSubtotal,
  changeFor,
  discountAmountFor,
  filterItems,
  hasUnclearedLines,
  newIdempotencyKey,
  parseQuantity,
  pendingQuantities,
  quickTenders,
  saleTotal,
  setQuantity,
  tenderedFor,
} from '~/utils/pos'
import { formatDecimal } from '~/utils/settingsForm'
import { localizeServerMessage } from '~/utils/apiError'

definePageMeta({ titleKey: 'pos.title' })

type Step = 'grid' | 'cart' | 'pay' | 'done'

const { n, t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const posQueue = usePosQueue()
const id = String(route.params.id)

const market = ref<Market | null>(null)
const items = ref<PosItem[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const step = ref<Step>('grid')
const search = ref('')
const cart = ref<CartLine[]>([])
const discountMode = ref<DiscountMode>('amount')
const discountText = ref('')
const method = ref<MarketPaymentMethod>('cash')
const tenderedText = ref('')
const acknowledged = ref(false)
const submitting = ref(false)
const problem = ref('')
const receipt = ref<{ sale: MarketSale | null, total: number, change: number, method: MarketPaymentMethod, queued: boolean } | null>(null)
const heading = ref<HTMLElement>()

const currency = computed(() => market.value?.currency ?? 'EUR')
const money = (amount: number) => n(amount, { style: 'currency', currency: currency.value })
const currencySymbol = computed(() => new Intl.NumberFormat('nl', { style: 'currency', currency: currency.value, currencyDisplay: 'narrowSymbol' }).formatToParts(0).find(part => part.type === 'currency')?.value ?? currency.value)

const reserved = computed(() => pendingQuantities(posQueue.queue.value, id))
const sellable = computed(() => items.value
  .map(item => ({ ...item, available: item.available - (reserved.value.get(item.variantId) ?? 0) }))
  .filter(item => item.available > 0 || cart.value.some(line => line.variantId === item.variantId)))
const visible = computed(() => filterItems(sellable.value, search.value))
const inCart = (variantId: string) => cart.value.find(line => line.variantId === variantId)?.quantity ?? 0

const count = computed(() => cartCount(cart.value))
const subtotal = computed(() => cartSubtotal(cart.value))
const discount = computed(() => discountAmountFor(discountMode.value, discountText.value, subtotal.value))
const total = computed(() => saleTotal(subtotal.value, discount.value.amount))
const tendered = computed(() => tenderedFor(tenderedText.value, total.value))
const change = computed(() => changeFor(total.value, tendered.value.value))
const needsAcknowledgement = computed(() => hasUnclearedLines(cart.value))
const waiting = computed(() => posQueue.queue.value.filter(entry => entry.marketId === id))
const waitingPending = computed(() => waiting.value.filter(entry => entry.state === 'pending'))
const waitingRejected = computed(() => waiting.value.filter(entry => entry.state === 'rejected'))
const canConfirm = computed(() => count.value > 0
  && discount.value.valid
  && (method.value !== 'cash' || (tendered.value.state !== 'short' && tendered.value.state !== 'invalid'))
  && (!needsAcknowledgement.value || acknowledged.value))

async function loadCatalogue() {
  items.value = await auth.authorizedFetch<PosItem[]>('/api/pos/catalogue')
}

async function load() {
  status.value = 'loading'
  try {
    const [found] = await Promise.all([auth.authorizedFetch<Market>(`/api/markets/${id}`), loadCatalogue()])
    market.value = found
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function refreshQuietly() {
  try {
    await loadCatalogue()
  }
  catch {
    // the register keeps working with the figures it has
  }
}

function go(next: Step) {
  step.value = next
  problem.value = ''
  nextTick(() => heading.value?.focus())
}

function tap(item: PosItem) {
  cart.value = addToCart(cart.value, item)
}

function editQuantity(variantId: string, text: string) {
  const value = parseQuantity(text)
  if (value !== null) cart.value = setQuantity(cart.value, variantId, value)
}

function bump(variantId: string, delta: number) {
  cart.value = setQuantity(cart.value, variantId, inCart(variantId) + delta)
  if (cart.value.length === 0) go('grid')
}

function setTender(amount: number) {
  tenderedText.value = formatDecimal(amount)
}

function reset() {
  cart.value = []
  discountText.value = ''
  discountMode.value = 'amount'
  method.value = 'cash'
  tenderedText.value = ''
  acknowledged.value = false
  search.value = ''
  receipt.value = null
  go('grid')
}

function describe(fieldErrors: Record<string, string[]>, status: number, title: string): string {
  if (status === 409) return t('pos.errors.closed')
  if (fieldErrors.commercialRights) return t('pos.errors.rights')
  const first = Object.values(fieldErrors)[0]?.[0]
  const local = first ? localizeServerMessage(first, t) : undefined
  return local ?? t(first || title ? 'pos.errors.rejected' : 'pos.errors.generic')
}

async function confirm() {
  if (!canConfirm.value || submitting.value) return
  submitting.value = true
  problem.value = ''
  const request = buildCheckout({
    key: newIdempotencyKey(),
    cart: cart.value,
    discountAmount: discount.value.amount,
    method: method.value,
    tendered: method.value === 'cash' ? tendered.value.value : null,
    acknowledgeRisk: acknowledged.value,
    now: new Date(),
    businessDate: localDateKey(new Date()),
  })
  const outcome = await posQueue.submit(id, request, total.value)
  submitting.value = false

  if (outcome.kind === 'rejected') {
    problem.value = describe(outcome.fieldErrors, outcome.status, outcome.title)
    await refreshQuietly()
    cart.value = cart.value.flatMap((line) => {
      const fresh = items.value.find(item => item.variantId === line.variantId)
      return fresh ? setQuantity([{ ...line, available: fresh.available - (reserved.value.get(line.variantId) ?? 0) }], line.variantId, line.quantity) : []
    })
    if (cart.value.length === 0) go('grid')
    return
  }

  receipt.value = {
    sale: outcome.kind === 'done' ? outcome.sale : null,
    total: total.value,
    change: method.value === 'cash' ? change.value : 0,
    method: method.value,
    queued: outcome.kind === 'queued',
  }
  go('done')
  if (outcome.kind === 'done') await refreshQuietly()
}

async function syncWaiting() {
  const synced = await posQueue.flush()
  if (synced > 0) await refreshQuietly()
}

let timer: ReturnType<typeof setInterval> | undefined

function onVisible() {
  if (document.visibilityState === 'visible') {
    refreshQuietly()
    syncWaiting()
  }
}

onMounted(() => {
  posQueue.hydrate()
  load().then(syncWaiting)
  window.addEventListener('online', syncWaiting)
  document.addEventListener('visibilitychange', onVisible)
  timer = setInterval(() => {
    if (waitingPending.value.length > 0) syncWaiting()
  }, 15_000)
})

onBeforeUnmount(() => {
  window.removeEventListener('online', syncWaiting)
  document.removeEventListener('visibilitychange', onVisible)
  if (timer) clearInterval(timer)
})
</script>

<template>
  <div class="register">
    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed' || !market"
      @retry="load"
    />

    <div
      v-else-if="market.status === 'Closed'"
      class="closed"
    >
      <p>{{ $t('pos.closedMarket') }}</p>
      <NuxtLink
        :to="`/markets/${id}`"
        class="back"
      >
        {{ $t('markets.backToMarket') }}
      </NuxtLink>
    </div>

    <template v-else>
      <header class="bar">
        <NuxtLink
          :to="`/markets/${id}`"
          class="back"
        >
          {{ market.name }}
        </NuxtLink>
        <button
          v-if="waitingPending.length > 0"
          type="button"
          class="pending"
          :aria-label="$t('pos.pendingAction')"
          @click="syncWaiting"
        >
          <span
            class="pulse"
            aria-hidden="true"
          />
          {{ $t('pos.pending', { count: waitingPending.length }, waitingPending.length) }}
        </button>
      </header>
      <p
        v-for="rejected in waitingRejected"
        :key="rejected.key"
        class="rejected"
        role="alert"
      >
        {{ $t('pos.rejected', { amount: money(rejected.total) }) }}
        <button
          type="button"
          @click="posQueue.discard(rejected.key)"
        >
          {{ $t('pos.dismiss') }}
        </button>
      </p>

      <section
        v-if="step === 'grid'"
        class="panel"
      >
        <h2
          ref="heading"
          tabindex="-1"
          class="sr-only"
        >
          {{ $t('pos.chooseProducts') }}
        </h2>
        <UiInput
          v-if="sellable.length > 8"
          v-model="search"
          type="search"
          :label="$t('pos.search')"
        />
        <StateEmpty
          v-if="sellable.length === 0"
          :message="$t('pos.noStock')"
        />
        <StateEmpty
          v-else-if="visible.length === 0"
          :message="$t('pos.noMatches')"
        />
        <ul
          v-else
          class="grid"
        >
          <li
            v-for="item in visible"
            :key="item.variantId"
          >
            <button
              type="button"
              class="tile"
              :class="{ picked: inCart(item.variantId) > 0 }"
              :aria-disabled="inCart(item.variantId) >= item.available || undefined"
              :aria-label="$t('pos.addAria', { name: `${item.productName} ${item.variantName}`, price: money(item.price), count: item.available })"
              @click="tap(item)"
            >
              <span class="name">{{ item.productName }}</span>
              <span class="variant">{{ item.variantName }}</span>
              <span class="price">{{ money(item.price) }}</span>
              <span class="stock">{{ $t('pos.left', { count: item.available - inCart(item.variantId) }) }}</span>
              <span
                v-if="item.commercialRightsStatus === 'NotCleared'"
                class="warn"
              >{{ $t('pos.rightsShort') }}</span>
              <span
                v-if="inCart(item.variantId) > 0"
                class="badge"
                aria-hidden="true"
              >{{ inCart(item.variantId) }}</span>
            </button>
          </li>
        </ul>

        <div
          v-if="count > 0"
          class="cartbar"
        >
          <button
            type="button"
            class="cartbtn"
            @click="go('cart')"
          >
            <span>{{ $t('pos.cartSummary', { count }, count) }}</span>
            <strong>{{ money(subtotal) }}</strong>
            <span class="go">{{ $t('pos.review') }}</span>
          </button>
        </div>
      </section>

      <section
        v-else-if="step === 'cart'"
        class="panel"
      >
        <h2
          ref="heading"
          tabindex="-1"
        >
          {{ $t('pos.cart') }}
        </h2>
        <ul class="lines">
          <li
            v-for="line in cart"
            :key="line.variantId"
            class="line"
          >
            <div class="what">
              <strong>{{ line.name }}</strong>
              <span class="muted">{{ money(line.price) }} · {{ money(line.price * line.quantity) }}</span>
            </div>
            <div
              class="stepper"
              role="group"
              :aria-label="line.name"
            >
              <button
                type="button"
                :aria-label="$t('pos.less')"
                @click="bump(line.variantId, -1)"
              >
                −
              </button>
              <input
                :value="line.quantity"
                inputmode="numeric"
                :aria-label="$t('pos.quantity')"
                @change="editQuantity(line.variantId, ($event.target as HTMLInputElement).value)"
              >
              <button
                type="button"
                :aria-label="$t('pos.more')"
                :disabled="line.quantity >= line.available"
                @click="bump(line.variantId, 1)"
              >
                +
              </button>
            </div>
          </li>
        </ul>

        <div class="discount">
          <UiSegmentedControl
            v-model="discountMode"
            :label="$t('pos.discountKind')"
            :options="[{ value: 'amount', label: currencySymbol }, { value: 'percent', label: '%' }]"
          />
          <UiInput
            v-model="discountText"
            inputmode="decimal"
            :label="$t('pos.discount')"
            :error="discount.valid ? undefined : $t('pos.errors.discount')"
          />
        </div>

        <dl class="sum">
          <div>
            <dt>{{ $t('pos.subtotal') }}</dt>
            <dd>{{ money(subtotal) }}</dd>
          </div>
          <div v-if="discount.amount > 0">
            <dt>{{ $t('pos.discount') }}</dt>
            <dd>-{{ money(discount.amount) }}</dd>
          </div>
          <div class="grand">
            <dt>{{ $t('pos.total') }}</dt>
            <dd>{{ money(total) }}</dd>
          </div>
        </dl>

        <div class="nav">
          <UiButton
            variant="secondary"
            @click="go('grid')"
          >
            {{ $t('pos.moreProducts') }}
          </UiButton>
          <UiButton
            :disabled="!discount.valid"
            @click="go('pay')"
          >
            {{ $t('pos.pay') }}
          </UiButton>
        </div>
      </section>

      <section
        v-else-if="step === 'pay'"
        class="panel"
      >
        <h2
          ref="heading"
          tabindex="-1"
        >
          {{ $t('pos.payTitle') }}
        </h2>
        <p
          class="bigtotal"
          aria-live="polite"
        >
          {{ money(total) }}
        </p>

        <div
          class="methods"
          role="radiogroup"
          :aria-label="$t('pos.method')"
        >
          <label
            v-for="option in marketPaymentMethods"
            :key="option"
            class="method"
            :class="{ active: method === option }"
          >
            <input
              v-model="method"
              type="radio"
              name="method"
              :value="option"
            >
            <span>{{ $t(`markets.method.${option}`) }}</span>
          </label>
        </div>

        <template v-if="method === 'cash'">
          <div class="quick">
            <button
              v-for="amount in quickTenders(total)"
              :key="amount"
              type="button"
              @click="setTender(amount)"
            >
              {{ money(amount) }}
            </button>
          </div>
          <UiInput
            v-model="tenderedText"
            inputmode="decimal"
            :label="$t('pos.tendered')"
            :hint="$t('pos.tenderedHint')"
            :error="tendered.state === 'short' ? $t('pos.errors.short') : tendered.state === 'invalid' ? $t('pos.errors.amount') : undefined"
          />
          <p
            class="change"
            role="status"
          >
            {{ $t('pos.change') }}: <strong>{{ money(change) }}</strong>
          </p>
        </template>

        <label
          v-if="needsAcknowledgement"
          class="ack"
        >
          <input
            v-model="acknowledged"
            type="checkbox"
          >
          <span>{{ $t('pos.acknowledge') }}</span>
        </label>

        <p
          v-if="problem"
          class="error"
          role="alert"
        >
          {{ problem }}
        </p>

        <div class="nav">
          <UiButton
            variant="secondary"
            :disabled="submitting"
            @click="go('cart')"
          >
            {{ $t('pos.back') }}
          </UiButton>
          <UiButton
            :loading="submitting"
            :disabled="!canConfirm"
            @click="confirm"
          >
            {{ $t('pos.confirm') }}
          </UiButton>
        </div>
      </section>

      <section
        v-else-if="receipt"
        class="panel done"
      >
        <div
          class="success"
          role="status"
        >
          <span
            class="tick"
            aria-hidden="true"
          >✓</span>
          <h2
            ref="heading"
            tabindex="-1"
          >
            {{ receipt.queued ? $t('pos.savedTitle') : $t('pos.doneTitle') }}
          </h2>
          <p
            v-if="receipt.sale"
            class="muted"
          >
            {{ $t('pos.receipt', { number: receipt.sale.receiptNumber }) }}
          </p>
          <p
            v-else
            class="muted"
          >
            {{ $t('pos.savedHelp') }}
          </p>
          <p class="bigtotal">
            {{ money(receipt.total) }}
          </p>
          <p
            v-if="receipt.method === 'cash'"
            class="change"
          >
            {{ $t('pos.change') }}: <strong>{{ money(receipt.change) }}</strong>
          </p>
          <p
            v-else
            class="muted"
          >
            {{ $t(`markets.method.${receipt.method}`) }}
          </p>
        </div>
        <UiButton
          block
          class="next"
          @click="reset"
        >
          {{ $t('pos.newSale') }}
        </UiButton>
      </section>
    </template>
  </div>
</template>

<style scoped>
.register { display: grid; gap: var(--space-3); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
h2 { margin: 0; font-size: var(--font-size-lg); }
h2:focus { outline: none; }
.muted { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.bar { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; overflow-wrap: anywhere; }
.closed { display: grid; gap: var(--space-3); justify-items: start; }
.pending { display: inline-flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); padding: 0 var(--space-4); color: var(--text); font: inherit; font-size: var(--font-size-sm); font-weight: 600; background: color-mix(in srgb, var(--warning) 22%, transparent); border: 1px solid var(--warning); border-radius: var(--radius-pill); cursor: pointer; }
.pulse { width: 8px; height: 8px; background: var(--warning); border-radius: 50%; animation: pulse 1.4s ease-in-out infinite; }
@keyframes pulse { 50% { opacity: 0.3; } }
@media (prefers-reduced-motion: reduce) { .pulse { animation: none; } }
.rejected { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); margin: 0; padding: var(--space-3) var(--space-4); color: var(--text); font-size: var(--font-size-sm); background: color-mix(in srgb, var(--danger) 16%, transparent); border: 1px solid var(--danger); border-radius: var(--radius-md); }
.rejected button { min-height: var(--tap-size); padding: 0 var(--space-3); color: var(--text); font: inherit; font-weight: 600; background: transparent; border: 0; cursor: pointer; }
.panel { display: grid; gap: var(--space-3); }

.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.tile { position: relative; display: grid; align-content: start; gap: 2px; width: 100%; min-height: 104px; padding: var(--space-3); color: var(--text); font: inherit; text-align: left; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); cursor: pointer; -webkit-tap-highlight-color: transparent; touch-action: manipulation; transition: transform 0.08s ease-out, background 0.15s; }
.tile:active { transform: scale(0.97); }
.tile.picked { background: var(--accent-soft); border-color: var(--accent); }
.tile[aria-disabled='true'] { opacity: 0.55; }
.tile:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.tile .name { font-weight: 700; overflow-wrap: anywhere; }
.tile .variant { color: var(--muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
.tile .price { margin-top: auto; font-size: var(--font-size-lg); font-weight: 700; font-variant-numeric: tabular-nums; }
.tile .stock { color: var(--muted); font-size: var(--font-size-sm); }
.tile .warn { color: var(--warning); font-size: var(--font-size-sm); font-weight: 600; }
.badge { position: absolute; top: var(--space-2); right: var(--space-2); display: grid; place-items: center; min-width: 28px; height: 28px; padding: 0 6px; color: var(--accent-contrast); font-weight: 800; background: var(--accent); border-radius: var(--radius-pill); }
@media (prefers-reduced-motion: reduce) { .tile { transition: none; } .tile:active { transform: none; } }

.cartbar { position: sticky; bottom: calc(72px + env(safe-area-inset-bottom)); z-index: 5; }
.cartbtn { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); width: 100%; min-height: 60px; padding: 0 var(--space-4); color: var(--accent-contrast); font: inherit; background: var(--accent); border: 0; border-radius: var(--radius-pill); box-shadow: var(--shadow); cursor: pointer; }
.cartbtn strong { font-size: var(--font-size-lg); font-variant-numeric: tabular-nums; }
.cartbtn .go { font-weight: 700; }
.cartbtn:focus-visible { outline: none; box-shadow: var(--focus-ring), var(--shadow); }
@media (min-width: 768px) { .cartbar { bottom: var(--space-3); } }

.lines { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.line { display: grid; gap: var(--space-2); padding: var(--space-3) var(--space-4); background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.what { display: grid; overflow-wrap: anywhere; }
.stepper { display: grid; grid-template-columns: 56px 1fr 56px; gap: var(--space-2); }
.stepper button, .stepper input { min-height: 52px; color: var(--text); font: inherit; font-size: var(--font-size-lg); font-weight: 700; text-align: center; background: var(--surface-2); border: 1px solid var(--border); border-radius: var(--radius-md); }
.stepper button { cursor: pointer; touch-action: manipulation; }
.stepper button:disabled { opacity: 0.4; cursor: not-allowed; }
.stepper input { width: 100%; min-width: 0; }
.stepper button:focus-visible, .stepper input:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.discount { display: grid; gap: var(--space-2); }
.sum { display: grid; gap: var(--space-1); margin: 0; }
.sum div { display: flex; justify-content: space-between; gap: var(--space-3); }
.sum dt { color: var(--muted); }
.sum dd { margin: 0; font-variant-numeric: tabular-nums; }
.sum .grand { padding-top: var(--space-2); border-top: 1px solid var(--border); font-size: var(--font-size-lg); font-weight: 800; }
.sum .grand dt { color: var(--text); }
.nav { display: grid; grid-template-columns: 1fr 1.4fr; gap: var(--space-2); }
.nav :deep(.btn) { min-height: 56px; }

.bigtotal { margin: 0; font-size: 2.5rem; font-weight: 800; line-height: 1.1; font-variant-numeric: tabular-nums; }
.methods { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-2); }
.method { position: relative; display: grid; place-items: center; min-height: 60px; font-weight: 700; background: var(--surface-2); border: 1px solid var(--border); border-radius: var(--radius-md); cursor: pointer; }
.method input { position: absolute; inset: 0; opacity: 0; margin: 0; cursor: pointer; }
.method.active { color: var(--accent-contrast); background: var(--accent); border-color: var(--accent); }
.method:has(input:focus-visible) { box-shadow: var(--focus-ring); }
.quick { display: grid; grid-template-columns: repeat(auto-fit, minmax(90px, 1fr)); gap: var(--space-2); }
.quick button { min-height: 52px; color: var(--text); font: inherit; font-weight: 700; font-variant-numeric: tabular-nums; background: var(--surface-2); border: 1px solid var(--border); border-radius: var(--radius-md); cursor: pointer; touch-action: manipulation; }
.quick button:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.change { margin: 0; font-size: var(--font-size-lg); }
.change strong { font-size: 1.75rem; font-variant-numeric: tabular-nums; }
.ack { display: flex; align-items: flex-start; gap: var(--space-3); min-height: var(--tap-size); padding: var(--space-3); background: color-mix(in srgb, var(--warning) 14%, transparent); border: 1px solid var(--warning); border-radius: var(--radius-md); font-size: var(--font-size-sm); }
.ack input { flex: none; width: 24px; height: 24px; accent-color: var(--accent); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }

.done { align-content: start; }
.success { display: grid; justify-items: center; gap: var(--space-2); padding: var(--space-5) var(--space-3); text-align: center; background: var(--accent-soft); border: 1px solid var(--border); border-radius: var(--radius-lg); }
.tick { display: grid; place-items: center; width: 64px; height: 64px; color: var(--accent-contrast); font-size: 2rem; font-weight: 800; background: var(--accent); border-radius: 50%; }
.next { min-height: 60px; }
</style>
