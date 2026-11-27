<script setup lang="ts">
import { changeTone, groupBySupplier } from '~/utils/purchases'
import type { MaterialPriceHistory } from '~/utils/purchases'

definePageMeta({ titleKey: 'prices.title' })

const { t, n, d } = useI18n()
const auth = useAuthStore()

const histories = ref<MaterialPriceHistory[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const view = ref<'materials' | 'suppliers'>('materials')

const viewOptions = computed(() => [
  { value: 'materials', label: t('prices.byMaterial') },
  { value: 'suppliers', label: t('prices.bySupplier') },
])
const groups = computed(() => groupBySupplier(histories.value))

async function load() {
  status.value = 'loading'
  try {
    histories.value = await auth.authorizedFetch<MaterialPriceHistory[]>('/api/price-history')
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function money(amount: number, currency: string) {
  return n(amount, { style: 'currency', currency })
}

function change(percent: number | null) {
  if (percent === null) return t('prices.noChange')
  if (percent === 0) return t('prices.unchanged')
  const text = n(Math.abs(percent), { maximumFractionDigits: 1 })
  return percent > 0 ? t('prices.up', { percent: text }) : t('prices.down', { percent: text })
}

function day(value: string) {
  return d(new Date(`${value}T00:00:00`), { dateStyle: 'medium' })
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
    <template v-else>
      <p class="intro">
        {{ $t('prices.intro') }}
      </p>
      <StateEmpty
        v-if="histories.length === 0"
        :message="$t('prices.empty')"
      />
      <template v-else>
        <UiSegmentedControl
          v-model="view"
          :label="$t('prices.view')"
          :options="viewOptions"
        />
        <template v-if="view === 'materials'">
          <PriceTrend
            v-for="history in histories"
            :key="history.materialId"
            :history="history"
          />
        </template>
        <template v-else>
          <UiCard
            v-for="group in groups"
            :key="group.supplierId"
            class="group"
          >
            <h2>{{ group.supplierName }}</h2>
            <ul class="rows">
              <li
                v-for="row in group.rows"
                :key="row.materialId"
              >
                <span class="name">
                  {{ row.materialName }}
                  <UiStatusChip
                    v-if="row.price.isCheapest && histories.find(history => history.materialId === row.materialId)!.suppliers.length > 1"
                    status="success"
                  >{{ $t('prices.cheapest') }}</UiStatusChip>
                </span>
                <span class="price">{{ $t('prices.perKg', { price: money(row.price.lastCostPerKg, row.currency) }) }}</span>
                <span class="meta">{{ day(row.price.lastPurchasedOn) }}</span>
                <span
                  class="change"
                  :class="changeTone(row.price.changePercent)"
                >{{ change(row.price.changePercent) }}</span>
              </li>
            </ul>
          </UiCard>
        </template>
      </template>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.intro { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.group { display: grid; gap: var(--space-2); }
h2 { margin: 0; font-size: var(--font-size-md); }
.rows { list-style: none; margin: 0; padding: 0; display: grid; gap: 0; }
.rows li { display: grid; grid-template-columns: 1fr auto; gap: 0 var(--space-3); padding: var(--space-2) 0; border-top: 1px solid var(--border); }
.name { display: flex; align-items: center; gap: var(--space-2); flex-wrap: wrap; font-weight: 600; }
.price { text-align: right; font-variant-numeric: tabular-nums; }
.meta, .change { color: var(--muted); font-size: var(--font-size-sm); }
.change { text-align: right; font-variant-numeric: tabular-nums; }
.change.up { color: var(--danger); }
.change.down { color: var(--accent); }
</style>
