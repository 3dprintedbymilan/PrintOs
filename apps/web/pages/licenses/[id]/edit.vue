<script setup lang="ts">
import { licenseToForm } from '~/utils/licenses'
import type { License, LicenseDetail, LicenseField, LicenseFormValues, LicenseRequest } from '~/utils/licenses'

definePageMeta({ titleKey: 'licenses.editTitle' })

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const id = String(route.params.id)

const initial = ref<LicenseFormValues | null>(null)
const status = ref<'loading' | 'ready' | 'failed' | 'missing'>('loading')
const saving = ref(false)
const saveError = ref('')
const serverErrors = ref<Partial<Record<LicenseField, string>>>({})

async function load() {
  status.value = 'loading'
  try {
    const detail = await auth.authorizedFetch<LicenseDetail>(`/api/licenses/${id}`)
    initial.value = licenseToForm(detail.license)
    status.value = 'ready'
  }
  catch (error) {
    status.value = parseApiError(error).status === 404 ? 'missing' : 'failed'
  }
}

async function save(request: LicenseRequest) {
  saveError.value = ''
  serverErrors.value = {}
  saving.value = true
  try {
    await auth.authorizedFetch<License>(`/api/licenses/${id}`, { method: 'PUT', body: request })
    await navigateTo(`/licenses/${id}`)
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation') {
      serverErrors.value = Object.fromEntries(Object.keys(parsed.fieldErrors).map(field => [field, t('common.rejected')]))
    }
    saveError.value = t('licenses.errors.save')
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
      :to="`/licenses/${id}`"
      class="back"
    >
      {{ $t('licenses.backToDetail') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateEmpty
      v-else-if="status === 'missing'"
      :message="$t('licenses.notFound')"
    />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else-if="initial">
      <h2 class="heading">
        {{ $t('licenses.editTitle') }}
      </h2>
      <LicenseForm
        :initial="initial"
        :saving="saving"
        :save-error="saveError"
        :server-errors="serverErrors"
        :submit-label="$t('licenses.save')"
        @submit="save"
      />
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.heading { margin: 0; font-size: var(--font-size-lg); }
</style>
