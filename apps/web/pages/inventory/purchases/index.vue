<script setup lang="ts">
import { purchaseStatuses, purchaseStatusTone } from '~/utils/purchases'
import type { PurchaseSummary } from '~/utils/purchases'

definePageMeta({ titleKey: 'purchases.title' })

const { t, n, d } = useI18n()
const auth = useAuthStore()

const purchases = ref<PurchaseSummary[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const filter = ref('')

const filterOptions = computed(() => [
  { value: '', label: t('purchases.allStatuses') },
  ...purchaseStatuses.map(value => ({ value, label: t(`purchases.status.${value}`) })),
])
const visible = computed(() => purchases.value.filter(purchase => !filter.value || purchase.status === filter.value))

async function load() {
  status.value = 'loading'
  try {
    purchases.value = await auth.authorizedFetch<PurchaseSummary[]>('/api/purchases')
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function title(purchase: PurchaseSummary) {
  return purchase.invoiceReference ? `${purchase.supplierName} · ${purchase.invoiceReference}` : purchase.supplierName
}

function subtitle(purchase: PurchaseSummary) {
  return t('purchases.rowSubtitle', {
    date: d(new Date(`${purchase.purchasedOn}T00:00:00`), { dateStyle: 'medium' }),
    total: n(purchase.total, { style: 'currency', currency: purchase.currency }),
    received: purchase.receivedQuantity,
    ordered: purchase.orderedQuantity,
  })
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
      <UiButton
        block
        @click="navigateTo('/inventory/purchases/new')"
      >
        {{ $t('purchases.add') }}
      </UiButton>

      <StateEmpty
        v-if="purchases.length === 0"
        :message="$t('purchases.empty')"
      />
      <template v-else>
        <UiSelect
          v-model="filter"
          :label="$t('purchases.statusFilter')"
          :options="filterOptions"
        />
        <StateEmpty
          v-if="visible.length === 0"
          :message="$t('purchases.noMatches')"
        />
        <UiListRow
          v-for="purchase in visible"
          :key="purchase.id"
          :to="`/inventory/purchases/${purchase.id}`"
          :title="title(purchase)"
          :subtitle="subtitle(purchase)"
        >
          <template #trailing>
            <UiStatusChip :status="purchaseStatusTone(purchase.status)">
              {{ $t(`purchases.status.${purchase.status}`) }}
            </UiStatusChip>
          </template>
        </UiListRow>
      </template>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
</style>
