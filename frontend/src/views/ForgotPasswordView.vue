<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VCard } from 'vuetify/components/VCard'
import { VTextField } from 'vuetify/components/VTextField'
import '../assets/role-palette.css'
import '../assets/auth-page.css'

// Cerința 3: primul pas al resetării. Răspunsul nu spune dacă adresa există.
const email = ref('')
const submitting = ref(false)
const notice = ref('')
const errorMessage = ref('')

async function requestReset() {
  if (submitting.value || !email.value.trim()) return
  submitting.value = true
  notice.value = ''
  errorMessage.value = ''
  try {
    const response = await fetch('/api/auth/forgot-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.value.trim() }),
    })
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      errorMessage.value = result?.message || 'Cererea nu a putut fi trimisă. Încearcă din nou.'
      return
    }
    notice.value = result?.message || 'Dacă adresa aparține unui cont activ, vei primi un email cu un link de resetare.'
  } catch {
    errorMessage.value = 'Cererea nu a putut fi trimisă. Verifică dacă serverul API este pornit.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="auth-page ritm-role">
    <header class="auth-header">
      <span class="auth-brand">Ritm</span>
      <RouterLink class="auth-link" :to="{ name: 'login' }">← Înapoi la autentificare</RouterLink>
    </header>

    <VCard class="auth-card" theme="light" elevation="0">
      <h1>Ai uitat parola?</h1>
      <p class="auth-intro">Scrie adresa de email a contului. Îți trimitem un link cu care îți alegi o parolă nouă.</p>

      <form class="auth-form" @submit.prevent="requestReset">
        <VTextField
          v-model="email"
          label="Email"
          name="email"
          type="email"
          autocomplete="username"
          variant="outlined"
          hide-details
          required
          :disabled="submitting"
        />
        <VBtn class="auth-action" type="submit" variant="flat" size="large" rounded="pill" block :loading="submitting" :disabled="submitting || !email.trim()">
          Trimite linkul de resetare
        </VBtn>
      </form>

      <VAlert v-if="notice" class="mt-4" color="success" variant="tonal" role="status">{{ notice }}</VAlert>
      <VAlert v-if="errorMessage" class="mt-4" color="error" variant="tonal" role="alert">{{ errorMessage }}</VAlert>
    </VCard>
  </div>
</template>
