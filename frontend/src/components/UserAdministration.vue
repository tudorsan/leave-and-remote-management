<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VSelect } from 'vuetify/components/VSelect'
import { VTextField } from 'vuetify/components/VTextField'
import { useAuthStore } from '../stores/auth'
import { matchesName } from '../utils/nameSearch'
import ConfirmDialog from './ConfirmDialog.vue'
import AnnualLeaveAllocation from './AnnualLeaveAllocation.vue'

const emit = defineEmits<{ unauthorized: [] }>()
const auth = useAuthStore()
const roles = { Employee: 'Angajat', Manager: 'Manager', Admin: 'Administrator' }
type Role = keyof typeof roles

interface UserFields {
  firstName: string
  lastName: string
  email: string
  role: Role
  teamId: number | null
  managerId: number | null
}

interface User extends UserFields {
  id: number
  isActive: boolean
}

interface Team {
  id: number
  name: string
  isActive: boolean
}

const props = defineProps<{ teams: Team[]; teamsUnavailable: boolean }>()
const users = ref<User[]>([])
const form = ref<(UserFields & { id: number | null; password: string }) | null>(null)
const busy = ref(false)
const loaded = ref(false)
const error = ref('')
const notice = ref('')
const userSearch = ref('')
const userToDeactivate = ref<User | null>(null)
const roleOptions = Object.entries(roles).map(([value, title]) => ({ value, title }))
const roleGroups = computed(() => ([
  { role: 'Admin', title: 'Administratori' },
  { role: 'Manager', title: 'Manageri' },
  { role: 'Employee', title: 'Angajați' },
] as const).map(group => ({
  ...group,
  users: users.value.filter(user => user.role === group.role &&
    matchesName(`${user.firstName} ${user.lastName} ${user.email}`, userSearch.value)),
})).filter(group => group.users.length > 0))
const teamNames = computed(() => new Map(props.teams.map(team => [team.id, team.name])))
const teamOptions = computed(() => props.teams
  .filter(team => team.isActive)
  .map(team => ({ value: team.id, title: team.name })))
const managerOptions = computed(() => users.value
  .filter(user => user.isActive && user.id !== form.value?.id &&
    (user.role === 'Admin' || (user.role === 'Manager' &&
      (form.value?.role !== 'Employee' || user.teamId === form.value.teamId))))
  .map(user => ({ value: user.id, title: `${user.firstName} ${user.lastName} (${user.email})` })))

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T | null> {
  const response = await fetch(`/api/${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${auth.getToken() ?? ''}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (response.status === 401) {
    emit('unauthorized')
    return null
  }
  if (!response.ok) {
    const result = await response.json().catch(() => null)
    throw new Error(result?.message || 'Operația nu a putut fi finalizată.')
  }
  return response.json() as Promise<T>
}

async function loadUsers() {
  busy.value = true
  error.value = ''
  try {
    const userList = await request<User[]>('users')
    if (!userList) return
    users.value = userList
    loaded.value = true
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : 'Lista nu a putut fi încărcată.'
  } finally {
    busy.value = false
  }
}

function openForm(user?: User) {
  error.value = ''
  notice.value = ''
  form.value = user
    ? { ...user, password: '' }
    : { id: null, firstName: '', lastName: '', email: '', password: '', role: 'Employee', teamId: null, managerId: null }
}

function clearUnavailableManager() {
  if (form.value && !managerOptions.value.some(option => option.value === form.value?.managerId)) {
    form.value.managerId = null
  }
}

async function saveUser() {
  if (!form.value || busy.value) return
  const { id, firstName, lastName, email, role, teamId, managerId, password } = form.value
  const creating = id === null
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    const result = await request<User>(creating ? 'users' : `users/${id}`, creating ? 'POST' : 'PUT', {
      firstName, lastName, email, role, teamId, managerId,
      ...(creating ? { password } : {}),
    })
    if (!result) return
    users.value = creating
      ? [...users.value, result]
      : users.value.map(user => user.id === result.id ? result : user)
    form.value = null
    notice.value = creating ? 'Contul a fost creat.' : 'Modificările au fost salvate.'
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : 'Contul nu a putut fi salvat.'
  } finally {
    busy.value = false
  }
}

async function deactivate(user: User) {
  userToDeactivate.value = null
  await setActive(user, false)
}

async function setActive(user: User, active: boolean) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    const action = active ? 'reactivate' : 'deactivate'
    const result = await request<Pick<User, 'id' | 'isActive'>>(`users/${user.id}/${action}`, 'PATCH')
    if (!result) return
    user.isActive = result.isActive
    notice.value = active
      ? 'Contul a fost reactivat.'
      : 'Contul a fost dezactivat, iar cererile lui în așteptare au fost anulate.'
  } catch (failure) {
    error.value = failure instanceof Error
      ? failure.message
      : active ? 'Contul nu a putut fi reactivat.' : 'Contul nu a putut fi dezactivat.'
  } finally {
    busy.value = false
  }
}

onMounted(loadUsers)
</script>

<template>
  <section class="mt-4" aria-labelledby="users-heading">
    <h2 id="users-heading" class="text-h6">Utilizatori</h2>
    <VAlert v-if="error" class="my-2" color="error" variant="tonal" role="alert">{{ error }}</VAlert>
    <VAlert v-if="notice" class="my-2" color="success" variant="tonal" role="status">{{ notice }}</VAlert>
    <p v-if="!loaded && busy" role="status">Se încarcă utilizatorii…</p>
    <VBtn v-if="!loaded && error" variant="text" :disabled="busy" @click="loadUsers">Reîncarcă utilizatorii</VBtn>
    <template v-if="loaded">
      <div class="user-toolbar">
        <VTextField v-model="userSearch" label="Caută utilizator" placeholder="Nume sau email" variant="outlined" density="compact" hide-details />
        <VBtn v-if="!form" color="primary" :disabled="busy || teamsUnavailable" @click="openForm()">Creează utilizator</VBtn>
      </div>
      <form v-if="form" class="user-form my-3" @submit.prevent="saveUser">
        <h3 class="text-subtitle-1 mb-2">{{ form.id === null ? 'Cont nou' : `Fișa utilizatorului · ${form.firstName} ${form.lastName}` }}</h3>
        <VTextField v-model="form.firstName" label="Prenume" required :disabled="busy" />
        <VTextField v-model="form.lastName" label="Nume" required :disabled="busy" />
        <VTextField v-model="form.email" type="email" label="Email" required :disabled="busy" />
        <VTextField v-if="form.id === null" v-model="form.password" type="password" label="Parolă" autocomplete="new-password" minlength="8" required :disabled="busy" />
        <VSelect v-model="form.role" label="Rol" :items="roleOptions" :disabled="busy" @update:model-value="clearUnavailableManager" />
        <VSelect v-model="form.teamId" label="Echipă" :items="[{ value: null, title: 'Fără echipă' }, ...teamOptions]" :disabled="busy" @update:model-value="clearUnavailableManager" />
        <VSelect v-model="form.managerId" label="Manager direct" :items="[{ value: null, title: 'Fără manager direct' }, ...managerOptions]" :disabled="busy" />
        <VBtn type="submit" color="primary" :loading="busy" :disabled="busy">Salvează utilizatorul</VBtn>
        <VBtn type="button" variant="text" :disabled="busy" @click="form = null; error = ''">Renunță</VBtn>
      </form>
      <AnnualLeaveAllocation v-if="form && form.id !== null" :key="form.id" :employee-id="form.id" @unauthorized="emit('unauthorized')" />
      <p v-if="!roleGroups.length" class="mt-3" role="status">{{ userSearch.trim() ? 'Nu am găsit niciun utilizator.' : 'Nu există utilizatori.' }}</p>
      <div v-else class="admin-table-scroll" role="region" aria-label="Utilizatori grupați după rol" tabindex="0">
        <table v-for="group in roleGroups" :key="group.role" class="admin-table user-table">
          <caption>{{ group.title }}</caption>
          <thead>
            <tr><th scope="col">Nume</th><th scope="col">Email</th><th scope="col">Echipă</th><th scope="col">Stare</th><th scope="col">Acțiuni</th></tr>
          </thead>
          <tbody>
            <tr v-for="user in group.users" :key="user.id">
              <th scope="row">{{ user.firstName }} {{ user.lastName }}</th>
              <td>{{ user.email }}</td>
              <td>{{ user.teamId === null ? 'Fără echipă' : (teamNames.get(user.teamId) ?? '—') }}</td>
              <td><span class="admin-record-status" :class="{ 'is-active': user.isActive }">{{ user.isActive ? 'Activ' : 'Dezactivat' }}</span></td>
              <td>
                <div class="admin-row-actions">
                  <VBtn variant="text" size="small" :disabled="busy || !!form || teamsUnavailable" @click="openForm(user)">Editează</VBtn>
                  <VBtn v-if="user.isActive && user.id !== Number(auth.user?.userId)" variant="text" size="small" color="error" :disabled="busy || !!form" @click="userToDeactivate = user">Dezactivează</VBtn>
                  <VBtn v-if="!user.isActive" variant="text" size="small" color="primary" :disabled="busy || !!form" @click="setActive(user, true)">Reactivează</VBtn>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
    <ConfirmDialog v-if="userToDeactivate" title="Dezactivezi contul?" :message="userToDeactivate.email" confirm-label="Dezactivează contul" @dismiss="userToDeactivate = null" @confirm="deactivate(userToDeactivate)" />
  </section>
</template>

<style scoped src="../assets/admin-table.css"></style>

<style scoped>
.user-form { max-width: 32rem; }
.user-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-top: 12px; }
.user-toolbar > .v-input { flex: 0 1 24rem; }
.user-table { min-width: 46rem; table-layout: fixed; }
.user-table :is(th, td) { overflow-wrap: anywhere; }
.user-table thead th:nth-child(2), .user-table thead th:last-child { width: 25%; }
</style>
