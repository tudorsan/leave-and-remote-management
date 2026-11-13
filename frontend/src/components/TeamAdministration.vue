<script setup lang="ts">
import { computed, ref } from 'vue'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VTextField } from 'vuetify/components/VTextField'
import { useAuthStore } from '../stores/auth'
import { matchesName } from '../utils/nameSearch'
import ConfirmDialog from './ConfirmDialog.vue'

interface Team {
  id: number
  name: string
  isActive: boolean
}

const props = defineProps<{ teams: Team[]; loading: boolean; loadError: string }>()
const emit = defineEmits<{ unauthorized: []; saved: [team: Team]; reload: [] }>()
const auth = useAuthStore()
const form = ref<{ id: number | null; name: string } | null>(null)
const saving = ref(false)
const error = ref('')
const notice = ref('')
const teamSearch = ref('')
const teamToDeactivate = ref<Team | null>(null)
const filteredTeams = computed(() => props.teams.filter(team => matchesName(team.name, teamSearch.value)))

function openForm(team?: Team) {
  form.value = { id: team?.id ?? null, name: team?.name ?? '' }
  error.value = ''
  notice.value = ''
}

async function saveTeam(path: string, method: 'POST' | 'PUT' | 'PATCH', confirmation: string, name?: string) {
  if (saving.value) return
  saving.value = true
  error.value = ''
  notice.value = ''
  try {
    const response = await fetch(`/api/teams${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${auth.getToken() ?? ''}`,
        ...(name === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: name === undefined ? undefined : JSON.stringify({ name }),
    })
    if (response.status === 401) {
      emit('unauthorized')
      return
    }
    if (!response.ok) {
      const result = await response.json().catch(() => null)
      throw new Error(result?.message || 'Modificarea echipei nu a putut fi salvată.')
    }
    const team: Team = await response.json()
    emit('saved', team)
    form.value = null
    notice.value = confirmation
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : 'Modificarea echipei nu a putut fi salvată.'
  } finally {
    saving.value = false
  }
}

function submitTeam() {
  if (!form.value) return
  const { id } = form.value
  const name = form.value.name.trim()
  if (!name) return
  if (id === null) void saveTeam('', 'POST', 'Echipa a fost creată.', name)
  else void saveTeam(`/${id}`, 'PUT', 'Numele echipei a fost actualizat.', name)
}

function deactivate(team: Team) {
  teamToDeactivate.value = null
  void saveTeam(`/${team.id}/deactivate`, 'PATCH', 'Echipa a fost dezactivată.')
}
</script>

<template>
  <section class="mt-4" aria-labelledby="team-admin-heading">
    <h2 id="team-admin-heading" class="text-h6">Echipe</h2>
    <p v-if="loading" role="status">Se încarcă echipele…</p>
    <VAlert v-if="loadError || error" class="my-2" color="error" variant="tonal" role="alert">{{ loadError || error }}</VAlert>
    <VAlert v-if="notice" class="my-2" color="success" variant="tonal" role="status">{{ notice }}</VAlert>
    <VBtn v-if="loadError" variant="text" :disabled="loading" @click="emit('reload')">Reîncarcă lista de echipe</VBtn>
    <template v-else-if="!loading">
      <div class="team-toolbar">
        <VTextField v-model="teamSearch" label="Caută echipă" placeholder="Numele echipei" variant="outlined" density="compact" hide-details />
        <VBtn v-if="!form" color="primary" :disabled="saving" @click="openForm()">Creează echipă</VBtn>
      </div>
      <form v-if="form" class="team-form my-3" @submit.prevent="submitTeam">
        <VTextField v-model="form.name" label="Numele echipei" required :disabled="saving" />
        <VBtn type="submit" color="primary" :loading="saving" :disabled="saving || !form.name.trim()">Salvează echipa</VBtn>
        <VBtn type="button" variant="text" :disabled="saving" @click="form = null; error = ''">Renunță</VBtn>
      </form>
      <p v-if="!filteredTeams.length" class="mt-3" role="status">{{ teamSearch.trim() ? 'Nu am găsit nicio echipă.' : 'Nu există echipe.' }}</p>
      <div v-else class="admin-table-scroll" role="region" aria-labelledby="team-admin-heading" tabindex="0">
        <table class="admin-table" aria-labelledby="team-admin-heading">
          <thead>
            <tr><th scope="col">Numele echipei</th><th scope="col">Stare</th><th scope="col">Acțiuni</th></tr>
          </thead>
          <tbody>
            <tr v-for="team in filteredTeams" :key="team.id">
              <th scope="row">{{ team.name }}</th>
              <td><span class="admin-record-status" :class="{ 'is-active': team.isActive }">{{ team.isActive ? 'Activă' : 'Dezactivată' }}</span></td>
              <td>
                <div class="admin-row-actions">
                  <VBtn variant="text" size="small" :disabled="saving || !!form" @click="openForm(team)">Redenumește</VBtn>
                  <VBtn v-if="team.isActive" variant="text" size="small" color="error" :disabled="saving || !!form" @click="teamToDeactivate = team">Dezactivează</VBtn>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
    <ConfirmDialog v-if="teamToDeactivate" title="Dezactivezi echipa?" :message="teamToDeactivate.name" confirm-label="Dezactivează echipa" @dismiss="teamToDeactivate = null" @confirm="deactivate(teamToDeactivate)" />
  </section>
</template>

<style scoped src="../assets/admin-table.css"></style>

<style scoped>
.team-form { max-width: 32rem; }
.team-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-top: 12px; }
.team-toolbar > .v-input { flex: 0 1 24rem; }
</style>
