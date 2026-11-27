import type { ProductSummary, RightsStatus, Variant } from '~/utils/products'

export interface SellableVariant {
  id: string
  label: string
  price: number
  rights: RightsStatus
  blockers: string[]
}

export function useSellableVariants() {
  const auth = useAuthStore()
  const variants = ref<SellableVariant[]>([])

  async function load() {
    const products = await auth.authorizedFetch<ProductSummary[]>('/api/products')
    const details = await Promise.all(
      products.filter(product => product.variantCount > 0).map(product =>
        auth.authorizedFetch<{ name: string, variants: Variant[] }>(`/api/products/${product.id}`)),
    )
    variants.value = details.flatMap(product => product.variants.map(variant => ({
      id: variant.id,
      label: `${product.name} - ${variant.name}`,
      price: variant.price,
      rights: variant.commercialRights?.status ?? 'NoModel',
      blockers: (variant.commercialRights?.models ?? []).filter(model => !model.allowsCommercialSale).map(model => model.modelName),
    })))
  }

  const listPrices = computed(() => new Map(variants.value.map(variant => [variant.id, variant.price])))

  return { variants, listPrices, load }
}
