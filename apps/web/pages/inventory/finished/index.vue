<script setup lang="ts">
import { availabilityTone, filterFinishedStock } from '~/utils/finishedStock'
import type { FinishedStockList } from '~/utils/finishedStock'

definePageMeta({ titleKey: 'finished.title' })

const { n } = useI18n()
const auth = useAuthStore()

const data = ref<FinishedStockList | null>(null)
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const search = ref('')
const onlyLow = ref(false)

const visible = computed(() => filterFinishedStock(data.value?.items ?? [], search.value, onlyLow.value))
const currency = ref('EUR')
const money = (amount: number) => n(amount, { style: 'currency', currency: currency.value })

async function load() {
  status.value = 'loading'
  try {
    const [list, settings] = await Promise.all([
      auth.authorizedFetch<FinishedStockList>('/api/finished-stock'),
      auth.authorizedFetch<{ currency: string }>('/api/workspace/settings').catch(() => ({ currency: 'EUR' })),
    ])
    data.value = list
    currency.value = settings.currency
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
          :label="$t('finished.kpi.units')"
          :value="n(data.summary.totalUnits)"
        />
        <UiKpiTile
          :label="$t('finished.kpi.skus')"
          :value="n(data.summary.skusInStock)"
        />
        <UiKpiTile
          :label="$t('finished.kpi.available')"
          :value="n(data.summary.totalAvailable)"
        />
      </div>
      <p
        v-if="data.summary.stockValue > 0"
        class="meta"
      >
        {{ $t('finished.stockValue', { amount: money(data.summary.stockValue) }) }}
      </p>

      <StateEmpty
        v-if="data.items.length === 0"
        :message="$t('finished.empty')"
      />
      <template v-else>
        <div class="filters">
          <UiInput
            v-model="search"
            :label="$t('finished.search')"
            type="search"
          />
          <label class="toggle">
            <input
              v-model="onlyLow"
              type="checkbox"
            >
            {{ $t('finished.onlyLow', { count: data.summary.lowStockCount }) }}
          </label>
        </div>

        <StateEmpty
          v-if="visible.length === 0"
          :message="$t('finished.noMatches')"
        />
        <ul
          v-else
          class="list"
        >
          <li
            v-for="item in visible"
            :key="item.variantId"
          >
            <NuxtLink
              :to="`/inventory/finished/${item.variantId}`"
              class="card"
            >
              <span class="top">
                <span class="name">
                  <strong>{{ item.productName }} · {{ item.variantName }}</strong>
                  <span
                    v-if="item.sku"
                    class="meta"
                  >{{ item.sku }}</span>
                </span>
                <UiStatusChip
                  v-if="item.isLow"
                  status="warning"
                >
                  {{ $t('finished.low') }}
                </UiStatusChip>
              </span>
              <span class="chips">
                <UiStatusChip status="neutral">
                  {{ $t('finished.onHand', { count: item.onHand }) }}
                </UiStatusChip>
                <UiStatusChip :status="item.reserved > 0 ? 'info' : 'neutral'">
                  {{ $t('finished.reserved', { count: item.reserved }) }}
                </UiStatusChip>
                <UiStatusChip :status="availabilityTone(item)">
                  {{ $t('finished.availableCount', { count: item.available }) }}
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
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.filters { display: grid; gap: var(--space-2); }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
.list { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.card { display: grid; gap: var(--space-2); padding: var(--space-3) var(--space-4); color: var(--text); text-decoration: none; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.top { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-2); }
.name { display: grid; min-width: 0; overflow-wrap: anywhere; }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-2); }
</style>
