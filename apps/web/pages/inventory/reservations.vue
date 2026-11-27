<script setup lang="ts">
import { filterReservations } from '~/utils/stockCount'
import type { ReservationOverview } from '~/utils/stockCount'

definePageMeta({ titleKey: 'stockTools.reservations.title' })

const { n, d } = useI18n()
const auth = useAuthStore()

const data = ref<ReservationOverview | null>(null)
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const search = ref('')
const shortOnly = ref(false)

const visible = computed(() => filterReservations(data.value?.items ?? [], search.value, shortOnly.value))

async function load() {
  status.value = 'loading'
  try {
    data.value = await auth.authorizedFetch<ReservationOverview>('/api/finished-stock/reservations')
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
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
      {{ $t('finished.backToInventory') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <template v-else-if="data">
      <div class="kpis">
        <UiKpiTile
          :label="$t('stockTools.reservations.kpi.units')"
          :value="n(data.summary.reservedUnits)"
        />
        <UiKpiTile
          :label="$t('stockTools.reservations.kpi.orders')"
          :value="n(data.summary.orders)"
        />
        <UiKpiTile
          :label="$t('stockTools.reservations.kpi.short')"
          :value="n(data.summary.shortUnits)"
        />
      </div>

      <StateEmpty
        v-if="data.items.length === 0"
        :message="$t('stockTools.reservations.empty')"
      />
      <template v-else>
        <div class="filters">
          <UiInput
            v-model="search"
            :label="$t('stockTools.reservations.search')"
            type="search"
          />
          <label class="toggle">
            <input
              v-model="shortOnly"
              type="checkbox"
            >
            {{ $t('stockTools.reservations.onlyShort', { count: data.summary.shortLines }) }}
          </label>
        </div>

        <StateEmpty
          v-if="visible.length === 0"
          :message="$t('stockTools.reservations.noMatches')"
        />
        <ul
          v-else
          class="list"
        >
          <li
            v-for="item in visible"
            :key="item.orderLineId"
          >
            <NuxtLink
              :to="`/orders/${item.orderId}`"
              class="card"
            >
              <span class="top">
                <span class="name">
                  <strong>{{ item.productName }} · {{ item.variantName }}</strong>
                  <span class="meta">
                    {{ $t('finished.orderNumber', { number: item.orderNumber }) }} · {{ item.customerName ?? $t('orders.noCustomer') }}
                  </span>
                  <span
                    v-if="item.dueDate"
                    class="meta"
                  >{{ $t('orders.dueOn', { date: d(new Date(`${item.dueDate}T00:00:00`), { dateStyle: 'medium' }) }) }}</span>
                </span>
                <span class="qty">{{ $t('finished.units', { count: item.quantity }) }}</span>
              </span>
              <span class="chips">
                <UiStatusChip status="info">
                  {{ $t(`orders.status.${item.orderStatus}`) }}
                </UiStatusChip>
                <UiStatusChip :status="item.isShort ? 'danger' : 'neutral'">
                  {{ item.isShort ? $t('finished.shortage', { count: item.reserved - item.onHand }) : $t('finished.availableCount', { count: item.available }) }}
                </UiStatusChip>
              </span>
            </NuxtLink>
          </li>
        </ul>
      </template>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.kpis { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-2); }
.meta { color: var(--muted); font-size: var(--font-size-sm); }
.filters { display: grid; gap: var(--space-2); }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
.list { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.card { display: grid; gap: var(--space-2); padding: var(--space-3) var(--space-4); color: var(--text); text-decoration: none; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.top { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-2); }
.name { display: grid; min-width: 0; overflow-wrap: anywhere; }
.qty { font-weight: 600; white-space: nowrap; font-variant-numeric: tabular-nums; }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-2); }
</style>
