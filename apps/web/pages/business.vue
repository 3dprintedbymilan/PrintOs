<script setup lang="ts">
definePageMeta({ titleKey: 'business.title' })
const auth = useAuthStore()

async function signOut() {
  await auth.signOut()
  await navigateTo('/sign-in')
}

onMounted(() => {
  if (auth.isSignedIn && !auth.workspace) auth.loadWorkspace().catch(() => undefined)
})

</script>

<template>
  <div class="stack">
    <UiListRow
      to="/settings"
      :title="$t('business.settings')"
      :subtitle="$t('business.settingsSubtitle')"
    />
    <UiListRow
      to="/printers"
      :title="$t('business.printers')"
      :subtitle="$t('business.printersSubtitle')"
    />
    <UiListRow
      to="/products"
      :title="$t('business.products')"
      :subtitle="$t('business.productsSubtitle')"
    />
    <UiListRow
      to="/calculator"
      :title="$t('business.calculator')"
      :subtitle="$t('business.calculatorSubtitle')"
    />
    <UiListRow
      to="/orders"
      :title="$t('business.orders')"
      :subtitle="$t('business.ordersSubtitle')"
    />
    <UiListRow
      to="/markets"
      :title="$t('business.markets')"
      :subtitle="$t('business.marketsSubtitle')"
    />
    <UiListRow
      to="/customers"
      :title="$t('business.customers')"
      :subtitle="$t('business.customersSubtitle')"
    />
    <UiListRow
      to="/audit"
      :title="$t('business.audit')"
      :subtitle="$t('business.auditSubtitle')"
    />
    <UiListRow
      to="/licenses"
      :title="$t('business.licenses')"
      :subtitle="$t('business.licensesSubtitle')"
    />
    <UiListRow
      to="/reports/margin"
      :title="$t('business.margins')"
      :subtitle="$t('business.marginsSubtitle')"
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
