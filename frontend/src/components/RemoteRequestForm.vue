<script setup lang="ts">
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VTextarea } from 'vuetify/components/VTextarea'

defineProps<{
  editingRequestId: number | null
  remoteDays: string[]
  remoteError: string
  remoteSubmitting: boolean
  submitDisabled: boolean
  formatRequestDate: (date: string) => string
}>()
const remoteComment = defineModel<string>('remoteComment', { required: true })
const emit = defineEmits<{ close: []; submit: [] }>()
</script>

<template>
  <form class="request-panel" aria-labelledby="remote-heading" @submit.prevent="emit('submit')">
    <div class="request-form-header">
      <h2 id="remote-heading" tabindex="-1" class="text-h6">
        {{ editingRequestId === null ? 'Solicită lucru remote' : 'Editează cererea de lucru remote' }}
      </h2>
      <VBtn type="button" variant="outlined" :disabled="remoteSubmitting" @click="emit('close')">
        <span aria-hidden="true">←&nbsp;</span>Înapoi
      </VBtn>
    </div>
    <p class="mb-2">
      {{ editingRequestId === null
        ? 'Selectează zilele din calendar. Fiecare zi devine o cerere separată, care poate fi anulată individual. Cererile remote pentru sărbători legale sunt respinse.'
        : 'Alege din calendar ziua nouă pentru această cerere.' }}
    </p>
    <div class="remote-selection mb-3">
      <p role="status">
        {{ remoteDays.length === 0 ? 'Alege o zi din calendar.' : remoteDays.length === 1 ? '1 zi selectată' : `${remoteDays.length} zile selectate` }}
      </p>
      <ul v-if="remoteDays.length" class="selected-dates" aria-label="Zile remote selectate">
        <li v-for="date in remoteDays" :key="date">
          <time :datetime="date">{{ formatRequestDate(date) }}</time>
        </li>
      </ul>
    </div>
    <VAlert v-if="remoteError" class="mb-3" color="error" variant="tonal" role="alert">
      {{ remoteError }}
    </VAlert>
    <VTextarea v-model="remoteComment" label="Comentariu (opțional)" rows="2" :disabled="remoteSubmitting" />
    <div class="month-actions">
      <VBtn
        color="primary"
        type="submit"
        :loading="remoteSubmitting"
        :disabled="submitDisabled"
      >{{ editingRequestId === null ? 'Trimite cererea remote' : 'Salvează modificările' }}</VBtn>
    </div>
  </form>
</template>

<style scoped src="../assets/request-form.css"></style>

<style scoped>
.remote-selection { padding: 10px 14px; border: 1px solid var(--ritm-border); border-radius: 10px; background: var(--ritm-surface); color: var(--ritm-accent); }
.remote-selection > p { font-weight: 700; }
.selected-dates { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 6px; padding: 0; list-style: none; }
.selected-dates li { padding: 3px 9px; border-radius: 999px; background: var(--ritm-highlight); font-size: 14px; }
</style>
