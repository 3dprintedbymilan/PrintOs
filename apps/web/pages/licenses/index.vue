<script setup lang="ts">
import { describeExpiry } from '~/utils/licenses'
import type { License } from '~/utils/licenses'

definePageMeta({ titleKey: 'licenses.title' })

const { t } = useI18n()
const auth = useAuthStore()

const licenses = ref<License[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

const attention = computed(() => licenses.value.filter(license => ['ExpiringSoon', 'Expired', 'LimitReached'].includes(license.assessment.status) || license.assessment.limitNearlyReached).length)

async function load() {
  status.value = 'loading'
  try {
    licenses.value = await auth.authorizedFetch<License[]>('/api/licenses')
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function subtitle(license: License) {
  const parts = [t(`licenses.source.${license.source}`), t(`licenses.type.${license.type}`)]
  if (license.validUntil) parts.push(describeExpiry(t, license.assessment))
  return parts.join(' · ')
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/business"
      class="back"
    >
      {{ $t('settings.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <p
        v-if="attention > 0"
        class="attention"
        role="status"
      >
        {{ $t('licenses.attention', { count: attention }) }}
      </p>
      <StateEmpty
        v-if="licenses.length === 0"
        :message="$t('licenses.empty')"
      />
      <UiListRow
        v-for="license in licenses"
        :key="license.id"
        :to="`/licenses/${license.id}`"
        :title="license.name"
        :subtitle="subtitle(license)"
      >
        <template #trailing>
          <LicenseStatusChip :status="license.assessment.status" />
        </template>
      </UiListRow>
    </template>

    <UiButton
      block
      @click="navigateTo('/licenses/new')"
    >
      {{ $t('licenses.add') }}
    </UiButton>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.attention { margin: 0; padding: var(--space-3) var(--space-4); background: var(--surface-2); box-shadow: inset 3px 0 0 var(--warning); border-radius: var(--radius-md); font-size: var(--font-size-sm); }
</style>
