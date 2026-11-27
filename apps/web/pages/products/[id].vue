<script setup lang="ts">
import { productToForm } from '~/utils/products'
import type { Product, ProductField, ProductRequest, Variant, VariantField, VariantRequest } from '~/utils/products'
import type { Material, Spool } from '~/utils/inventory'
import type { Printer } from '~/utils/printerForm'
import type { ModelSummary } from '~/utils/modelLibrary'

definePageMeta({ titleKey: 'products.detailTitle' })

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()

const id = String(route.params.id)

const product = ref<Product | null>(null)
const status = ref<'loading' | 'ready' | 'failed' | 'missing'>('loading')
const printers = ref<Printer[]>([])
const spools = ref<Spool[]>([])
const materials = ref<Material[]>([])
const models = ref<ModelSummary[]>([])

const editing = ref(false)
const saving = ref(false)
const saveError = ref('')
const serverErrors = ref<Partial<Record<ProductField, string>>>({})
const actionError = ref('')
const removing = ref(false)

const variantDialog = ref(false)
const variantSaving = ref(false)
const variantError = ref('')
const variantServerErrors = ref<Partial<Record<VariantField, string>>>({})

async function load() {
  status.value = 'loading'
  try {
    product.value = await auth.authorizedFetch<Product>(`/api/products/${id}`)
    status.value = 'ready'
  }
  catch (error) {
    status.value = parseApiError(error).status === 404 ? 'missing' : 'failed'
    return
  }

  const [loadedPrinters, loadedSpools, loadedMaterials, loadedModels] = await Promise.allSettled([
    auth.authorizedFetch<Printer[]>('/api/printers'),
    auth.authorizedFetch<Spool[]>('/api/spools', { query: { includeArchived: true } }),
    auth.authorizedFetch<Material[]>('/api/materials'),
    auth.authorizedFetch<ModelSummary[]>('/api/models'),
  ])
  printers.value = loadedPrinters.status === 'fulfilled' ? loadedPrinters.value : []
  spools.value = loadedSpools.status === 'fulfilled' ? loadedSpools.value : []
  materials.value = loadedMaterials.status === 'fulfilled' ? loadedMaterials.value : []
  models.value = loadedModels.status === 'fulfilled' ? loadedModels.value : []
}

function errorText(error: unknown, fallbackKey: string) {
  return parseApiError(error).kind === 'network' ? t('products.errors.network') : t(fallbackKey)
}

async function saveProduct(request: ProductRequest) {
  saving.value = true
  saveError.value = ''
  serverErrors.value = {}
  try {
    product.value = await auth.authorizedFetch<Product>(`/api/products/${id}`, { method: 'PUT', body: request })
    editing.value = false
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && parsed.fieldErrors.sku) serverErrors.value = { sku: t('products.errors.skuTaken') }
    else saveError.value = errorText(error, 'products.errors.save')
  }
  finally {
    saving.value = false
  }
}

async function setArchived(value: boolean) {
  actionError.value = ''
  try {
    product.value = await auth.authorizedFetch<Product>(`/api/products/${id}/${value ? 'archive' : 'restore'}`, { method: 'POST' })
  }
  catch (error) {
    actionError.value = errorText(error, 'products.errors.save')
  }
}

async function removeProduct() {
  removing.value = true
  actionError.value = ''
  try {
    await auth.authorizedFetch(`/api/products/${id}`, { method: 'DELETE' })
    await navigateTo('/products')
  }
  catch (error) {
    actionError.value = errorText(error, 'products.errors.save')
  }
  finally {
    removing.value = false
  }
}

const confirmingRemove = ref(false)

async function addVariant(request: VariantRequest) {
  variantSaving.value = true
  variantError.value = ''
  variantServerErrors.value = {}
  try {
    const created = await auth.authorizedFetch<Variant>(`/api/products/${id}/variants`, { method: 'POST', body: request })
    product.value?.variants.push(created)
    variantDialog.value = false
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && parsed.fieldErrors.sku) variantServerErrors.value = { sku: t('products.errors.skuTaken') }
    else variantError.value = errorText(error, 'products.errors.save')
  }
  finally {
    variantSaving.value = false
  }
}

function replaceVariant(updated: Variant) {
  if (!product.value) return
  product.value.variants = product.value.variants.map(variant => (variant.id === updated.id ? updated : variant))
}

function removeVariant(variantId: string) {
  if (!product.value) return
  product.value.variants = product.value.variants.filter(variant => variant.id !== variantId)
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/products"
      class="back"
    >
      {{ $t('products.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateEmpty
      v-else-if="status === 'missing'"
      :message="$t('products.missing')"
    />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <template v-else-if="product">
      <header class="head">
        <div class="title">
          <h2>{{ product.name }}</h2>
          <p v-if="product.sku">
            {{ product.sku }}
          </p>
        </div>
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
      </header>
      <p
        v-if="product.description && !editing"
        class="description"
      >
        {{ product.description }}
      </p>

      <UiCard v-if="editing">
        <ProductForm
          :initial="productToForm(product)"
          :submit-label="$t('products.saveProduct')"
          :saving="saving"
          :error="saveError"
          :server-errors="serverErrors"
          @submit="saveProduct"
        />
        <UiButton
          variant="ghost"
          block
          @click="editing = false"
        >
          {{ $t('products.cancel') }}
        </UiButton>
      </UiCard>
      <UiButton
        v-else
        variant="secondary"
        block
        @click="editing = true"
      >
        {{ $t('products.editProduct') }}
      </UiButton>

      <section
        class="variants"
        :aria-label="$t('products.variants')"
      >
        <h3>{{ $t('products.variants') }}</h3>
        <StateEmpty
          v-if="product.variants.length === 0"
          :message="$t('products.variantsEmpty')"
        />
        <ProductVariantCard
          v-for="variant in product.variants"
          :key="variant.id"
          :product-id="product.id"
          :variant="variant"
          :currency="product.currency"
          :printers="printers"
          :spools="spools"
          :materials="materials"
          :models="models"
          @updated="replaceVariant"
          @removed="removeVariant"
        />
        <UiButton
          block
          @click="variantDialog = true"
        >
          {{ $t('products.addVariant') }}
        </UiButton>
      </section>

      <p
        v-if="actionError"
        class="error"
        role="alert"
      >
        {{ actionError }}
      </p>

      <div class="danger-zone">
        <UiButton
          variant="secondary"
          block
          @click="setArchived(!product.isArchived)"
        >
          {{ product.isArchived ? $t('products.restore') : $t('products.archive') }}
        </UiButton>
        <template v-if="confirmingRemove">
          <p class="error">
            {{ $t('products.confirmRemoveProduct') }}
          </p>
          <UiButton
            variant="danger"
            block
            :loading="removing"
            @click="removeProduct"
          >
            {{ $t('products.confirmRemoveProductAction') }}
          </UiButton>
          <UiButton
            variant="ghost"
            block
            @click="confirmingRemove = false"
          >
            {{ $t('products.cancel') }}
          </UiButton>
        </template>
        <UiButton
          v-else
          variant="ghost"
          block
          @click="confirmingRemove = true"
        >
          {{ $t('products.removeProduct') }}
        </UiButton>
      </div>

      <VariantDialog
        v-model="variantDialog"
        :title="$t('products.addVariant')"
        :saving="variantSaving"
        :error="variantError"
        :server-errors="variantServerErrors"
        @submit="addVariant"
      />
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); }
.title { min-width: 0; }
h2 { margin: 0; font-size: var(--font-size-lg); overflow-wrap: anywhere; }
h3 { margin: 0; font-size: var(--font-size-md); }
.title p { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.description { margin: 0; color: var(--muted); overflow-wrap: anywhere; white-space: pre-line; }
.variants { display: grid; gap: var(--space-3); }
.danger-zone { display: grid; gap: var(--space-2); margin-top: var(--space-4); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
