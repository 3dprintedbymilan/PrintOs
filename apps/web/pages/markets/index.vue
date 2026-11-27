<script setup lang="ts">
import type { Market } from '~/utils/markets'
import { formatDateRange, marketTone } from '~/utils/markets'

definePageMeta({ titleKey: 'markets.title' })

const { n, d } = useI18n()
const auth = useAuthStore()

const markets = ref<Market[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

const open = computed(() => markets.value.filter(market => market.status === 'Open'))
const closed = computed(() => markets.value.filter(market => market.status === 'Closed'))

const dateText = (value: string) => d(new Date(`${value}T12:00:00`), { dateStyle: 'medium' })
const money = (market: Market, amount: number) => n(amount, { style: 'currency', currency: market.currency })

async function load() {
  status.value = 'loading'
  try {
    markets.value = await auth.authorizedFetch<Market[]>('/api/markets')
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
    <UiButton
      block
      @click="navigateTo('/markets/new')"
    >
      {{ $t('markets.add') }}
    </UiButton>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <StateEmpty
      v-else-if="markets.length === 0"
      :message="$t('markets.empty')"
    />

    <template v-else>
      <section
        v-for="group in [{ key: 'open', items: open }, { key: 'closed', items: closed }].filter(entry => entry.items.length > 0)"
        :key="group.key"
        class="group"
      >
        <h2>{{ $t(`markets.group.${group.key}`) }}</h2>
        <ul class="list">
          <li
            v-for="market in group.items"
            :key="market.id"
            class="card"
          >
            <NuxtLink
              :to="`/markets/${market.id}`"
              class="info"
            >
              <span class="top">
                <strong>{{ market.name }}</strong>
                <UiStatusChip :status="marketTone(market.status)">
                  {{ $t(`markets.status.${market.status}`) }}
                </UiStatusChip>
              </span>
              <span class="meta">
                {{ formatDateRange(market.startDate, market.endDate, dateText) }}<template v-if="market.location"> · {{ market.location }}</template>
              </span>
              <span class="meta">
                {{ $t('markets.salesCount', { count: market.saleCount }, market.saleCount) }} · {{ money(market, market.revenue) }}
              </span>
            </NuxtLink>
            <NuxtLink
              v-if="market.status === 'Open'"
              :to="`/markets/${market.id}/pos`"
              class="register"
            >
              {{ $t('markets.openRegister') }}
            </NuxtLink>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>

<style scoped>
.stack, .group { display: grid; gap: var(--space-3); }
h2 { margin: 0; color: var(--muted); font-size: var(--font-size-sm); font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em; }
.list { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.card { display: grid; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.info { display: grid; gap: var(--space-1); color: var(--text); text-decoration: none; }
.top { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-2); overflow-wrap: anywhere; }
.meta { color: var(--muted); font-size: var(--font-size-sm); }
.register { display: grid; place-items: center; min-height: 52px; color: var(--accent-contrast); font-weight: 700; text-decoration: none; background: var(--accent); border-radius: var(--radius-pill); }
</style>
