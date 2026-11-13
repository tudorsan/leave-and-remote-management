<script setup lang="ts">
import { onMounted, ref, useId } from 'vue'

defineProps<{ title: string; message: string; confirmLabel: string }>()
const emit = defineEmits<{ confirm: []; dismiss: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const titleId = useId()
const messageId = useId()

onMounted(() => dialog.value?.showModal())

function confirm() {
  dialog.value?.close()
  emit('confirm')
}

function dismiss() {
  dialog.value?.close()
  emit('dismiss')
}
</script>

<template>
  <dialog ref="dialog" class="confirm-dialog" :aria-labelledby="titleId" :aria-describedby="messageId" @cancel.prevent="dismiss">
    <h2 :id="titleId">{{ title }}</h2>
    <p :id="messageId">{{ message }}</p>
    <div class="confirm-actions">
      <button type="button" autofocus @click="dismiss">Renunță</button>
      <button type="button" class="confirm-primary" @click="confirm">{{ confirmLabel }}</button>
    </div>
  </dialog>
</template>

<style scoped>
.confirm-dialog {
  position: fixed;
  inset: 0;
  width: min(28rem, calc(100% - 32px));
  max-height: calc(100% - 32px);
  overflow: auto;
  margin: auto;
  padding: 24px;
  border: 1px solid var(--ritm-border);
  border-radius: 16px;
  background: var(--ritm-surface);
  color: var(--ritm-text);
  box-shadow: 0 20px 60px #18202d38;
}
.confirm-dialog::backdrop { background: #18202d72; }
.confirm-dialog h2 { font-size: 21px; font-weight: 750; }
.confirm-dialog p { margin-top: 8px; color: var(--ritm-muted); overflow-wrap: anywhere; }
.confirm-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-top: 22px; }
.confirm-actions button { padding: 8px 14px; border: 1px solid var(--ritm-border); border-radius: 8px; font-weight: 650; }
.confirm-actions button:hover { background: var(--ritm-panel); }
.confirm-actions .confirm-primary { border-color: var(--ritm-accent); background: var(--ritm-accent); color: #fff; }
.confirm-actions .confirm-primary:hover { background: var(--ritm-accent-secondary); }
.confirm-actions button:focus-visible { outline: 2px solid var(--ritm-focus); outline-offset: 3px; }
</style>
