<script setup lang="ts">
import type { Market, MarketFormValues } from '~/utils/markets'
import { emptyMarketForm, serverMarketErrors, validateMarketForm } from '~/utils/markets'
import { parseApiError } from '~/utils/apiError'

definePageMeta({ titleKey: 'markets.formTitle' })

const { t } = useI18n()
const auth = useAuthStore()

const form = ref<MarketFormValues>(emptyMarketForm())
const errors = ref<Record<string, string>>({})
const saving = ref(false)
const saveError = ref('')
const currency = ref('EUR')

function messages(found: Record<string, string>) {
  return Object.fromEntries(Object.entries(found).map(([field, kind]) => [field, t(`markets.errors.${kind}`)]))
}

function focusFirstInvalid() {
  nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
}

async function save() {
  saveError.value = ''
  const { request, errors: found } = validateMarketForm(form.value)
  errors.value = messages(found)
  if (!request) {
    focusFirstInvalid()
    return
  }

  saving.value = true
  try {
    const created = await auth.authorizedFetch<Market>('/api/markets', { method: 'POST', body: request })
    await navigateTo(`/markets/${created.id}`)
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && Object.keys(parsed.fieldErrors).length > 0) {
      errors.value = messages(serverMarketErrors(parsed.fieldErrors))
      focusFirstInvalid()
    }
    else saveError.value = t('markets.errors.save')
  }
  finally {
    saving.value = false
  }
}

onMounted(async () => {
  try {
    currency.value = (await auth.authorizedFetch<{ currency: string }>('/api/workspace/settings')).currency
  }
  catch {
    currency.value = 'EUR'
  }
})
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/markets"
      class="back"
    >
      {{ $t('markets.back') }}
    </NuxtLink>
    <form
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <MarketForm
        v-model="form"
        :errors="errors"
        :currency="currency"
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
        {{ $t('markets.save') }}
      </UiButton>
    </form>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
