<script setup lang="ts">
import type { Customer } from '~/utils/customers'
import type { SellableVariant } from '~/composables/useSellableVariants'
import type { OrderFormValues, OrderLineForm } from '~/utils/orders'
import { emptyLineForm, orderChannels, previewTotals } from '~/utils/orders'

const props = defineProps<{
  customers: Customer[]
  variants: SellableVariant[]
  listPrices: Map<string, number>
  currency: string
  errors: Record<string, string>
  linesLocked?: boolean
}>()
const emit = defineEmits<{ customerCreated: [customer: Customer] }>()
const form = defineModel<OrderFormValues>({ required: true })

const { t, n } = useI18n()
const showQuickAdd = ref(false)

const money = (amount: number) => n(amount, { style: 'currency', currency: props.currency })

const customerOptions = computed(() => [
  { value: '', label: t('orders.noCustomer') },
  ...props.customers
    .filter(customer => !customer.isArchived || customer.id === form.value.customerId)
    .map(customer => ({ value: customer.id, label: customer.name })),
])
const channelOptions = computed(() => orderChannels.map(channel => ({ value: channel, label: t(`orders.channels.${channel}`) })))
const productOptions = computed(() => [
  { value: '', label: t('orders.freeTextLine') },
  ...props.variants.map(variant => ({ value: variant.id, label: variant.label })),
])
const totals = computed(() => previewTotals(form.value, props.listPrices))

function addLine() {
  form.value.lines.push(emptyLineForm())
  nextTick(() => {
    const selects = document.querySelectorAll<HTMLElement>('[data-line-product] select')
    selects[selects.length - 1]?.focus()
  })
}

function removeLine(index: number) {
  form.value.lines.splice(index, 1)
}

function pickVariant(line: OrderLineForm, variantId: string) {
  line.variantId = variantId
  line.acknowledged = false
  const variant = props.variants.find(candidate => candidate.id === variantId)
  line.rights = variant?.rights ?? null
  line.unitPrice = variant ? String(variant.price).replace('.', ',') : ''
  if (variant) {
    line.description = ''
    line.unitCost = ''
  }
}

function variantFor(line: OrderLineForm) {
  return props.variants.find(candidate => candidate.id === line.variantId)
}

function blockersFor(line: OrderLineForm) {
  return variantFor(line)?.blockers ?? []
}

function onCustomerCreated(customer: Customer) {
  emit('customerCreated', customer)
  form.value.customerId = customer.id
}

function lineError(index: number, field: string) {
  return props.errors[`lines.${index}.${field}`]
}
</script>

<template>
  <div class="order-form">
    <UiCard class="group">
      <UiSelect
        v-model="form.customerId"
        :label="$t('orders.fields.customer')"
        :options="customerOptions"
        :error="errors.customerId"
      />
      <UiButton
        variant="secondary"
        block
        @click="showQuickAdd = true"
      >
        {{ $t('orders.newCustomer') }}
      </UiButton>
      <UiSelect
        v-model="form.channel"
        :label="$t('orders.fields.channel')"
        :options="channelOptions"
        :error="errors.channel"
      />
      <UiInput
        v-model="form.dueDate"
        type="date"
        :label="$t('orders.fields.dueDate')"
        :error="errors.dueDate"
      />
    </UiCard>

    <section
      class="lines"
      :aria-label="$t('orders.lines')"
    >
      <h3>{{ $t('orders.lines') }}</h3>
      <p
        v-if="form.lines.length === 0"
        class="hint"
      >
        {{ $t('orders.noLines') }}
      </p>
      <UiCard
        v-for="(line, index) in form.lines"
        :key="line.key"
        class="line"
      >
        <div data-line-product>
          <UiSelect
            :model-value="line.variantId"
            :label="$t('orders.fields.product')"
            :options="productOptions"
            :error="lineError(index, 'variantId')"
            @update:model-value="pickVariant(line, $event)"
          />
        </div>
        <template v-if="line.variantId === ''">
          <UiInput
            v-model="line.description"
            :label="$t('orders.fields.description')"
            :error="lineError(index, 'description') ? $t('orders.errors.required', { field: $t('orders.fields.description') }) : undefined"
          />
          <UiInput
            v-model="line.unitCost"
            :label="$t('orders.fields.unitCost')"
            :hint="$t('orders.hints.unitCost')"
            inputmode="decimal"
            :error="lineError(index, 'unitCost') ? $t('orders.errors.number', { field: $t('orders.fields.unitCost') }) : undefined"
          />
        </template>
        <div class="trio">
          <UiInput
            v-model="line.quantity"
            :label="$t('orders.fields.quantity')"
            inputmode="numeric"
            :error="lineError(index, 'quantity') ? $t('orders.errors.quantity') : undefined"
          />
          <UiInput
            v-model="line.unitPrice"
            :label="$t('orders.fields.unitPrice')"
            inputmode="decimal"
            :error="lineError(index, 'unitPrice') ? $t('orders.errors.price') : undefined"
          />
          <UiInput
            v-model="line.discountPercent"
            :label="$t('orders.fields.discountPercent')"
            inputmode="decimal"
            :error="lineError(index, 'discountPercent') ? $t('orders.errors.percent') : undefined"
          />
        </div>
        <div
          v-if="line.variantId !== '' && line.rights === 'NotCleared'"
          class="rights"
          role="alert"
        >
          <p>
            <strong>{{ $t('orders.rights.title') }}</strong>
            {{ $t('orders.rights.body', { models: blockersFor(line).join(', ') || '-' }) }}
          </p>
          <label class="ack">
            <input
              v-model="line.acknowledged"
              type="checkbox"
              :aria-invalid="lineError(index, 'risk') ? 'true' : undefined"
            >
            {{ $t('orders.rights.acknowledge') }}
          </label>
          <p
            v-if="lineError(index, 'risk')"
            class="error"
          >
            {{ $t('orders.errors.risk') }}
          </p>
        </div>
        <UiButton
          variant="ghost"
          @click="removeLine(index)"
        >
          {{ $t('orders.removeLine') }}
        </UiButton>
      </UiCard>
      <p
        v-if="errors.lines"
        class="error"
        role="alert"
      >
        {{ errors.lines }}
      </p>
      <UiButton
        v-if="!linesLocked"
        variant="secondary"
        block
        @click="addLine"
      >
        {{ $t('orders.addLine') }}
      </UiButton>
    </section>

    <UiCard class="group">
      <UiInput
        v-model="form.discountAmount"
        :label="$t('orders.fields.discountAmount')"
        inputmode="decimal"
        :error="errors.discountAmount"
      />
      <UiTextarea
        v-model="form.notes"
        :label="$t('orders.fields.notes')"
        :error="errors.notes"
        :maxlength="2000"
      />
      <dl
        class="totals"
        role="status"
      >
        <div>
          <dt>{{ $t('orders.subtotal') }}</dt>
          <dd>{{ money(totals.subtotal) }}</dd>
        </div>
        <div v-if="totals.discount > 0">
          <dt>{{ $t('orders.discount') }}</dt>
          <dd>-{{ money(totals.discount) }}</dd>
        </div>
        <div class="grand">
          <dt>{{ $t('orders.total') }}</dt>
          <dd>{{ money(totals.total) }}</dd>
        </div>
      </dl>
    </UiCard>

    <CustomerQuickAdd
      v-model="showQuickAdd"
      @created="onCustomerCreated"
    />
  </div>
</template>

<style scoped>
.order-form { display: grid; gap: var(--space-3); }
.group, .line { display: grid; gap: var(--space-3); }
.lines { display: grid; gap: var(--space-3); }
h3 { margin: 0; font-size: var(--font-size-md); }
.hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.trio { display: grid; grid-template-columns: 0.8fr 1.2fr 1fr; gap: var(--space-2); }
.rights { display: grid; gap: var(--space-2); padding: var(--space-3); border: 1px solid var(--warning); border-radius: var(--radius-md); background: color-mix(in srgb, var(--warning) 12%, transparent); }
.rights p { margin: 0; font-size: var(--font-size-sm); }
.ack { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); font-size: var(--font-size-sm); }
.ack input { width: 20px; height: 20px; accent-color: var(--accent); }
.ack input[aria-invalid='true'] { outline: 2px solid var(--danger); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.totals { display: grid; gap: var(--space-1); margin: 0; }
.totals div { display: flex; justify-content: space-between; gap: var(--space-3); }
.totals dt { color: var(--muted); }
.totals dd { margin: 0; font-variant-numeric: tabular-nums; }
.totals .grand { padding-top: var(--space-2); border-top: 1px solid var(--border); font-weight: 700; font-size: var(--font-size-lg); }
.totals .grand dt { color: var(--text); }
</style>
