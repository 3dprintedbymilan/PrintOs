<script setup lang="ts">
import type { OrderSummary } from '~/utils/orders'
import { orderLabel, orderPaymentTone, orderStatusTone } from '~/utils/orders'

definePageMeta({ titleKey: 'orders.title' })

const { t, n, d } = useI18n()
const auth = useAuthStore()

type FilterKey = 'active' | 'inProduction' | 'ready' | 'unpaid' | 'delivered' | 'all'
const filters: { key: FilterKey, query: Record<string, string> }[] = [
  { key: 'active', query: { status: 'active' } },
  { key: 'inProduction', query: { status: 'InProduction' } },
  { key: 'ready', query: { status: 'Ready' } },
  { key: 'unpaid', query: { status: 'active,Delivered', payment: 'open' } },
  { key: 'delivered', query: { status: 'Delivered' } },
  { key: 'all', query: {} },
]

const filter = ref<FilterKey>('active')
const search = ref('')
const orders = ref<OrderSummary[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

async function load() {
  status.value = 'loading'
  try {
    const selected = filters.find(candidate => candidate.key === filter.value)
    orders.value = await auth.authorizedFetch<OrderSummary[]>('/api/orders', {
      query: { ...selected?.query, q: search.value.trim() || undefined },
    })
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function money(order: OrderSummary, amount: number) {
  return n(amount, { style: 'currency', currency: order.currency })
}

function subtitle(order: OrderSummary) {
  const parts = [t('orders.items', { count: order.itemCount }, order.itemCount)]
  if (order.dueDate) parts.push(t('orders.dueOn', { date: d(new Date(`${order.dueDate}T00:00:00`), { dateStyle: 'medium' }) }))
  return parts.join(' · ')
}

let timer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 250)
})
watch(filter, load)
onMounted(load)
onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/business"
      class="back"
    >
      {{ $t('settings.back') }}
    </NuxtLink>

    <div
      class="filters"
      role="group"
      :aria-label="$t('orders.filters.label')"
    >
      <button
        v-for="option in filters"
        :key="option.key"
        type="button"
        class="filter"
        :aria-pressed="filter === option.key"
        @click="filter = option.key"
      >
        {{ $t(`orders.filters.${option.key}`) }}
      </button>
    </div>

    <UiInput
      v-model="search"
      type="search"
      :label="$t('orders.search')"
      :placeholder="$t('orders.searchPlaceholder')"
    />

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <StateEmpty
        v-if="orders.length === 0"
        :message="search.trim() || filter !== 'all' ? $t('orders.noResults') : $t('orders.empty')"
      />
      <NuxtLink
        v-for="order in orders"
        :key="order.id"
        :to="`/orders/${order.id}`"
        class="order"
      >
        <span class="top">
          <strong class="name">{{ orderLabel(order.number) }} · {{ order.customerName ?? $t('orders.noCustomer') }}</strong>
          <strong class="amount">{{ money(order, order.totalAmount) }}</strong>
        </span>
        <span class="sub">{{ subtitle(order) }}</span>
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
      </NuxtLink>
    </template>

    <UiButton
      block
      @click="navigateTo('/orders/new')"
    >
      {{ $t('orders.add') }}
    </UiButton>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.filters { display: flex; gap: var(--space-2); overflow-x: auto; padding-bottom: var(--space-1); scrollbar-width: none; }
.filter { flex: none; min-height: var(--tap-size); padding: 0 var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-pill); font: inherit; font-weight: 600; cursor: pointer; }
.filter[aria-pressed='true'] { background: var(--accent); color: var(--accent-contrast); border-color: var(--accent); }
.filter:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.order { display: grid; gap: var(--space-2); padding: var(--space-3) var(--space-4); color: var(--text); text-decoration: none; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.order:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.top { display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-3); }
.name { overflow-wrap: anywhere; }
.sub { color: var(--muted); font-size: var(--font-size-sm); }
.amount { font-variant-numeric: tabular-nums; white-space: nowrap; }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-1); }
</style>
