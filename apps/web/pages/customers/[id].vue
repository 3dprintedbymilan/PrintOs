<script setup lang="ts">
import type { Customer, CustomerErrorKind, CustomerField, CustomerFormValues } from '~/utils/customers'
import type { OrderSummary } from '~/utils/orders'
import { orderLabel, orderPaymentTone, orderStatusTone } from '~/utils/orders'
import { customerToForm, emptyCustomerForm, validateCustomerForm } from '~/utils/customers'

definePageMeta({ titleKey: 'customers.formTitle' })

const { t, n } = useI18n()
const route = useRoute()
const auth = useAuthStore()

const id = String(route.params.id)
const isNew = id === 'new'

const form = ref<CustomerFormValues>(emptyCustomerForm())
const fieldErrors = ref<Partial<Record<CustomerField, string>>>({})
const status = ref<'loading' | 'ready' | 'failed'>(isNew ? 'ready' : 'loading')
const saving = ref(false)
const saveError = ref('')
const archived = ref(false)
const orders = ref<OrderSummary[]>([])
const ordersFailed = ref(false)

async function load() {
  status.value = 'loading'
  try {
    const customer = await auth.authorizedFetch<Customer>(`/api/customers/${id}`)
    form.value = customerToForm(customer)
    archived.value = customer.isArchived
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function errorText(field: CustomerField, kind: CustomerErrorKind) {
  return t(`customers.errors.${kind}`, { field: t(`customers.fields.${field}`) })
}

async function save() {
  saveError.value = ''
  const { customer, errors } = validateCustomerForm(form.value)
  fieldErrors.value = Object.fromEntries(Object.entries(errors).map(([field, kind]) => [field, errorText(field as CustomerField, kind)]))
  if (!customer) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }

  saving.value = true
  try {
    await auth.authorizedFetch(isNew ? '/api/customers' : `/api/customers/${id}`, { method: isNew ? 'POST' : 'PUT', body: customer })
    await navigateTo('/customers')
  }
  catch {
    saveError.value = t('customers.errors.save')
  }
  finally {
    saving.value = false
  }
}

async function setArchived(value: boolean) {
  saveError.value = ''
  try {
    await auth.authorizedFetch(`/api/customers/${id}/${value ? 'archive' : 'restore'}`, { method: 'POST' })
    await navigateTo('/customers')
  }
  catch {
    saveError.value = t('customers.errors.save')
  }
}

async function loadOrders() {
  ordersFailed.value = false
  try {
    orders.value = await auth.authorizedFetch<OrderSummary[]>('/api/orders', { query: { customerId: id } })
  }
  catch {
    ordersFailed.value = true
  }
}

onMounted(() => {
  if (isNew) return
  load()
  loadOrders()
})
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/customers"
      class="back"
    >
      {{ $t('customers.back') }}
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
        {{ isNew ? $t('customers.addTitle') : $t('customers.editTitle') }}
      </h2>

      <UiCard>
        <CustomerFields
          v-model="form"
          :errors="fieldErrors"
        />
      </UiCard>

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
        {{ $t('customers.save') }}
      </UiButton>
      <UiButton
        v-if="!isNew"
        variant="secondary"
        block
        @click="setArchived(!archived)"
      >
        {{ archived ? $t('customers.restore') : $t('customers.archive') }}
      </UiButton>
    </form>

    <section
      v-if="!isNew && status === 'ready'"
      class="history"
      :aria-label="$t('customers.orders')"
    >
      <h3>{{ $t('customers.orders') }}</h3>
      <p
        v-if="ordersFailed"
        class="error"
        role="alert"
      >
        {{ $t('customers.ordersFailed') }}
      </p>
      <p
        v-else-if="orders.length === 0"
        class="hint"
      >
        {{ $t('customers.noOrders') }}
      </p>
      <UiListRow
        v-for="order in orders"
        :key="order.id"
        :to="`/orders/${order.id}`"
        :title="orderLabel(order.number)"
        :subtitle="n(order.totalAmount, { style: 'currency', currency: order.currency })"
      >
        <template #trailing>
          <span class="chips">
            <UiStatusChip :status="orderStatusTone[order.status]">
              {{ $t(`orders.status.${order.status}`) }}
            </UiStatusChip>
            <UiStatusChip :status="orderPaymentTone[order.paymentStatus]">
              {{ $t(`orders.payment.${order.paymentStatus}`) }}
            </UiStatusChip>
          </span>
        </template>
      </UiListRow>
      <UiButton
        v-if="!archived"
        variant="secondary"
        block
        @click="navigateTo({ path: '/orders/new', query: { customerId: id } })"
      >
        {{ $t('customers.newOrder') }}
      </UiButton>
    </section>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.heading { margin: 0; font-size: var(--font-size-lg); }
.history { display: grid; gap: var(--space-3); }
.history h3 { margin: 0; font-size: var(--font-size-md); }
.hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.chips { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--space-1); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
