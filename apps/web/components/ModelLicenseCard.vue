<script setup lang="ts">
import { describeExpiry } from '~/utils/licenses'
import type { License, LicenseSummary } from '~/utils/licenses'
import type { ModelDetail } from '~/utils/modelLibrary'

const props = defineProps<{ modelId: string, license: LicenseSummary | null | undefined }>()
const emit = defineEmits<{ updated: [model: ModelDetail] }>()

const { t } = useI18n()
const auth = useAuthStore()

const licenses = ref<License[]>([])
const loadFailed = ref(false)
const selected = ref(props.license?.id ?? '')
const saving = ref(false)
const message = ref('')
const error = ref('')

const options = computed(() => [
  { value: '', label: t('licenses.picker.none') },
  ...licenses.value.map(license => ({ value: license.id, label: `${license.name} (${t(`licenses.status.${license.assessment.status}`)})` })),
])

watch(() => props.license?.id, (value) => {
  selected.value = value ?? ''
})

async function loadLicenses() {
  loadFailed.value = false
  try {
    licenses.value = await auth.authorizedFetch<License[]>('/api/licenses')
  }
  catch {
    loadFailed.value = true
  }
}

async function choose(value: string) {
  selected.value = value
  message.value = ''
  error.value = ''
  saving.value = true
  try {
    const updated = await auth.authorizedFetch<ModelDetail>(`/api/models/${props.modelId}/license`, {
      method: 'PUT',
      body: { licenseId: value || null },
    })
    message.value = value ? t('licenses.picker.linked') : t('licenses.picker.unlinked')
    emit('updated', updated)
  }
  catch {
    selected.value = props.license?.id ?? ''
    error.value = t('licenses.picker.error')
  }
  finally {
    saving.value = false
  }
}

onMounted(loadLicenses)
</script>

<template>
  <UiCard class="group">
    <div class="head">
      <h2 class="heading">
        {{ $t('licenses.picker.title') }}
      </h2>
      <LicenseStatusChip :status="license?.assessment.status ?? 'NoLicense'" />
    </div>

    <template v-if="license">
      <p
        class="verdict"
        :class="{ allowed: license.assessment.allowsCommercialSale }"
      >
        {{ license.assessment.allowsCommercialSale ? $t('licenses.commercialAllowed') : $t('licenses.commercialBlocked') }}
      </p>
      <p
        v-if="license.validUntil"
        class="meta"
      >
        {{ describeExpiry(t, license.assessment) }}
      </p>
      <p
        v-if="license.attributionRequired"
        class="meta"
      >
        {{ $t('licenses.attributionNeeded') }}{{ license.attributionText ? `: ${license.attributionText}` : '' }}
      </p>
    </template>
    <p
      v-else
      class="meta"
    >
      {{ $t('licenses.picker.hint') }}
    </p>

    <UiSelect
      :model-value="selected"
      :label="$t('licenses.picker.label')"
      :options="options"
      @update:model-value="choose"
    />
    <p
      v-if="loadFailed"
      class="error"
      role="alert"
    >
      {{ $t('licenses.picker.loadFailed') }}
      <button
        type="button"
        class="retry"
        @click="loadLicenses"
      >
        {{ $t('licenses.picker.retry') }}
      </button>
    </p>
    <p
      v-if="message"
      class="notice"
      role="status"
    >
      {{ message }}
    </p>
    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <div class="links">
      <NuxtLink
        v-if="license"
        :to="`/licenses/${license.id}`"
      >
        {{ $t('licenses.picker.open') }}
      </NuxtLink>
      <NuxtLink to="/licenses/new">
        {{ $t('licenses.picker.create') }}
      </NuxtLink>
    </div>
  </UiCard>
</template>

<style scoped>
.group { display: grid; gap: var(--space-3); }
.head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); flex-wrap: wrap; }
.heading { margin: 0; font-size: var(--font-size-md, 1rem); }
.verdict { margin: 0; color: var(--danger); font-weight: 600; }
.verdict.allowed { color: var(--accent-text); }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
.notice { margin: 0; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.retry { min-height: var(--tap-size); padding: 0 var(--space-3); background: none; border: 0; color: var(--accent); font: inherit; font-weight: 600; text-decoration: underline; }
.links { display: flex; flex-wrap: wrap; gap: var(--space-4); }
.links a { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
</style>
