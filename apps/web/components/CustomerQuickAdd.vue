<script setup lang="ts">
import type { Customer, CustomerErrorKind, CustomerField, CustomerFormValues } from '~/utils/customers'
import { emptyCustomerForm, validateCustomerForm } from '~/utils/customers'

const emit = defineEmits<{ created: [customer: Customer] }>()
const open = defineModel<boolean>({ required: true })

const { t } = useI18n()
const auth = useAuthStore()
const formId = useId()

const form = ref<CustomerFormValues>(emptyCustomerForm())
const fieldErrors = ref<Partial<Record<CustomerField, string>>>({})
const saving = ref(false)
const saveError = ref('')

watch(open, (isOpen) => {
  if (!isOpen) return
  form.value = emptyCustomerForm()
  fieldErrors.value = {}
  saveError.value = ''
})

function errorText(field: CustomerField, kind: CustomerErrorKind) {
  return t(`customers.errors.${kind}`, { field: t(`customers.fields.${field}`) })
}

async function submit() {
  saveError.value = ''
  const { customer, errors } = validateCustomerForm(form.value)
  fieldErrors.value = Object.fromEntries(Object.entries(errors).map(([field, kind]) => [field, errorText(field as CustomerField, kind)]))
  if (!customer) {
    nextTick(() => document.querySelector<HTMLElement>(`[id="${formId}"] [aria-invalid="true"]`)?.focus())
    return
  }

  saving.value = true
  try {
    const created = await auth.authorizedFetch<Customer>('/api/customers', { method: 'POST', body: customer })
    emit('created', created)
    open.value = false
  }
  catch {
    saveError.value = t('customers.errors.save')
  }
  finally {
    saving.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model="open"
    :title="$t('customers.addTitle')"
  >
    <form
      :id="formId"
      class="form"
      novalidate
      @submit.prevent="submit"
    >
      <CustomerFields
        v-model="form"
        :errors="fieldErrors"
        compact
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
        {{ $t('customers.save') }}
      </UiButton>
    </form>
  </UiDialog>
</template>

<style scoped>
.form { display: grid; gap: var(--space-3); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
