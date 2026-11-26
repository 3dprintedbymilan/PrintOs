<script setup lang="ts">
definePageMeta({ titleKey: 'business.title' })
const { t } = useI18n()
const auth = useAuthStore()

async function signOut() {
  await auth.signOut()
  await navigateTo('/sign-in')
}

onMounted(() => {
  if (auth.isSignedIn && !auth.workspace) auth.loadWorkspace().catch(() => undefined)
})

const sections = computed(() => [
  { key: 'orders', title: t('business.orders') },
  { key: 'calculator', title: t('business.calculator') },
  { key: 'margins', title: t('business.margins') },
  { key: 'licenses', title: t('business.licenses') },
  { key: 'marketStock', title: t('business.marketStock') },
])
</script>

<template>
  <div class="stack">
    <UiListRow
      to="/settings"
      :title="$t('business.settings')"
      :subtitle="$t('business.settingsSubtitle')"
    />
    <UiListRow
      v-for="section in sections"
      :key="section.key"
      :title="section.title"
    />
    <UiCard class="settings">
      <p>{{ $t('theme.label') }}</p>
      <UiThemeSwitch />
    </UiCard>

    <UiCard class="settings">
      <p>{{ $t('account.signedInAs') }}</p>
      <p class="account-value">
        {{ auth.email }}
      </p>
      <p v-if="auth.workspace">
        {{ $t('account.workspace') }}
      </p>
      <p
        v-if="auth.workspace"
        class="account-value"
      >
        {{ auth.workspace.name }}
      </p>
      <UiButton
        variant="secondary"
        block
        @click="signOut"
      >
        {{ $t('account.signOut') }}
      </UiButton>
    </UiCard>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.settings p { margin: 0 0 var(--space-2); color: var(--muted); font-size: var(--font-size-sm); }
.settings p.account-value { margin-bottom: var(--space-3); color: var(--text); font-size: var(--font-size-md); font-weight: 600; overflow-wrap: anywhere; }
</style>
