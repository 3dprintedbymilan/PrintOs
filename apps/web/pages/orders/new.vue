<script setup lang="ts">
import type { Order, OrderFormValues } from '~/utils/orders'
import { emptyOrderForm, orderErrorMessages, serverOrderErrors, validateOrderForm } from '~/utils/orders'
import { parseApiError } from '~/utils/apiError'

definePageMeta({ titleKey: 'orders.formTitle' })

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const lookups = useOrderLookups()

const form = ref<OrderFormValues>(emptyOrderForm())
const errors = ref<Record<string, string>>({})
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const saveError = ref('')

async function load() {
  status.value = 'loading'
  try {
    await lookups.load()
    const customerId = typeof route.query.customerId === 'string' ? route.query.customerId : ''
    if (customerId && lookups.customers.value.some(customer => customer.id === customerId && !customer.isArchived)) form.value.customerId = customerId
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function focusFirstInvalid() {
  nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
}

async function save() {
  saveError.value = ''
  const { request, errors: found } = validateOrderForm(form.value)
  errors.value = orderErrorMessages(found, t)
  if (!request) {
    focusFirstInvalid()
    return
  }

  saving.value = true
  try {
    const created = await auth.authorizedFetch<Order>('/api/orders', { method: 'POST', body: request })
    await navigateTo(`/orders/${created.id}`)
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && Object.keys(parsed.fieldErrors).length > 0) {
      errors.value = orderErrorMessages(serverOrderErrors(parsed.fieldErrors), t)
      focusFirstInvalid()
    }
    else saveError.value = t('orders.errors.save')
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/orders"
      class="back"
    >
      {{ $t('orders.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <form
      v-else
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <h2 class="heading">
        {{ $t('orders.addTitle') }}
      </h2>

      <OrderForm
        v-model="form"
        :customers="lookups.customers.value"
        :variants="lookups.variants.value"
        :list-prices="lookups.listPrices.value"
        :currency="lookups.currency.value"
        :errors="errors"
        @customer-created="lookups.addCustomer"
      />

      <p
        v-if="saveError"
        class="error"
        role="alert"
      >
        {{ saveError }}
      </p>

      <UiButton
        type="submit"
        block
        :loading="saving"
      >
        {{ $t('orders.save') }}
      </UiButton>
    </form>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.heading { margin: 0; font-size: var(--font-size-lg); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
