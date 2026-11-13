<script setup lang="ts">
import { ref } from 'vue'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { useAuthStore } from '../stores/auth'

// Cerința 6: exportul propriilor zile aprobate. Fișierele sunt generate de backend,
// care include doar cererile aprobate ale utilizatorului autentificat.
const emit = defineEmits<{ unauthorized: [] }>()
const auth = useAuthStore()
const downloading = ref<'ics' | 'csv' | null>(null)
const error = ref('')

const formats = {
  ics: { url: '/api/exports/my-approved-days.ics', fileName: 'zile-aprobate.ics' },
  csv: { url: '/api/exports/my-approved-days.csv', fileName: 'zile-aprobate.csv' },
} as const

async function download(format: keyof typeof formats) {
  if (downloading.value) return
  downloading.value = format
  error.value = ''
  try {
    // Descărcarea are nevoie de tokenul de autentificare, deci fișierul se cere prin fetch, nu printr-un link simplu.
    const response = await fetch(formats[format].url, {
      headers: { Authorization: `Bearer ${auth.getToken() ?? ''}` },
    })
    if (response.status === 401) {
      emit('unauthorized')
      return
    }
    if (!response.ok) throw new Error('Export unavailable')

    const url = URL.createObjectURL(await response.blob())
    const link = document.createElement('a')
    link.href = url
    link.download = formats[format].fileName
    document.body.append(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  } catch {
    error.value = 'Fișierul nu a putut fi descărcat. Încearcă din nou.'
  } finally {
    downloading.value = null
  }
}
</script>

<template>
  <div class="approved-export">
    <p class="approved-export-text">Exportă zilele tale aprobate (concedii și lucru remote):</p>
    <div class="approved-export-actions">
      <VBtn variant="outlined" size="small" :loading="downloading === 'ics'" :disabled="!!downloading" @click="download('ics')">
        Calendar (.ics)
      </VBtn>
      <VBtn variant="outlined" size="small" :loading="downloading === 'csv'" :disabled="!!downloading" @click="download('csv')">
        Excel (.csv)
      </VBtn>
    </div>
    <VAlert v-if="error" class="mt-2" color="error" variant="tonal" role="alert">{{ error }}</VAlert>
  </div>
</template>

<style scoped>
.approved-export {
  margin-bottom: 14px;
  padding: 10px 14px;
  border: 1px solid var(--ritm-border);
  border-radius: 10px;
  background: var(--ritm-surface);
}
.approved-export-text { margin-bottom: 6px; color: var(--ritm-muted); font-size: 0.9rem; }
.approved-export-actions { display: flex; flex-wrap: wrap; gap: 8px; }
</style>
