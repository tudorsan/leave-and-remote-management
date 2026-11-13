<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VAutocomplete } from 'vuetify/components/VAutocomplete'
import { VTextField } from 'vuetify/components/VTextField'
import { useAuthStore } from '../stores/auth'
import LeaveBalanceSummary from './LeaveBalanceSummary.vue'
import { matchesName } from '../utils/nameSearch'

const props = defineProps<{ employeeId?: number }>()
const emit = defineEmits<{ unauthorized: [] }>()
const auth = useAuthStore()
const canSetAllocation = computed(() => props.employeeId !== undefined && auth.user?.role === 'Admin')

interface User {
  id: number
  firstName: string
  lastName: string
  email: string
}

interface Allocation {
  userId: number
  year: number
  totalDays: number
  usedDays: number
  pendingDays: number
  availableDays: number
}

const users = ref<User[]>([])
const selectedUserId = ref<number | null>(null)
const userId = computed(() => props.employeeId ?? selectedUserId.value)
const year = ref(String(new Date().getFullYear()))
const totalDays = ref('')
const allocation = ref<Allocation | null>(null)
const busy = ref(false)
const error = ref('')
const saved = ref(false)
const userOptions = computed(() => users.value.map(user => ({
  title: `${user.firstName} ${user.lastName} (${user.email})`,
  value: user.id,
})))
const validYear = computed(() => /^\d{1,4}$/.test(year.value) && Number(year.value) > 0)
const validDays = computed(() => /^\d+$/.test(totalDays.value) && Number(totalDays.value) <= 2147483647)

watch([userId, year], () => {
  allocation.value = null
  saved.value = false
  error.value = ''
})

async function request<T>(url: string, options?: RequestInit): Promise<T | null> {
  const response = await fetch(url, {
    ...options,
    headers: { Authorization: `Bearer ${auth.getToken() ?? ''}`, ...options?.headers },
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
    users.value = await request<User[]>('/api/users') ?? []
  } catch {
    error.value = 'Lista utilizatorilor nu a putut fi încărcată.'
  } finally {
    busy.value = false
  }
}

async function updateAllocation(save: boolean) {
  if (busy.value || userId.value === null || !validYear.value) return
  if (save && (!canSetAllocation.value || !allocation.value || !validDays.value)) return

  busy.value = true
  error.value = ''
  saved.value = false
  const base = `/api/leave-balances/${userId.value}`
  const selectedYear = Number(year.value)
  try {
    const result = save
      ? await request<Allocation>(`${base}/${selectedYear}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ totalDays: Number(totalDays.value) }),
        })
      : await request<Allocation>(`${base}?year=${selectedYear}`)
    if (!result) return
    allocation.value = result
    totalDays.value = String(result.totalDays)
    saved.value = save
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : 'Operația nu a putut fi finalizată.'
  } finally {
    busy.value = false
  }
}

onMounted(() => {
  if (props.employeeId === undefined) void loadUsers()
  else void updateAllocation(false)
})
</script>

<template>
  <section class="allocation-panel" aria-labelledby="allocation-heading">
    <h2 id="allocation-heading" class="text-h6 mb-3">{{ canSetAllocation ? 'Alocare anuală de concediu' : 'Solduri de concediu' }}</h2>
    <VAlert v-if="error" class="mb-3" color="error" variant="tonal" role="alert">{{ error }}</VAlert>
    <VAlert v-if="saved && allocation" class="mb-3" color="success" variant="tonal" role="status">
      Alocarea pentru {{ allocation.year }} a fost salvată ({{ allocation.totalDays }} zile).
    </VAlert>
    <VBtn v-if="employeeId === undefined && error && !users.length" variant="text" :disabled="busy" @click="loadUsers">Reîncarcă utilizatorii</VBtn>
    <form class="allocation-filters" :class="{ 'allocation-filters--employee': employeeId !== undefined }" @submit.prevent="updateAllocation(false)">
      <VAutocomplete v-if="employeeId === undefined" v-model="selectedUserId" label="Utilizator" placeholder="Caută după nume" :items="userOptions" :custom-filter="matchesName" no-data-text="Nu am găsit niciun utilizator." :disabled="busy" variant="outlined" hide-details />
      <VTextField v-model="year" label="An" type="number" min="1" max="9999" :disabled="busy" variant="outlined" hide-details />
      <VBtn type="submit" :disabled="busy || userId === null || !validYear">Vezi soldul</VBtn>
    </form>
    <div v-if="allocation" class="allocation-result" aria-live="polite">
      <h3>Concediu de odihnă pentru {{ allocation.year }}</h3>
      <LeaveBalanceSummary :balance="allocation" />
    </div>
    <form v-if="allocation && canSetAllocation" class="allocation-edit" @submit.prevent="updateAllocation(true)">
      <h3>Modifică alocarea anuală</h3>
      <div class="allocation-edit-fields">
        <VTextField v-model="totalDays" label="Număr anual de zile" type="number" min="0" step="1" required :disabled="busy" variant="outlined" hide-details />
        <VBtn type="submit" :disabled="busy || !validDays">Salvează alocarea</VBtn>
      </div>
    </form>
  </section>
</template>

<style scoped>
.allocation-panel {
  margin-top: 18px;
  padding: clamp(14px, 3vw, 24px);
  border: 1px solid var(--ritm-border);
  border-radius: 16px;
  background: var(--ritm-panel-soft);
}
.allocation-panel h2, .allocation-panel h3 { color: var(--ritm-accent); font-weight: 750; }
.allocation-panel h3 { margin-bottom: 10px; font-size: 16px; }
.allocation-filters, .allocation-edit-fields { display: grid; align-items: center; gap: 12px; }
.allocation-filters { grid-template-columns: minmax(0, 1fr) 7rem auto; }
.allocation-filters--employee { grid-template-columns: 7rem auto; justify-content: start; }
.allocation-result, .allocation-edit { margin-top: 20px; }
.allocation-edit { padding-top: 16px; border-top: 1px solid var(--ritm-border); }
.allocation-edit-fields { grid-template-columns: minmax(0, 12rem) auto; justify-content: start; }
@media (max-width: 640px) {
  .allocation-filters, .allocation-edit-fields { grid-template-columns: minmax(0, 1fr); justify-content: stretch; }
}
</style>
