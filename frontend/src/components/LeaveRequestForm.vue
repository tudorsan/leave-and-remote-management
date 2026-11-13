<script setup lang="ts">
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VSelect } from 'vuetify/components/VSelect'
import { VTextField } from 'vuetify/components/VTextField'
import { VTextarea } from 'vuetify/components/VTextarea'

defineProps<{
  editingRequestId: number | null
  leaveError: string
  leaveSubmitting: boolean
  leaveTypeOptions: { title: string; value: string }[]
  submitDisabled: boolean
}>()
const leaveStart = defineModel<string>('leaveStart', { required: true })
const leaveEnd = defineModel<string>('leaveEnd', { required: true })
const leaveType = defineModel<string | null>('leaveType', { required: true })
const leaveComment = defineModel<string>('leaveComment', { required: true })
const emit = defineEmits<{ close: []; submit: [] }>()
</script>

<template>
  <form class="request-panel" aria-labelledby="leave-heading" @submit.prevent="emit('submit')">
    <div class="request-form-header">
      <h2 id="leave-heading" tabindex="-1" class="text-h6">
        {{ editingRequestId === null ? 'Solicită concediu' : 'Editează cererea de concediu' }}
      </h2>
      <VBtn type="button" variant="outlined" :disabled="leaveSubmitting" @click="emit('close')">
        <span aria-hidden="true">←&nbsp;</span>Înapoi
      </VBtn>
    </div>
    <VAlert v-if="leaveError" class="mb-3" color="error" variant="tonal" role="alert">
      {{ leaveError }}
    </VAlert>
    <fieldset class="leave-group">
      <legend>Perioada concediului</legend>
      <p class="leave-holiday-note">Sărbătorile legale din interval sunt excluse din calculul zilelor de concediu.</p>
      <div class="leave-fields">
        <VTextField v-model="leaveStart" type="date" label="Data de început" required :disabled="leaveSubmitting" variant="outlined" />
        <VTextField v-model="leaveEnd" type="date" label="Data de sfârșit" required :disabled="leaveSubmitting" variant="outlined" />
      </div>
    </fieldset>
    <fieldset class="leave-group">
      <legend>Detaliile cererii</legend>
      <VSelect v-model="leaveType" label="Tip de concediu" :items="leaveTypeOptions" :disabled="leaveSubmitting" variant="outlined" />
      <VTextarea v-model="leaveComment" label="Comentariu (opțional)" rows="2" :disabled="leaveSubmitting" variant="outlined" />
    </fieldset>
    <div class="month-actions leave-actions">
      <VBtn
        class="leave-submit"
        color="primary"
        type="submit"
        :loading="leaveSubmitting"
        :disabled="submitDisabled"
      >{{ editingRequestId === null ? 'Trimite cererea' : 'Salvează modificările' }}</VBtn>
    </div>
  </form>
</template>

<style scoped src="../assets/request-form.css"></style>

<style scoped>
.leave-group {
  min-width: 0;
  margin-bottom: 14px;
  padding: 14px 16px 0;
  border: 1px solid var(--ritm-border);
  border-radius: 12px;
  background: var(--ritm-surface);
}
.leave-group legend { padding-inline: 6px; color: var(--ritm-accent); font-size: 15px; font-weight: 750; }
.leave-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); column-gap: 16px; }
.leave-actions { justify-content: flex-end; padding-top: 4px; }
.leave-holiday-note { margin-bottom: 12px; color: var(--ritm-muted); font-size: 0.88rem; }

@media (max-width: 640px) {
  .leave-fields { grid-template-columns: 1fr; }
}
</style>
