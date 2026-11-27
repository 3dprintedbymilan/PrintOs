<script setup lang="ts">
import type { Product, ProductField, ProductRequest } from '~/utils/products'

definePageMeta({ titleKey: 'products.formTitle' })

const { t } = useI18n()
const auth = useAuthStore()

const saving = ref(false)
const error = ref('')
const serverErrors = ref<Partial<Record<ProductField, string>>>({})

async function create(request: ProductRequest) {
  saving.value = true
  error.value = ''
  serverErrors.value = {}
  try {
    const product = await auth.authorizedFetch<Product>('/api/products', { method: 'POST', body: request })
    await navigateTo(`/products/${product.id}`)
  }
  catch (caught) {
    const parsed = parseApiError(caught)
    if (parsed.kind === 'validation' && parsed.fieldErrors.sku) serverErrors.value = { sku: t('products.errors.skuTaken') }
    else error.value = parsed.kind === 'network' ? t('products.errors.network') : t('products.errors.save')
  }
  finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/products"
      class="back"
    >
      {{ $t('products.back') }}
    </NuxtLink>
    <h2 class="heading">
      {{ $t('products.addTitle') }}
    </h2>
    <UiCard>
      <ProductForm
        :submit-label="$t('products.create')"
        :saving="saving"
        :error="error"
        :server-errors="serverErrors"
        @submit="create"
      />
    </UiCard>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.heading { margin: 0; font-size: var(--font-size-lg); }
</style>
