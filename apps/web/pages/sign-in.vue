<script setup lang="ts">
import { mapSignUpFieldErrors, parseApiError } from '~/utils/apiError'
import { failedPasswordRules, isValidEmail, minimumPasswordLength } from '~/utils/passwordRules'

definePageMeta({ layout: 'auth' })

const { t } = useI18n()
const route = useRoute()
const auth = useAuthStore()

const mode = ref(route.query.mode === 'sign-up' ? 'sign-up' : 'sign-in')
const email = ref('')
const password = ref('')
const workspaceName = ref('')
const submitting = ref(false)
const formError = ref('')
const errors = reactive({ email: '', password: '', workspaceName: '' })

const isSignUp = computed(() => mode.value === 'sign-up')

useHead({ title: computed(() => `${isSignUp.value ? t('auth.signUpTitle') : t('auth.signInTitle')} Â· ${t('app.name')}`) })

const modeOptions = computed(() => [
  { value: 'sign-in', label: t('auth.signIn') },
  { value: 'sign-up', label: t('auth.signUp') },
])

watch(mode, () => {
  formError.value = ''
  errors.email = ''
  errors.password = ''
  errors.workspaceName = ''
})

function validate(): boolean {
  errors.email = isValidEmail(email.value) ? '' : t('auth.errors.email')
  errors.workspaceName = !isSignUp.value || workspaceName.value.trim() ? '' : t('auth.errors.workspaceName')

  if (isSignUp.value) {
    errors.password = failedPasswordRules(password.value).length > 0 ? t('auth.passwordRules', { min: minimumPasswordLength }) : ''
  }
  else {
    errors.password = password.value ? '' : t('auth.errors.passwordRequired')
  }

  return !errors.email && !errors.password && !errors.workspaceName
}

async function focusFirstInvalidField() {
  await nextTick()
  document.querySelector<HTMLInputElement>('input[aria-invalid="true"]')?.focus()
}

async function submit() {
  formError.value = ''
  if (!validate()) {
    await focusFirstInvalidField()
    return
  }

  submitting.value = true
  try {
    if (isSignUp.value) {
      await auth.signUp({ email: email.value, password: password.value, workspaceName: workspaceName.value })
    }
    else {
      await auth.signIn({ email: email.value, password: password.value })
    }
    await navigateTo('/')
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation') {
      const { fields, unmatched } = mapSignUpFieldErrors(parsed.fieldErrors)
      errors.email = fields.email ?? ''
      errors.password = fields.password ?? ''
      errors.workspaceName = fields.workspaceName ?? ''
      formError.value = unmatched[0] ?? (Object.keys(fields).length === 0 ? t('auth.errors.unknown') : '')
      await focusFirstInvalidField()
    }
    else if (parsed.kind === 'invalidCredentials') formError.value = t('auth.errors.invalidCredentials')
    else if (parsed.kind === 'locked') formError.value = t('auth.errors.locked')
    else if (parsed.kind === 'network') formError.value = t('auth.errors.network')
    else formError.value = t('auth.errors.unknown')
  }
  finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="screen">
    <header class="brand">
      <div class="brand-text">
        <span class="wordmark">
          <span
            class="logo"
            aria-hidden="true"
          >P</span>
          {{ $t('app.name') }}
        </span>
        <p class="tagline">
          {{ $t('app.tagline') }}
        </p>
      </div>
      <LayerVase />
    </header>

    <UiSegmentedControl
      v-model="mode"
      :label="$t('auth.modeLabel')"
      :options="modeOptions"
    />

    <h1 tabindex="-1">
      {{ isSignUp ? $t('auth.signUpTitle') : $t('auth.signInTitle') }}
    </h1>

    <p
      v-if="route.query.deleted === '1'"
      class="deleted"
      role="status"
    >
      {{ $t('accountData.deletedNotice') }}
    </p>

    <form
      class="form"
      novalidate
      @submit.prevent="submit"
    >
      <UiCard>
        <div class="fields">
          <UiInput
            v-if="isSignUp"
            v-model="workspaceName"
            name="workspaceName"
            :label="$t('auth.workspaceName')"
            :hint="$t('auth.workspaceHint')"
            :error="errors.workspaceName"
            autocomplete="organization"
          />
          <UiInput
            v-model="email"
            name="email"
            type="email"
            inputmode="email"
            autocomplete="email"
            :label="$t('auth.email')"
            :error="errors.email"
          />
          <UiInput
            v-model="password"
            name="password"
            type="password"
            revealable
            :autocomplete="isSignUp ? 'new-password' : 'current-password'"
            :label="$t('auth.password')"
            :hint="isSignUp ? $t('auth.passwordRules', { min: minimumPasswordLength }) : undefined"
            :error="errors.password"
          />
        </div>
      </UiCard>

      <p
        v-if="formError"
        class="form-error"
        role="alert"
      >
        {{ formError }}
      </p>

      <UiButton
        type="submit"
        block
        :loading="submitting"
      >
        {{ isSignUp ? $t('auth.createAccount') : $t('auth.signInAction') }}
      </UiButton>
    </form>
  </div>
</template>

<style scoped>
.screen { display: grid; gap: var(--space-5); }
.brand { display: flex; align-items: center; justify-content: space-between; gap: var(--space-4); }
.brand-text { display: grid; gap: var(--space-2); min-width: 0; }
.wordmark { display: inline-flex; align-items: center; gap: var(--space-3); font-size: var(--font-size-xl); font-weight: 700; letter-spacing: -0.01em; }
.logo { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; background: var(--accent); color: var(--accent-contrast); font-weight: 800; }
.tagline { margin: 0; max-width: 28ch; color: var(--muted); text-wrap: balance; }
h1 { margin: 0; font-size: var(--font-size-lg); text-wrap: balance; }
h1:focus { outline: none; }
.form { display: grid; gap: var(--space-4); }
.fields { display: grid; gap: var(--space-4); }
.form-error { margin: 0; padding: var(--space-3) var(--space-4); border: 1px solid var(--danger); border-radius: var(--radius-md); color: var(--danger); background: color-mix(in srgb, var(--danger) 10%, transparent); }
.deleted { margin: 0; color: var(--accent); }
</style>
