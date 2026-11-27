<script setup lang="ts">
import { emptyLicenseForm } from '~/utils/licenses'
import type { License, LicenseField, LicenseRequest } from '~/utils/licenses'

definePageMeta({ titleKey: 'licenses.addTitle' })

const { t } = useI18n()
const auth = useAuthStore()

const initial = emptyLicenseForm()
const saving = ref(false)
const saveError = ref('')
const serverErrors = ref<Partial<Record<LicenseField, string>>>({})

async function save(request: LicenseRequest) {
  saveError.value = ''
  serverErrors.value = {}
  saving.value = true
  try {
    const created = await auth.authorizedFetch<License>('/api/licenses', { method: 'POST', body: request })
    await navigateTo(`/licenses/${created.id}`)
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation') {
      serverErrors.value = Object.fromEntries(Object.keys(parsed.fieldErrors).map(field => [field, parsed.fieldErrors[field]?.[0] ?? t('licenses.errors.save')]))
    }
    saveError.value = t('licenses.errors.save')
  }
  finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/licenses"
      class="back"
    >
      {{ $t('licenses.back') }}
    </NuxtLink>
    <h2 class="heading">
      {{ $t('licenses.addTitle') }}
    </h2>
    <LicenseForm
      :initial="initial"
      :saving="saving"
      :save-error="saveError"
      :server-errors="serverErrors"
      :submit-label="$t('licenses.save')"
      @submit="save"
    />
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.heading { margin: 0; font-size: var(--font-size-lg); }
</style>
