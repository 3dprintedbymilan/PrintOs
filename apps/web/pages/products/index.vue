<script setup lang="ts">
import { priceRange } from '~/utils/products'
import type { ProductSummary } from '~/utils/products'

definePageMeta({ titleKey: 'products.title' })

const { t, n } = useI18n()
const auth = useAuthStore()

const products = ref<ProductSummary[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const search = ref('')
const showArchived = ref(false)

let requestCounter = 0

async function load() {
  const current = ++requestCounter
  status.value = 'loading'
  try {
    const result = await auth.authorizedFetch<ProductSummary[]>('/api/products', {
      query: { search: search.value.trim() || undefined, includeArchived: showArchived.value },
    })
    if (current !== requestCounter) return
    products.value = result
    status.value = 'ready'
  }
  catch {
    if (current === requestCounter) status.value = 'failed'
  }
}

function money(amount: number, currency: string) {
  return n(amount, { style: 'currency', currency })
}

function subtitle(product: ProductSummary) {
  const parts: string[] = []
  if (product.sku) parts.push(product.sku)
  parts.push(t('products.variantCount', { count: product.variantCount }, product.variantCount))
  const range = priceRange(product)
  if (range) parts.push(range.low === range.high ? money(range.low, product.currency) : `${money(range.low, product.currency)} – ${money(range.high, product.currency)}`)
  return parts.join(' · ')
}

let timer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 250)
})
watch(showArchived, load)
onBeforeUnmount(() => clearTimeout(timer))
onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/business"
      class="back"
    >
      {{ $t('settings.back') }}
    </NuxtLink>

    <UiInput
      v-model="search"
      type="search"
      :label="$t('products.search')"
      :placeholder="$t('products.searchPlaceholder')"
      autocomplete="off"
    />

    <label class="toggle">
      <input
        v-model="showArchived"
        type="checkbox"
      >
      {{ $t('products.showArchived') }}
    </label>

    <StateLoading v-if="status === 'loading' && products.length === 0" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <StateEmpty
        v-if="products.length === 0"
        :message="search.trim() ? $t('products.noResults') : $t('products.empty')"
      />
      <ul
        v-else
        class="list"
        :aria-label="$t('products.title')"
        :aria-busy="status === 'loading'"
      >
        <li
          v-for="product in products"
          :key="product.id"
        >
          <UiListRow
            :to="`/products/${product.id}`"
            :title="product.name"
            :subtitle="subtitle(product)"
          >
            <template
              v-if="product.isArchived || product.commercialRightsStatus === 'NotCleared'"
              #trailing
            >
              <span class="chips">
                <UiStatusChip
                  v-if="product.commercialRightsStatus === 'NotCleared'"
                  status="danger"
                >
                  {{ $t('rights.listNotCleared') }}
                </UiStatusChip>
                <UiStatusChip
                  v-if="product.isArchived"
                  status="neutral"
                >
                  {{ $t('products.archived') }}
                </UiStatusChip>
              </span>
            </template>
          </UiListRow>
        </li>
      </ul>
    </template>

    <UiButton
      block
      @click="navigateTo('/products/new')"
    >
      {{ $t('products.add') }}
    </UiButton>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.chips { display: grid; justify-items: end; gap: var(--space-1); }
.chips :deep(.chip) { white-space: nowrap; }
.list { display: grid; gap: var(--space-3); margin: 0; padding: 0; list-style: none; }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
</style>
