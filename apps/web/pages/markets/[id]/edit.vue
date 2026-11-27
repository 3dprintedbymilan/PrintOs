<script setup lang="ts">
import type { Market, MarketFormValues } from '~/utils/markets'
import { marketToForm, serverMarketErrors, validateMarketForm } from '~/utils/markets'
import { parseApiError } from '~/utils/apiError'

definePageMeta({ titleKey: 'markets.editTitle' })

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const id = String(route.params.id)

const market = ref<Market | null>(null)
const form = ref<MarketFormValues | null>(null)
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const errors = ref<Record<string, string>>({})
const saving = ref(false)
const saveError = ref('')

function messages(found: Record<string, string>) {
  return Object.fromEntries(Object.entries(found).map(([field, kind]) => [field, t(`markets.errors.${kind}`)]))
}

function focusFirstInvalid() {
  nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
}

async function load() {
  status.value = 'loading'
  try {
    market.value = await auth.authorizedFetch<Market>(`/api/markets/${id}`)
    form.value = marketToForm(market.value)
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function save() {
  if (!form.value) return
  saveError.value = ''
  const { request, errors: found } = validateMarketForm(form.value)
  errors.value = messages(found)
  if (!request) {
    focusFirstInvalid()
    return
  }

  saving.value = true
  try {
    await auth.authorizedFetch<Market>(`/api/markets/${id}`, { method: 'PUT', body: request })
    await navigateTo(`/markets/${id}`)
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

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      :to="`/markets/${id}`"
      class="back"
    >
      {{ $t('markets.backToMarket') }}
    </NuxtLink>
    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed' || !form || !market"
      @retry="load"
    />
    <form
      v-else
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <MarketForm
        v-model="form"
        :errors="errors"
        :currency="market.currency"
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
