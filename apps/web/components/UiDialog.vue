<script setup lang="ts">
defineProps<{ title: string }>()
const open = defineModel<boolean>({ required: true })
const dialog = ref<HTMLDialogElement>()
const titleId = useId()

watch(open, (isOpen) => {
  if (isOpen && !dialog.value?.open) dialog.value?.showModal()
  if (!isOpen && dialog.value?.open) dialog.value.close()
})

function onClose() {
  open.value = false
}
</script>

<template>
  <dialog
    ref="dialog"
    class="dialog"
    :aria-labelledby="titleId"
    @close="onClose"
  >
    <header>
      <h2 :id="titleId">
        {{ title }}
      </h2>
      <button
        type="button"
        class="close"
        :aria-label="$t('common.close')"
        @click="open = false"
      >
        ✕
      </button>
    </header>
    <div class="body">
      <slot />
    </div>
    <footer v-if="$slots.actions">
      <slot name="actions" />
    </footer>
  </dialog>
</template>

<style scoped>
.dialog { width: min(92vw, 440px); padding: var(--space-4); color: var(--text); background: var(--bg-elevated); border: 1px solid var(--border); border-radius: var(--radius-lg); box-shadow: var(--shadow); }
.dialog::backdrop { background: var(--overlay); }
header { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
h2 { margin: 0; font-size: var(--font-size-lg); }
.close { min-width: var(--tap-size); min-height: var(--tap-size); background: transparent; color: var(--muted); border: 0; border-radius: var(--radius-pill); font-size: 1.1rem; cursor: pointer; }
.body { margin: var(--space-3) 0; }
footer { display: flex; justify-content: flex-end; gap: var(--space-2); }
</style>
