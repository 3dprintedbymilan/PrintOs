<script setup lang="ts">
import { acceptedProofAttribute, describeExpiry, maxProofBytes, validateProofFile } from '~/utils/licenses'
import type { License, LicenseDetail } from '~/utils/licenses'
import { formatFileSize } from '~/utils/modelLibrary'

definePageMeta({ titleKey: 'licenses.detailTitle' })

const { t, locale } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const apiBase = useRuntimeConfig().public.apiBase
const id = String(route.params.id)

const detail = ref<LicenseDetail | null>(null)
const status = ref<'loading' | 'ready' | 'failed' | 'missing'>('loading')
const message = ref('')
const actionError = ref('')
const proofError = ref('')
const uploading = ref(false)
const confirmDelete = ref(false)
const deleting = ref(false)
const proofInputId = useId()

const license = computed<License | null>(() => detail.value?.license ?? null)

async function load() {
  status.value = 'loading'
  try {
    detail.value = await auth.authorizedFetch<LicenseDetail>(`/api/licenses/${id}`)
    status.value = 'ready'
  }
  catch (error) {
    status.value = parseApiError(error).status === 404 ? 'missing' : 'failed'
  }
}

function formatDate(value: string | null) {
  return value ? new Date(`${value}T00:00:00`).toLocaleDateString(locale.value) : ''
}

async function uploadProof(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || !detail.value) return
  proofError.value = ''
  message.value = ''
  const problem = validateProofFile(file)
  if (problem) {
    proofError.value = problem === 'size' ? t('licenses.errors.proofSize', { max: formatFileSize(maxProofBytes, locale.value) }) : t(`licenses.errors.proof${problem === 'type' ? 'Type' : 'Empty'}`)
    return
  }
  const body = new FormData()
  body.append('file', file)
  uploading.value = true
  try {
    const updated = await auth.authorizedFetch<License>(`/api/licenses/${id}/proof`, { method: 'POST', body })
    detail.value = { ...detail.value, license: updated }
    message.value = t('licenses.proofSaved')
  }
  catch (error) {
    proofError.value = parseApiError(error).status === 503 ? t('licenses.errors.storage') : t('licenses.errors.proofUpload')
  }
  finally {
    uploading.value = false
  }
}

async function removeProof() {
  if (!detail.value) return
  proofError.value = ''
  message.value = ''
  try {
    const updated = await auth.authorizedFetch<License>(`/api/licenses/${id}/proof`, { method: 'DELETE' })
    detail.value = { ...detail.value, license: updated }
    message.value = t('licenses.proofRemoved')
  }
  catch {
    proofError.value = t('licenses.errors.proofUpload')
  }
}

async function downloadProof() {
  proofError.value = ''
  try {
    const link = await auth.authorizedFetch<{ url: string, fileName: string }>(`/api/licenses/${id}/proof-url`)
    const anchor = document.createElement('a')
    anchor.href = `${apiBase}${link.url}`
    anchor.download = link.fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  }
  catch {
    proofError.value = t('licenses.errors.proofDownload')
  }
}

async function removeLicense() {
  deleting.value = true
  actionError.value = ''
  try {
    await auth.authorizedFetch(`/api/licenses/${id}`, { method: 'DELETE' })
    await navigateTo('/licenses')
  }
  catch {
    confirmDelete.value = false
    actionError.value = t('licenses.errors.delete')
  }
  finally {
    deleting.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/licenses"
      class="back"
    >
      {{ $t('licenses.back') }}
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

    <template v-else-if="license && detail">
      <UiCard class="group">
        <div class="head">
          <h2 class="name">
            {{ license.name }}
          </h2>
          <LicenseStatusChip :status="license.assessment.status" />
        </div>
        <p
          class="verdict"
          :class="{ allowed: license.assessment.allowsCommercialSale }"
        >
          {{ license.assessment.allowsCommercialSale ? $t('licenses.commercialAllowed') : $t('licenses.commercialBlocked') }}
        </p>
        <p
          v-if="license.assessment.limitNearlyReached"
          class="warn"
          role="status"
        >
          {{ $t('licenses.limitNear', { remaining: license.assessment.unitsRemaining ?? 0 }, license.assessment.unitsRemaining ?? 0) }}
        </p>

        <dl class="facts">
          <dt>{{ $t('licenses.fields.source') }}</dt>
          <dd>{{ $t(`licenses.source.${license.source}`) }}</dd>
          <dt>{{ $t('licenses.fields.type') }}</dt>
          <dd>{{ $t(`licenses.type.${license.type}`) }}</dd>
          <template v-if="license.validFrom">
            <dt>{{ $t('licenses.fields.validFrom') }}</dt>
            <dd>{{ formatDate(license.validFrom) }}</dd>
          </template>
          <template v-if="license.validUntil">
            <dt>{{ $t('licenses.fields.validUntil') }}</dt>
            <dd>{{ formatDate(license.validUntil) }} · {{ describeExpiry(t, license.assessment) }}</dd>
          </template>
          <template v-if="license.assessment.unitsUsed !== null">
            <dt>{{ $t('licenses.fields.unitsLimit') }}</dt>
            <dd>
              {{ $t('licenses.unitsUsage', { used: license.assessment.unitsUsed, limit: license.unitsLimit ?? '—' }) }}
            </dd>
          </template>
          <dt>{{ $t('licenses.fields.attributionRequired') }}</dt>
          <dd>{{ license.attributionRequired ? $t('licenses.yes') : $t('licenses.no') }}</dd>
          <template v-if="license.attributionText">
            <dt>{{ $t('licenses.fields.attributionText') }}</dt>
            <dd class="pre">
              {{ license.attributionText }}
            </dd>
          </template>
          <template v-if="license.notes">
            <dt>{{ $t('licenses.fields.notes') }}</dt>
            <dd class="pre">
              {{ license.notes }}
            </dd>
          </template>
        </dl>
        <UiButton
          variant="secondary"
          @click="navigateTo(`/licenses/${license.id}/edit`)"
        >
          {{ $t('licenses.edit') }}
        </UiButton>
      </UiCard>

      <p
        v-if="message"
        class="notice"
        role="status"
      >
        {{ message }}
      </p>
      <p
        v-if="actionError"
        class="error"
        role="alert"
      >
        {{ actionError }}
      </p>

      <UiCard class="group">
        <h2 class="heading">
          {{ $t('licenses.proof.title') }}
        </h2>
        <a
          v-if="license.proofUrl"
          :href="license.proofUrl"
          class="link"
          target="_blank"
          rel="noopener noreferrer"
        >{{ license.proofUrl }}</a>
        <p
          v-if="license.hasProofFile"
          class="meta"
        >
          {{ license.proofFileName }}
        </p>
        <p
          v-else-if="!license.proofUrl"
          class="meta"
        >
          {{ $t('licenses.proof.none') }}
        </p>
        <div class="actions">
          <UiButton
            v-if="license.hasProofFile"
            variant="secondary"
            @click="downloadProof"
          >
            {{ $t('licenses.proof.download') }}
          </UiButton>
          <UiButton
            v-if="license.hasProofFile"
            variant="ghost"
            @click="removeProof"
          >
            {{ $t('licenses.proof.remove') }}
          </UiButton>
        </div>
        <div class="field">
          <label :for="proofInputId">{{ license.hasProofFile ? $t('licenses.proof.replace') : $t('licenses.proof.upload') }}</label>
          <input
            :id="proofInputId"
            type="file"
            :accept="acceptedProofAttribute"
            :disabled="uploading"
            :aria-invalid="proofError ? 'true' : undefined"
            :aria-describedby="`${proofInputId}-help`"
            @change="uploadProof"
          >
          <p
            :id="`${proofInputId}-help`"
            class="help"
            :class="{ error: !!proofError }"
            :role="proofError ? 'alert' : undefined"
          >
            {{ proofError || (uploading ? $t('licenses.proof.uploading') : $t('licenses.proof.hint', { max: formatFileSize(maxProofBytes, locale) })) }}
          </p>
        </div>
      </UiCard>

      <UiCard class="group">
        <h2 class="heading">
          {{ $t('licenses.linkedModels') }}
        </h2>
        <p
          v-if="detail.models.length === 0"
          class="meta"
        >
          {{ $t('licenses.noModels') }}
        </p>
        <UiListRow
          v-for="model in detail.models"
          :key="model.id"
          :to="`/models/${model.id}`"
          :title="model.name"
        />
      </UiCard>

      <UiButton
        block
        variant="danger"
        @click="confirmDelete = true"
      >
        {{ $t('licenses.delete') }}
      </UiButton>

      <UiDialog
        v-model="confirmDelete"
        :title="$t('licenses.deleteTitle')"
      >
        <p class="text">
          {{ $t('licenses.deleteBody', { name: license.name, count: detail.models.length }, detail.models.length) }}
        </p>
        <div class="actions">
          <UiButton
            variant="danger"
            :loading="deleting"
            @click="removeLicense"
          >
            {{ $t('licenses.deleteConfirm') }}
          </UiButton>
          <UiButton
            variant="ghost"
            @click="confirmDelete = false"
          >
            {{ $t('licenses.cancel') }}
          </UiButton>
        </div>
      </UiDialog>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); flex-wrap: wrap; }
.name { margin: 0; font-size: var(--font-size-lg, 1.25rem); overflow-wrap: anywhere; }
.heading { margin: 0; font-size: var(--font-size-md, 1rem); }
.verdict { margin: 0; color: var(--danger); font-weight: 600; }
.verdict.allowed { color: var(--accent-text); }
.warn { margin: 0; color: var(--warning); font-size: var(--font-size-sm); }
.facts { display: grid; grid-template-columns: auto 1fr; gap: var(--space-2) var(--space-4); margin: 0; }
.facts dt { color: var(--muted); font-size: var(--font-size-sm); }
.facts dd { margin: 0; overflow-wrap: anywhere; }
.pre { white-space: pre-line; }
.text { margin: 0; overflow-wrap: anywhere; }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
.link { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); overflow-wrap: anywhere; }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.notice { margin: 0; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.field { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
input[type='file'] { min-height: var(--tap-size); padding: var(--space-3); color: var(--text); background: var(--surface-2); border: 1px dashed var(--border); border-radius: var(--radius-md); font: inherit; }
input[type='file'][aria-invalid='true'] { border-color: var(--danger); }
.help { margin: 0; font-size: var(--font-size-sm); color: var(--muted); }
.help.error { color: var(--danger); }
</style>
