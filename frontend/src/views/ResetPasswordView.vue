<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VCard } from 'vuetify/components/VCard'
import { VTextField } from 'vuetify/components/VTextField'
import { useAuthStore } from '../stores/auth'
import '../assets/role-palette.css'
import '../assets/auth-page.css'

// Cerința 3: al doilea pas al resetării, deschis din linkul primit pe email (?token=...).
const route = useRoute()
const auth = useAuthStore()
const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : ''))
const password = ref('')
const confirmation = ref('')
const submitting = ref(false)
const done = ref(false)
const errorMessage = ref('')

const mismatch = computed(() => confirmation.value.length > 0 && password.value !== confirmation.value)
const canSubmit = computed(() =>
  !!token.value && password.value.length >= 8 && password.value === confirmation.value && !submitting.value)

async function resetPassword() {
  if (!canSubmit.value) return
  submitting.value = true
  errorMessage.value = ''
  try {
    const response = await fetch('/api/auth/reset-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: token.value, newPassword: password.value }),
    })
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      errorMessage.value = result?.message || 'Parola nu a putut fi schimbată.'
      return
    }
    // Sesiunile vechi sunt invalidate de server; o eventuală sesiune locală se închide și aici.
    auth.signOut()
    done.value = true
  } catch {
    errorMessage.value = 'Parola nu a putut fi schimbată. Verifică dacă serverul API este pornit.'
  } finally {
    password.value = ''
    confirmation.value = ''
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
      <h1>Alege o parolă nouă</h1>

      <template v-if="done">
        <VAlert class="mt-4" color="success" variant="tonal" role="status">
          Parola a fost schimbată. Te poți autentifica cu parola nouă.
        </VAlert>
        <p class="auth-footer"><RouterLink class="auth-link" :to="{ name: 'login' }">Mergi la autentificare</RouterLink></p>
      </template>

      <template v-else-if="!token">
        <VAlert class="mt-4" color="error" variant="tonal" role="alert">
          Linkul de resetare este incomplet. Deschide linkul exact din email sau cere unul nou.
        </VAlert>
        <p class="auth-footer"><RouterLink class="auth-link" :to="{ name: 'forgot-password' }">Cere un link nou</RouterLink></p>
      </template>

      <template v-else>
        <p class="auth-intro">Parola trebuie să aibă cel puțin 8 caractere. După schimbare, toate sesiunile deschise se închid.</p>
        <form class="auth-form" @submit.prevent="resetPassword">
          <VTextField
            v-model="password"
            label="Parola nouă"
            type="password"
            autocomplete="new-password"
            variant="outlined"
            hide-details="auto"
            :rules="[() => password.length === 0 || password.length >= 8 || 'Minimum 8 caractere.']"
            :disabled="submitting"
            required
          />
          <VTextField
            v-model="confirmation"
            label="Confirmă parola nouă"
            type="password"
            autocomplete="new-password"
            variant="outlined"
            hide-details="auto"
            :error-messages="mismatch ? 'Parolele nu coincid.' : []"
            :disabled="submitting"
            required
          />
          <VBtn class="auth-action" type="submit" variant="flat" size="large" rounded="pill" block :loading="submitting" :disabled="!canSubmit">
            Salvează parola nouă
          </VBtn>
        </form>
        <VAlert v-if="errorMessage" class="mt-4" color="error" variant="tonal" role="alert">{{ errorMessage }}</VAlert>
        <p v-if="errorMessage" class="auth-footer"><RouterLink class="auth-link" :to="{ name: 'forgot-password' }">Cere un link nou</RouterLink></p>
      </template>
    </VCard>
  </div>
</template>
