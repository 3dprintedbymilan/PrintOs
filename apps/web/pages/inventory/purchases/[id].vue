<script setup lang="ts">
import { acceptedProofAttribute, maxProofBytes, validateProofFile } from '~/utils/licenses'
import { formatFileSize } from '~/utils/modelLibrary'
import { parseDecimal } from '~/utils/settingsForm'
import { remainingToReceive, purchaseStatusTone } from '~/utils/purchases'
import type { PurchaseDetail } from '~/utils/purchases'

definePageMeta({ titleKey: 'purchases.detailTitle' })

const { t, n, d, locale } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const apiBase = useRuntimeConfig().public.apiBase
const id = String(route.params.id)

const purchase = ref<PurchaseDetail | null>(null)
const status = ref<'loading' | 'ready' | 'failed' | 'missing'>('loading')
const message = ref('')
const actionError = ref('')
const receiptError = ref('')
const busy = ref(false)
const uploading = ref(false)
const confirm = ref<'' | 'cancel' | 'delete'>('')
const confirmOpen = ref(false)
const receiptInputId = useId()
const receiveQuantities = reactive<Record<string, string>>({})
const receiveErrors = reactive<Record<string, string>>({})

const remaining = computed(() => (purchase.value ? remainingToReceive(purchase.value) : 0))
const canReceive = computed(() => purchase.value !== null && ['Ordered', 'PartiallyReceived'].includes(purchase.value.status) && remaining.value > 0)
const partial = computed(() => purchase.value?.status === 'PartiallyReceived')

function money(amount: number) {
  return n(amount, { style: 'currency', currency: purchase.value?.currency ?? 'EUR' })
}

function resetQuantities(detail: PurchaseDetail) {
  for (const line of detail.lines) {
    receiveQuantities[line.id] = String(Math.max(0, line.quantity - line.receivedQuantity))
    receiveErrors[line.id] = ''
  }
}

async function load() {
  status.value = 'loading'
  try {
    purchase.value = await auth.authorizedFetch<PurchaseDetail>(`/api/purchases/${id}`)
    resetQuantities(purchase.value)
    status.value = 'ready'
  }
  catch (error) {
    status.value = parseApiError(error).status === 404 ? 'missing' : 'failed'
  }
}

function apply(detail: PurchaseDetail) {
  purchase.value = detail
  resetQuantities(detail)
}

async function receive() {
  if (!purchase.value) return
  message.value = ''
  actionError.value = ''
  const lines: { lineId: string, quantity: number }[] = []
  let invalid = false
  for (const line of purchase.value.lines) {
    const open = line.quantity - line.receivedQuantity
    if (open <= 0) continue
    const quantity = parseDecimal(receiveQuantities[line.id] ?? '')
    receiveErrors[line.id] = ''
    if (quantity === null || !Number.isInteger(quantity) || quantity < 0 || quantity > open) {
      receiveErrors[line.id] = t('purchases.errors.receiveQuantity', { max: open })
      invalid = true
    }
    else if (quantity > 0) {
      lines.push({ lineId: line.id, quantity })
    }
  }
  if (!invalid && lines.length === 0) {
    actionError.value = t('purchases.errors.receiveNothing')
    return
  }
  if (invalid) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }

  busy.value = true
  try {
    const count = lines.reduce((sum, line) => sum + line.quantity, 0)
    apply(await auth.authorizedFetch<PurchaseDetail>(`/api/purchases/${id}/receive`, { method: 'POST', body: { lines } }))
    message.value = t('purchases.received', { count }, count)
  }
  catch {
    actionError.value = t('purchases.errors.receive')
  }
  finally {
    busy.value = false
  }
}

function ask(kind: 'cancel' | 'delete') {
  confirm.value = kind
  confirmOpen.value = true
}

async function runConfirmed() {
  const kind = confirm.value
  busy.value = true
  actionError.value = ''
  message.value = ''
  try {
    if (kind === 'delete') {
      await auth.authorizedFetch(`/api/purchases/${id}`, { method: 'DELETE' })
      await navigateTo('/inventory/purchases')
      return
    }
    apply(await auth.authorizedFetch<PurchaseDetail>(`/api/purchases/${id}/cancel`, { method: 'POST' }))
    message.value = t('purchases.cancelled')
  }
  catch {
    actionError.value = t(kind === 'delete' ? 'purchases.errors.delete' : 'purchases.errors.cancel')
  }
  finally {
    confirmOpen.value = false
    busy.value = false
  }
}

async function uploadReceipt(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || !purchase.value) return
  receiptError.value = ''
  message.value = ''
  const problem = validateProofFile(file)
  if (problem) {
    receiptError.value = problem === 'size' ? t('purchases.errors.fileSize', { max: formatFileSize(maxProofBytes, locale.value) }) : t(`purchases.errors.file${problem === 'type' ? 'Type' : 'Empty'}`)
    return
  }
  const body = new FormData()
  body.append('file', file)
  uploading.value = true
  try {
    purchase.value = await auth.authorizedFetch<PurchaseDetail>(`/api/purchases/${id}/receipt`, { method: 'POST', body })
    message.value = t('purchases.receipt.saved')
  }
  catch (error) {
    receiptError.value = parseApiError(error).status === 503 ? t('purchases.errors.storage') : t('purchases.errors.upload')
  }
  finally {
    uploading.value = false
  }
}

async function removeReceipt() {
  receiptError.value = ''
  message.value = ''
  try {
    purchase.value = await auth.authorizedFetch<PurchaseDetail>(`/api/purchases/${id}/receipt`, { method: 'DELETE' })
    message.value = t('purchases.receipt.removed')
  }
  catch {
    receiptError.value = t('purchases.errors.upload')
  }
}

async function downloadReceipt() {
  receiptError.value = ''
  try {
    const link = await auth.authorizedFetch<{ url: string, fileName: string }>(`/api/purchases/${id}/receipt-url`)
    const anchor = document.createElement('a')
    anchor.href = `${apiBase}${link.url}`
    anchor.download = link.fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  }
  catch {
    receiptError.value = t('purchases.errors.download')
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/inventory/purchases"
      class="back"
    >
      {{ $t('purchases.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateEmpty
      v-else-if="status === 'missing'"
      :message="$t('purchases.missing')"
    />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <template v-else-if="purchase">
      <UiCard class="group">
        <div class="head">
          <h2 class="heading">
            {{ purchase.supplierName }}
          </h2>
          <UiStatusChip :status="purchaseStatusTone(purchase.status)">
            {{ $t(`purchases.status.${purchase.status}`) }}
          </UiStatusChip>
        </div>
        <p class="meta">
          {{ d(new Date(`${purchase.purchasedOn}T00:00:00`), { dateStyle: 'long' }) }}<template v-if="purchase.invoiceReference">
            · {{ purchase.invoiceReference }}
          </template>
        </p>
        <p
          v-if="purchase.notes"
          class="meta"
        >
          {{ purchase.notes }}
        </p>
      </UiCard>

      <p
        class="feedback"
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

      <section aria-labelledby="lines-heading">
        <h2
          id="lines-heading"
          class="heading"
        >
          {{ $t('purchases.lines') }}
        </h2>
        <ul class="lines">
          <li
            v-for="line in purchase.lines"
            :key="line.id"
          >
            <span
              class="swatch"
              :style="{ background: line.colorHex ?? 'var(--surface-2)' }"
              aria-hidden="true"
            />
            <span class="what">
              <strong>{{ line.materialName }} · {{ line.colorName }}</strong>
              <span class="meta">{{ $t('purchases.lineDetail', { quantity: line.quantity, weight: n(line.weightGramsPerSpool), price: money(line.unitPrice) }) }}</span>
              <span class="meta">{{ $t('purchases.lineReceived', { received: line.receivedQuantity, ordered: line.quantity }) }} · {{ $t('purchases.costPerKg', { price: money(line.costPerKg) }) }}</span>
            </span>
            <strong class="amount">{{ money(line.lineTotal) }}</strong>
          </li>
        </ul>
        <dl class="totals">
          <div>
            <dt>{{ $t('purchases.subtotal') }}</dt>
            <dd>{{ money(purchase.subtotal) }}</dd>
          </div>
          <div v-if="purchase.shippingCost > 0">
            <dt>{{ $t('purchases.fields.shippingCost') }}</dt>
            <dd>{{ money(purchase.shippingCost) }}</dd>
          </div>
          <div v-if="purchase.discount > 0">
            <dt>{{ $t('purchases.fields.discount') }}</dt>
            <dd>−{{ money(purchase.discount) }}</dd>
          </div>
          <div class="grand">
            <dt>{{ $t('purchases.total') }}</dt>
            <dd>{{ money(purchase.total) }}</dd>
          </div>
        </dl>
      </section>

      <form
        v-if="canReceive"
        novalidate
        @submit.prevent="receive"
      >
        <UiCard class="group">
          <h2 class="heading">
            {{ $t('purchases.receiveTitle') }}
          </h2>
          <p class="meta">
            {{ partial ? $t('purchases.receivePartialHint', { count: remaining }, remaining) : $t('purchases.receiveHint') }}
          </p>
          <template
            v-for="line in purchase.lines"
            :key="line.id"
          >
            <UiInput
              v-if="line.quantity > line.receivedQuantity"
              v-model="receiveQuantities[line.id]"
              :label="$t('purchases.receiveLine', { name: `${line.materialName} · ${line.colorName}`, max: line.quantity - line.receivedQuantity })"
              :error="receiveErrors[line.id]"
              inputmode="numeric"
            />
          </template>
          <UiButton
            type="submit"
            block
            :loading="busy"
          >
            {{ $t('purchases.receiveAction') }}
          </UiButton>
        </UiCard>
      </form>
      <NuxtLink
        v-if="purchase.lines.some(line => line.receivedQuantity > 0)"
        to="/inventory"
        class="back"
      >
        {{ $t('purchases.viewStock') }}
      </NuxtLink>

      <UiCard class="group">
        <h2 class="heading">
          {{ $t('purchases.receipt.title') }}
        </h2>
        <p class="meta">
          {{ purchase.hasReceiptFile ? purchase.receiptFileName : $t('purchases.receipt.none') }}
        </p>
        <p
          v-if="receiptError"
          class="error"
          role="alert"
        >
          {{ receiptError }}
        </p>
        <div class="actions">
          <UiButton
            v-if="purchase.hasReceiptFile"
            variant="secondary"
            @click="downloadReceipt"
          >
            {{ $t('purchases.receipt.download') }}
          </UiButton>
          <label
            :for="receiptInputId"
            class="upload"
            :class="{ busy: uploading }"
          >
            {{ purchase.hasReceiptFile ? $t('purchases.receipt.replace') : $t('purchases.receipt.upload') }}
          </label>
          <input
            :id="receiptInputId"
            class="file"
            type="file"
            :accept="acceptedProofAttribute"
            :disabled="uploading"
            @change="uploadReceipt"
          >
          <UiButton
            v-if="purchase.hasReceiptFile"
            variant="ghost"
            @click="removeReceipt"
          >
            {{ $t('purchases.receipt.remove') }}
          </UiButton>
        </div>
      </UiCard>

      <UiButton
        v-if="purchase.status === 'Ordered'"
        variant="secondary"
        block
        @click="ask('cancel')"
      >
        {{ $t('purchases.cancelAction') }}
      </UiButton>
      <UiButton
        v-if="purchase.status === 'Ordered' || purchase.status === 'Cancelled'"
        variant="danger"
        block
        @click="ask('delete')"
      >
        {{ $t('purchases.deleteAction') }}
      </UiButton>

      <UiDialog
        v-model="confirmOpen"
        :title="confirm === 'delete' ? $t('purchases.deleteTitle') : $t('purchases.cancelTitle')"
      >
        <p>{{ confirm === 'delete' ? $t('purchases.deleteBody') : $t('purchases.cancelBody') }}</p>
        <div class="dialog-actions">
          <UiButton
            variant="secondary"
            @click="confirmOpen = false"
          >
            {{ $t('purchases.keep') }}
          </UiButton>
          <UiButton
            :variant="confirm === 'delete' ? 'danger' : 'primary'"
            :loading="busy"
            @click="runConfirmed"
          >
            {{ confirm === 'delete' ? $t('purchases.deleteConfirm') : $t('purchases.cancelConfirm') }}
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
.head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); }
.heading { margin: 0 0 var(--space-2); font-size: var(--font-size-md); }
.group .heading, .head .heading { margin: 0; }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
.feedback { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.lines { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.lines li { display: grid; grid-template-columns: auto 1fr auto; gap: var(--space-3); align-items: start; padding: var(--space-3) var(--space-4); background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.swatch { display: block; width: 28px; height: 28px; margin-top: 2px; border-radius: var(--radius-pill); border: 1px solid var(--border); }
.what { display: grid; gap: 2px; min-width: 0; }
.amount { font-variant-numeric: tabular-nums; }
.totals { margin: var(--space-3) 0 0; display: grid; gap: var(--space-1); }
.totals div { display: flex; justify-content: space-between; gap: var(--space-3); color: var(--muted); }
.totals dd { margin: 0; font-variant-numeric: tabular-nums; }
.totals .grand { color: var(--text); font-weight: 700; }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.file { position: absolute; width: 1px; height: 1px; opacity: 0; overflow: hidden; }
.upload { display: inline-flex; align-items: center; justify-content: center; min-height: var(--tap-size); padding: 0 var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); font-weight: 600; cursor: pointer; }
.upload:has(+ .file:focus-visible) { outline: 2px solid var(--accent); outline-offset: 2px; }
.upload.busy { opacity: 0.6; pointer-events: none; }
.dialog-actions { display: flex; justify-content: flex-end; gap: var(--space-2); margin-top: var(--space-4); }
</style>
