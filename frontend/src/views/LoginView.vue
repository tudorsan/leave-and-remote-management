<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VCard } from 'vuetify/components/VCard'
import { VTextField } from 'vuetify/components/VTextField'
import { useAuthStore } from '../stores/auth'
import '../assets/role-palette.css'

const router = useRouter()
const route = useRoute()
// Afișat când aplicația te-a trimis aici pentru că sesiunea nu mai este valabilă.
const sessionExpired = route.query.session === 'expired'
const auth = useAuthStore()
const email = ref('')
const password = ref('')
const submitting = ref(false)
const errorMessage = ref('')

const roles = [
  { id: 'Employee', name: 'Angajat', detail: 'Consultă calendarul echipei și trimite cereri de concediu sau remote.' },
  { id: 'Manager', name: 'Manager', detail: 'Urmărește planificarea echipei și gestionează cererile.' },
  { id: 'Admin', name: 'Admin', detail: 'Gestionează utilizatorii, echipele și setările globale.' },
] as const

const selectedRole = ref<(typeof roles)[number] | null>(null)
const roleQuestion = 'Cu cine avem de-a face?'

async function chooseRole(role: (typeof roles)[number] | null) {
  if (submitting.value) return
  selectedRole.value = role
  password.value = ''
  errorMessage.value = ''
  await nextTick()
  document.getElementById(role ? 'login-email' : 'role-choice-heading')?.focus()
}

interface LoginResponse {
  token?: string
  message?: string
}

async function login() {
  if (submitting.value || !selectedRole.value) return

  submitting.value = true
  errorMessage.value = ''

  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.value.trim(), password: password.value }),
    })
    const result: LoginResponse | null = await response.json().catch(() => null)

    if (!response.ok || !result?.token) {
      errorMessage.value = result?.message || 'Autentificarea nu a reușit. Verifică dacă serverul API este pornit.'
      return
    }

    // Cardul ales e doar un punct de plecare: rolul real vine din cont (verificat de server),
    // deci un card greșit nu blochează autentificarea.
    const session = await auth.startSession(result.token)
    if (session !== true) {
      errorMessage.value = 'Sesiunea nu a putut fi verificată. Încearcă din nou.'
      return
    }

    await router.replace({ name: 'home' })
  } catch {
    errorMessage.value = 'Nu s-a putut finaliza autentificarea. Încearcă din nou.'
  } finally {
    password.value = ''
    submitting.value = false
  }
}
</script>

<template>
  <div class="login ritm-role" :class="selectedRole && `ritm-role--${selectedRole.id}`">
    <header class="login-header">
      <span class="brand">Ritm</span>
      <RouterLink class="back-link" :to="{ name: 'start' }">← Pagina de start</RouterLink>
    </header>

    <div class="login-layout">
      <VAlert v-if="sessionExpired" class="session-notice" color="info" variant="tonal" role="status">
        Sesiunea ta a expirat sau nu mai este valabilă. Autentifică-te din nou ca să continui.
      </VAlert>
      <section v-if="!selectedRole" class="role-choice" aria-labelledby="role-choice-heading">
        <h1 id="role-choice-heading" class="role-question" tabindex="-1" :aria-label="roleQuestion">
          <span
            v-for="(word, index) in roleQuestion.split(' ')"
            :key="index"
            :style="{ '--word-delay': index * 85 + 'ms' }"
            aria-hidden="true"
          >{{ word }}</span>
        </h1>
        <div class="role-options">
          <button
            v-for="role in roles"
            :key="role.id"
            class="role-option ritm-role"
            :class="`ritm-role--${role.id}`"
            type="button"
            @click="chooseRole(role)"
          >
            <strong>{{ role.name }}</strong>
            <span>{{ role.detail }}</span>
          </button>
        </div>
      </section>

      <VCard v-else class="login-card" theme="light" elevation="0">
        <div class="selected-role">
          <span>{{ selectedRole.name }}</span>
          <VBtn type="button" class="change-role" variant="text" :disabled="submitting" @click="chooseRole(null)">
            Schimbă rolul
          </VBtn>
        </div>
        <h1>Autentificare</h1>
        <p class="login-intro">Intră în cont pentru a accesa calendarul echipei.</p>

        <form class="login-form" @submit.prevent="login">
          <VTextField
            id="login-email"
            v-model="email"
            label="Email"
            name="email"
            type="email"
            autocomplete="username"
            variant="outlined"
            hide-details
            required
          />

          <VTextField
            id="login-password"
            v-model="password"
            label="Parolă"
            name="password"
            type="password"
            autocomplete="current-password"
            variant="outlined"
            hide-details
            required
          />

          <VBtn class="login-action" type="submit" variant="flat" size="large" rounded="pill" block :loading="submitting" :disabled="submitting">
            Intră în cont
          </VBtn>
        </form>

        <VAlert v-if="errorMessage" class="mt-4" color="error" variant="tonal" role="alert">
          {{ errorMessage }}
        </VAlert>

        <p class="forgot-password">
          <RouterLink class="back-link" :to="{ name: 'forgot-password' }">Ai uitat parola?</RouterLink>
        </p>
      </VCard>

    </div>
  </div>
</template>

<style scoped>
.login {
  min-height: 100dvh;
  padding: 0 clamp(16px, 6vw, 72px) 36px;
  background: var(--ritm-page);
  color: var(--ritm-text);
}

.login-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  max-width: 1280px;
  min-height: 82px;
  margin-inline: auto;
  border-bottom: 1px solid var(--ritm-divider);
}

.brand { font-size: 27px; font-weight: 750; letter-spacing: -.045em; }
.back-link { color: var(--ritm-accent); font-weight: 650; }
.back-link:hover { background: transparent; text-decoration: underline; }

.login-layout {
  max-width: 990px;
  margin: clamp(32px, 8vh, 100px) auto 0;
}

.login-card {
  max-width: 32rem;
  margin-inline: auto;
  padding: clamp(22px, 4vw, 44px);
  border: 1px solid var(--ritm-divider);
  border-radius: 24px;
  background: var(--ritm-surface);
  color: var(--ritm-text);
}

.login-layout h1 { font-size: clamp(30px, 4vw, 46px); font-weight: 760; letter-spacing: -.055em; line-height: 1.1; }
.login-intro { margin: 12px 0 28px; color: var(--ritm-muted); }
.selected-role { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 16px; }
.selected-role span { padding: 4px 12px; border-radius: 999px; background: var(--ritm-badge); color: var(--ritm-accent); font-weight: 750; }
.change-role { color: var(--ritm-accent); text-transform: none; }

.login-form {
  display: grid;
  gap: 1rem;
}

.forgot-password { margin-top: 20px; text-align: center; }
.session-notice { max-width: 38rem; margin: 0 auto 24px; }

.login-action { margin-top: 4px; background: var(--ritm-accent); color: #fff; font-weight: 700; text-transform: none; letter-spacing: 0; }
.login-action:focus-visible, .back-link:focus-visible, .change-role:focus-visible, .role-option:focus-visible { outline: 3px solid var(--ritm-focus); outline-offset: 4px; }

@media (prefers-reduced-motion: no-preference) {
  .login-action {
    box-shadow: 0 5px 0 color-mix(in srgb, var(--ritm-accent) 78%, black);
    transition: transform 160ms ease-out, box-shadow 160ms ease-out;
  }
  .login-action:active {
    transform: translateY(5px) scale(.94);
    box-shadow: none;
    transition-duration: 60ms;
  }
}

.role-choice { max-width: 38rem; margin-inline: auto; }
.role-question { display: flex; flex-wrap: wrap; gap: 0 .23em; }
.role-options { display: grid; gap: 12px; margin-top: 24px; }
.role-option {
  display: grid;
  align-content: start;
  gap: 8px;
  padding: clamp(18px, 3vw, 30px);
  border: 1px solid var(--ritm-border-strong);
  border-radius: 16px;
  background: var(--ritm-control);
  color: var(--ritm-accent);
  text-align: left;
  cursor: pointer;
}
.role-option strong { font-size: 23px; font-weight: 750; }
.role-option:hover { background: var(--ritm-accent); color: #fff; }

@media (prefers-reduced-motion: no-preference) {
  .role-question span { animation: word-drop 360ms cubic-bezier(.22, .8, .35, 1) var(--word-delay) both; }
  .role-option { animation: role-enter 320ms ease-out both; }
  .role-option:nth-child(1) { animation-delay: 690ms; }
  .role-option:nth-child(2) { animation-delay: 810ms; }
  .role-option:nth-child(3) { animation-delay: 930ms; }
}

@keyframes word-drop {
  from { opacity: 0; transform: translateY(-28px); }
  78% { opacity: 1; transform: translateY(3px); }
  to { opacity: 1; transform: translateY(0); }
}

@keyframes role-enter {
  from { opacity: 0; transform: translateY(18px); }
  to { opacity: 1; transform: translateY(0); }
}

@media (max-width: 700px) {
  .login-header { min-height: 66px; }
  .login-layout { max-width: 500px; }
}
</style>
