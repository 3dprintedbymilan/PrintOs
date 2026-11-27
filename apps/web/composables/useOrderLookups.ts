import type { Customer } from '~/utils/customers'

export function useOrderLookups() {
  const auth = useAuthStore()
  const customers = ref<Customer[]>([])
  const currency = ref('EUR')
  const { variants, listPrices, load: loadVariants } = useSellableVariants()

  async function load() {
    const [customerList, settings] = await Promise.all([
      auth.authorizedFetch<Customer[]>('/api/customers', { query: { includeArchived: true } }),
      auth.authorizedFetch<{ currency: string }>('/api/workspace/settings'),
      loadVariants(),
    ])
    customers.value = customerList
    currency.value = settings.currency
  }

  function addCustomer(customer: Customer) {
    customers.value = [...customers.value, customer].sort((left, right) => left.name.localeCompare(right.name))
  }

  return { customers, currency, variants, listPrices, load, addCustomer }
}
