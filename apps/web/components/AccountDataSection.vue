<script setup lang="ts">
import { exportFileName, mapAccountServerErrors, validateDeleteAccountForm } from '~/utils/accountData'
import type { DeleteAccountErrorKind, DeleteAccountField } from '~/utils/accountData'

const { t } = useI18n()
const auth = useAuthStore()
const formId = useId()

const exportPassword = ref('')
const exportState = ref<'idle' | 'running' | 'done'>('idle')
const exportError = ref('')
const exportPasswordError = ref('')
const exportedName = ref('')

const dialogOpen = ref(false)
const deleting = ref(false)
const deleteError = ref('')
const deleteForm = reactive({ password: '', workspaceName: '' })
const deleteErrors = ref<Partial<Record<DeleteAccountField, string>>>({})

const workspaceName = computed(() => auth.workspace?.name ?? '')

onMounted(() => {
  if (!auth.workspace) auth.loadWorkspace().catch(() => undefined)
})

function errorText(kind: DeleteAccountErrorKind) {
  return t(`accountData.errors.${kind}`)
}

async function runExport() {
  exportError.value = ''
  exportPasswordError.value = ''
  exportedName.value = ''
  if (!exportPassword.value) {
    exportPasswordError.value = errorText('passwordRequired')
    nextTick(() => document.getElementById(`${formId}-export`)?.querySelector<HTMLElement>('input')?.focus())
    return
  }

  exportState.value = 'running'
  try {
    const blob = await auth.authorizedFetch<Blob>('/api/account/export', {
      method: 'POST',
      body: { password: exportPassword.value },
      responseType: 'blob',
    })
    const name = exportFileName(new Date())
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = name
    document.body.append(link)
    link.click()
    link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 10000)
    exportedName.value = name
    exportState.value = 'done'
    exportPassword.value = ''
  }
  catch (error) {
    exportState.value = 'idle'
    const parsed = parseApiError(error)
    if (parsed.status === 400) exportPasswordError.value = errorText('passwordWrong')
    else if (parsed.status === 429) exportError.value = t('accountData.errors.rateLimited')
    else if (parsed.kind === 'locked') exportError.value = t('accountData.errors.locked')
    else if (parsed.kind === 'network') exportError.value = t('accountData.errors.network')
    else exportError.value = t('accountData.errors.exportFailed')
  }
}

function openDialog() {
  deleteForm.password = ''
  deleteForm.workspaceName = ''
  deleteErrors.value = {}
  deleteError.value = ''
  dialogOpen.value = true
}

function focusFirstInvalid() {
  nextTick(() => document.querySelector<HTMLElement>(`[id="${formId}-delete"] [aria-invalid="true"]`)?.focus())
}

async function confirmDelete() {
  deleteError.value = ''
  const { valid, errors } = validateDeleteAccountForm(deleteForm, workspaceName.value)
  deleteErrors.value = Object.fromEntries(Object.entries(errors).map(([field, kind]) => [field, errorText(kind)]))
  if (!valid) {
    focusFirstInvalid()
    return
  }

  deleting.value = true
  try {
    await auth.authorizedFetch('/api/account/delete', {
      method: 'POST',
      body: { password: deleteForm.password, workspaceName: deleteForm.workspaceName.trim() },
    })
    dialogOpen.value = false
    await auth.signOut()
    await navigateTo({ path: '/sign-in', query: { deleted: '1' } })
  }
  catch (error) {
    const parsed = parseApiError(error)
    const serverErrors = mapAccountServerErrors(parsed.fieldErrors)
    if (Object.keys(serverErrors).length > 0) {
      deleteErrors.value = Object.fromEntries(Object.entries(serverErrors).map(([field, kind]) => [field, errorText(kind)]))
      focusFirstInvalid()
    }
    else if (parsed.kind === 'locked') deleteError.value = t('accountData.errors.locked')
    else if (parsed.kind === 'network') deleteError.value = t('accountData.errors.network')
    else deleteError.value = t('accountData.errors.deleteFailed')
  }
  finally {
    deleting.value = false
  }
}
</script>

<template>
  <section
    class="section"
    aria-labelledby="account-data-heading"
  >
    <h2 id="account-data-heading">
      {{ $t('accountData.title') }}
    </h2>

    <UiCard class="group">
      <h3>{{ $t('accountData.export.title') }}</h3>
      <p class="intro">
        {{ $t('accountData.export.intro') }}
      </p>
      <form
        :id="`${formId}-export`"
        class="form"
        novalidate
        @submit.prevent="runExport"
      >
        <UiInput
          v-model="exportPassword"
          type="password"
          revealable
          autocomplete="current-password"
          :label="$t('accountData.fields.password')"
          :hint="$t('accountData.export.passwordHint')"
          :error="exportPasswordError"
        />
        <UiButton
          type="submit"
          block
          :loading="exportState === 'running'"
        >
          {{ exportState === 'running' ? $t('accountData.export.running') : $t('accountData.export.action') }}
        </UiButton>
      </form>
      <p
        class="feedback"
        role="status"
      >
        <template v-if="exportState === 'running'">
          {{ $t('accountData.export.progress') }}
        </template>
        <template v-else-if="exportState === 'done'">
          {{ $t('accountData.export.done', { name: exportedName }) }}
        </template>
      </p>
      <p
        v-if="exportError"
        class="feedback error"
        role="alert"
      >
        {{ exportError }}
      </p>
    </UiCard>

    <UiCard class="group danger-zone">
      <h3>{{ $t('accountData.delete.title') }}</h3>
      <p class="intro">
        {{ $t('accountData.delete.intro') }}
      </p>
      <UiButton
        variant="danger"
        block
        @click="openDialog"
      >
        {{ $t('accountData.delete.open') }}
      </UiButton>
    </UiCard>

    <UiDialog
      v-model="dialogOpen"
      :title="$t('accountData.delete.dialogTitle')"
    >
      <form
        :id="`${formId}-delete`"
        class="form"
        novalidate
        @submit.prevent="confirmDelete"
      >
        <p class="warning">
          {{ $t('accountData.delete.warning') }}
        </p>
        <ul class="consequences">
          <li>{{ $t('accountData.delete.consequences.data') }}</li>
          <li>{{ $t('accountData.delete.consequences.files') }}</li>
          <li>{{ $t('accountData.delete.consequences.bambu') }}</li>
          <li>{{ $t('accountData.delete.consequences.signedOut') }}</li>
          <li>{{ $t('accountData.delete.consequences.backups') }}</li>
        </ul>
        <UiInput
          v-model="deleteForm.password"
          type="password"
          revealable
          autocomplete="current-password"
          :label="$t('accountData.fields.password')"
          :error="deleteErrors.password"
        />
        <UiInput
          v-model="deleteForm.workspaceName"
          autocomplete="off"
          :label="$t('accountData.delete.typeName', { name: workspaceName })"
          :error="deleteErrors.workspaceName"
        />
        <p
          v-if="deleteError"
          class="feedback error"
          role="alert"
        >
          {{ deleteError }}
        </p>
      </form>
      <template #actions>
        <UiButton
          variant="secondary"
          :disabled="deleting"
          @click="dialogOpen = false"
        >
          {{ $t('accountData.delete.cancel') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="deleting"
          @click="confirmDelete"
        >
          {{ $t('accountData.delete.confirm') }}
        </UiButton>
      </template>
    </UiDialog>
  </section>
</template>

<style scoped>
.section, .form { display: grid; gap: var(--space-3); }
.section > h2 { margin: var(--space-3) 0 0; font-size: var(--font-size-lg); }
.group { display: grid; gap: var(--space-3); }
.group h3 { margin: 0; font-size: var(--font-size-md); }
.intro { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.danger-zone { border-color: var(--danger); }
.warning { margin: 0; color: var(--danger); font-weight: 600; }
.consequences { margin: 0; padding-left: var(--space-5); color: var(--muted); font-size: var(--font-size-sm); display: grid; gap: var(--space-1); }
.feedback { margin: 0; color: var(--accent); font-size: var(--font-size-sm); }
.feedback:empty { display: none; }
.feedback.error { color: var(--danger); }
</style>
