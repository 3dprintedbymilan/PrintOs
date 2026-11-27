<script setup lang="ts">
import { blockingModels, isNotCleared, saleRiskBody, saleToggleAction } from '~/utils/products'
import type { Variant } from '~/utils/products'

const props = defineProps<{ productId: string, variant: Variant }>()
const emit = defineEmits<{ updated: [variant: Variant] }>()

const { t } = useI18n()
const auth = useAuthStore()

const rights = computed(() => props.variant.commercialRights)
const blockers = computed(() => blockingModels(rights.value))
const blockerNames = computed(() => blockers.value.map(model => model.modelName).join(', '))
const action = computed(() => saleToggleAction(props.variant))
const stale = computed(() => props.variant.readyForSale === true && isNotCleared(rights.value))

const confirmOpen = ref(false)
const saving = ref(false)
const error = ref('')

async function send(readyForSale: boolean, acknowledged: boolean) {
  saving.value = true
  error.value = ''
  try {
    const updated = await auth.authorizedFetch<Variant>(
      `/api/products/${props.productId}/variants/${props.variant.id}/sale-status`,
      { method: 'PUT', body: saleRiskBody(readyForSale, acknowledged) },
    )
    confirmOpen.value = false
    emit('updated', updated)
  }
  catch (failure) {
    const parsed = parseApiError(failure)
    confirmOpen.value = false
    error.value = parsed.kind === 'network'
      ? t('rights.errors.network')
      : parsed.kind === 'validation' && parsed.fieldErrors.commercialRights ? t('rights.errors.stale') : t('rights.errors.save')
  }
  finally {
    saving.value = false
  }
}

function toggle() {
  if (action.value === 'markReadyAtRisk') confirmOpen.value = true
  else send(action.value === 'markReady', false)
}
</script>

<template>
  <section
    class="rights"
    :aria-label="$t('rights.status.' + (rights?.status ?? 'NoModel'))"
  >
    <div
      v-if="rights?.status === 'NotCleared'"
      class="banner danger"
      role="alert"
    >
      <svg
        viewBox="0 0 24 24"
        width="24"
        height="24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      ><path d="M12 3 2.5 20h19L12 3zM12 10v4.5M12 17.5v.01" /></svg>
      <div class="banner-text">
        <strong>{{ $t('rights.bannerTitle') }}</strong>
        <span>{{ $t('rights.bannerBody', { models: blockerNames }) }}</span>
        <ul class="models">
          <li
            v-for="model in blockers"
            :key="model.modelId"
          >
            {{ $t('rights.modelStatus', { name: model.modelName, status: $t(`licenses.status.${model.licenseStatus}`) }) }}
          </li>
        </ul>
      </div>
    </div>
    <div
      v-else-if="rights?.status === 'Cleared'"
      class="banner ok"
      role="note"
    >
      <div class="banner-text">
        <strong>{{ $t('rights.clearedTitle') }}</strong>
        <span>{{ $t('rights.clearedBody') }}</span>
        <span v-if="rights.expiringSoon">{{ $t('rights.expiringSoon') }}</span>
      </div>
    </div>
    <div
      v-else
      class="banner neutral"
      role="note"
    >
      <div class="banner-text">
        <strong>{{ $t('rights.noModelTitle') }}</strong>
        <span>{{ $t('rights.noModelBody') }}</span>
      </div>
    </div>

    <div
      v-if="rights && rights.attributions.length > 0"
      class="attribution"
      role="note"
    >
      <strong>{{ $t('rights.attributionTitle') }}</strong>
      <ul>
        <li
          v-for="item in rights.attributions"
          :key="item.modelId"
        >
          {{ item.modelName }}: {{ item.text || $t('rights.attributionFallback') }}
        </li>
      </ul>
    </div>

    <p
      v-if="stale"
      class="stale"
      role="alert"
    >
      {{ $t('rights.staleWarning') }}
    </p>

    <div class="sale">
      <UiStatusChip
        v-if="variant.readyForSale"
        :status="variant.saleRiskAcknowledged || stale ? 'warning' : 'success'"
      >
        {{ variant.saleRiskAcknowledged ? $t('rights.readyAtRisk') : $t('rights.readyBadge') }}
      </UiStatusChip>
      <UiButton
        :variant="action === 'markReadyAtRisk' ? 'danger' : 'secondary'"
        block
        :loading="saving"
        @click="toggle"
      >
        {{ $t(`rights.${action}`) }}
      </UiButton>
    </div>
    <p
      v-if="error"
      class="stale"
      role="alert"
    >
      {{ error }}
    </p>

    <UiDialog
      v-model="confirmOpen"
      :title="$t('rights.confirmTitle')"
    >
      <p class="confirm-text">
        {{ $t('rights.confirmBody', { models: blockerNames }) }}
      </p>
      <template #actions>
        <UiButton
          variant="ghost"
          @click="confirmOpen = false"
        >
          {{ $t('rights.cancel') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="saving"
          @click="send(true, true)"
        >
          {{ $t('rights.confirmAction') }}
        </UiButton>
      </template>
    </UiDialog>
  </section>
</template>

<style scoped>
.rights { display: grid; gap: var(--space-3); }
.banner { display: flex; gap: var(--space-3); padding: var(--space-3) var(--space-4); border-radius: var(--radius-md); border: 1px solid var(--border); background: var(--surface-2); font-size: var(--font-size-sm); }
.banner svg { flex-shrink: 0; }
.banner-text { display: grid; gap: var(--space-1); min-width: 0; overflow-wrap: anywhere; }
.banner.danger { color: var(--text); border: 2px solid var(--danger); background: color-mix(in srgb, var(--danger) 14%, var(--surface-2)); }
.banner.danger svg { color: var(--danger); }
.banner.danger strong { color: var(--danger); font-size: var(--font-size-md); }
.banner.ok { border-color: var(--success); }
.banner.ok strong { color: var(--success); }
.banner.neutral { color: var(--muted); }
.models { margin: 0; padding-left: var(--space-4); }
.attribution { display: grid; gap: var(--space-1); padding: var(--space-3) var(--space-4); border-radius: var(--radius-md); border: 1px solid var(--info); font-size: var(--font-size-sm); }
.attribution ul { margin: 0; padding-left: var(--space-4); }
.stale { margin: 0; color: var(--warning); font-size: var(--font-size-sm); font-weight: 600; }
.sale { display: grid; gap: var(--space-2); justify-items: start; }
.sale :deep(.btn) { justify-self: stretch; }
.confirm-text { margin: 0; line-height: 1.5; }
</style>
