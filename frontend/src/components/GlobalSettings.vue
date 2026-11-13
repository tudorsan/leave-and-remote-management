<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VSelect } from 'vuetify/components/VSelect'
import { VTextField } from 'vuetify/components/VTextField'
import { useAuthStore } from '../stores/auth'

const emit = defineEmits<{ unauthorized: [] }>()
const auth = useAuthStore()

const fields = [
  { key: 'remote.min_days_ahead', name: 'remoteMinDaysAhead', label: 'Preaviz minim (zile)' },
  { key: 'remote.max_days_ahead', name: 'remoteMaxDaysAhead', label: 'Orizont maxim (zile)' },
  { key: 'remote.max_days_per_week', name: 'remoteMaxDaysPerWeek', label: 'Zile maxime pe săptămână', max: 7 },
  { key: 'leave.min_days_ahead', name: 'leaveMinDaysAhead', label: 'Preaviz minim pentru concediu de odihnă (zile)' },
  { key: 'leave.max_days_ahead', name: 'leaveMaxDaysAhead', label: 'Orizont maxim (zile)' },
  { key: 'leave.default_annual_days', name: 'leaveDefaultAnnualDays', label: 'Alocare anuală implicită (zile)' },
  { key: 'leave.max_team_absent_pct', name: 'leaveMaxTeamAbsentPct', label: 'Procent maxim din echipă absent în aceeași zi', max: 100 },
]
const groups = [
  { title: 'Lucru remote', fields: fields.slice(0, 3) },
  { title: 'Concediu', fields: fields.slice(3) },
]

interface Holiday {
  date: string
  name: string
}

const values = ref<Record<string, string>>({})
const requiresApproval = ref(false)
const approvalOptions = [
  { title: 'Da', value: true },
  { title: 'Nu', value: false },
]
const holidays = ref<(Holiday & { id: number })[]>([])
let nextHolidayId = 0
const loaded = ref(false)
const busy = ref(false)
const error = ref('')
const notice = ref('')

async function request(path = '', body?: unknown) {
  const response = await fetch(`/api/settings${path}`, {
    method: body === undefined ? 'GET' : 'PUT',
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
  return response
}

async function loadSettings() {
  busy.value = true
  error.value = ''
  try {
    const response = await request()
    if (!response) return
    const result: { settings: Record<string, string>; holidays: Holiday[] } = await response.json()
    values.value = Object.fromEntries(fields.map(field => [field.name, result.settings[field.key] ?? '']))
    requiresApproval.value = result.settings['remote.requires_approval']?.toLowerCase() === 'true'
    holidays.value = result.holidays.map(holiday => ({ ...holiday, id: nextHolidayId++ }))
    loaded.value = true
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : 'Setările nu au putut fi încărcate.'
  } finally {
    busy.value = false
  }
}

async function save(path: string, body: unknown, confirmation: string) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    if (await request(path, body)) notice.value = confirmation
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : 'Modificările nu au putut fi salvate.'
  } finally {
    busy.value = false
  }
}

function saveLimits() {
  const limits = Object.fromEntries(fields.map(({ name }) => {
    const value = String(values.value[name] ?? '').trim()
    return [name, value ? Number(value) : null]
  }))
  void save('', { ...limits, remoteRequiresApproval: requiresApproval.value }, 'Setările globale au fost salvate.')
}

function saveHolidays() {
  const dates = holidays.value.map(({ date, name }) => ({ date, name: name.trim() }))
  void save('/holidays', dates, 'Lista de sărbători a fost salvată.')
}

async function addHoliday() {
  const id = nextHolidayId++
  holidays.value.push({ id, date: '', name: '' })
  await nextTick()
  document.getElementById(`holiday-date-${id}`)?.focus()
}

onMounted(loadSettings)
</script>

<template>
  <section class="mt-4" aria-labelledby="settings-heading">
    <h2 id="settings-heading" class="text-h6 mb-2">Setări globale</h2>
    <VAlert v-if="error" class="mb-3" color="error" variant="tonal" role="alert">{{ error }}</VAlert>
    <VAlert v-if="notice" class="mb-3" color="success" variant="tonal" role="status">{{ notice }}</VAlert>
    <p v-if="!loaded && busy" role="status">Se încarcă setările…</p>
    <VBtn v-if="!loaded && error" variant="text" :disabled="busy" @click="loadSettings">Reîncarcă setările</VBtn>
    <template v-if="loaded">
      <form class="settings-form" @submit.prevent="saveLimits">
        <div class="settings-groups">
          <fieldset v-for="group in groups" :key="group.title" class="settings-group">
            <legend>{{ group.title }}</legend>
            <div v-for="field in group.fields" :key="field.key" class="settings-field">
              <label :for="field.name">{{ field.label }}</label>
              <VTextField
                :id="field.name"
                v-model="values[field.name]"
                type="number"
                min="0"
                :max="field.max ?? 2147483647"
                step="1"
                required
                variant="outlined"
                density="compact"
                hide-details="auto"
                :disabled="busy"
              />
            </div>
            <div v-if="group.title === 'Lucru remote'" class="settings-field">
              <label for="remote-requires-approval">Cererile remote necesită aprobare</label>
              <VSelect
                id="remote-requires-approval"
                v-model="requiresApproval"
                :items="approvalOptions"
                variant="outlined"
                density="compact"
                hide-details="auto"
                :disabled="busy"
              />
            </div>
          </fieldset>
        </div>
        <div class="settings-actions">
          <VBtn type="submit" class="settings-save" :disabled="busy">Salvează setările</VBtn>
        </div>
      </form>
      <form class="holidays-form" aria-labelledby="holidays-heading" @submit.prevent="saveHolidays">
        <div class="holidays-header">
          <h3 id="holidays-heading">Sărbători legale</h3>
          <VBtn type="button" variant="text" :disabled="busy" @click="addHoliday">Adaugă sărbătoare</VBtn>
        </div>
        <p v-if="!holidays.length" class="holiday-empty">Nu sunt configurate sărbători.</p>
        <div v-else class="holiday-list" role="region" aria-labelledby="holidays-heading" tabindex="0">
          <div v-for="(holiday, index) in holidays" :key="holiday.id" class="holiday-row">
            <VTextField :id="`holiday-date-${holiday.id}`" v-model="holiday.date" type="date" :label="`Data sărbătorii ${index + 1}`" variant="outlined" density="compact" hide-details="auto" required :disabled="busy" />
            <VTextField v-model="holiday.name" :label="`Numele sărbătorii ${index + 1}`" variant="outlined" density="compact" hide-details="auto" required :disabled="busy" />
            <VBtn type="button" variant="text" color="error" :disabled="busy" @click="holidays.splice(index, 1)">Elimină</VBtn>
          </div>
        </div>
        <div class="settings-actions">
          <VBtn type="submit" class="settings-save" :disabled="busy">Salvează sărbătorile</VBtn>
        </div>
      </form>
    </template>
  </section>
</template>

<style scoped>
.settings-groups { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.settings-group, .holidays-form {
  min-width: 0;
  padding: clamp(12px, 2vw, 20px);
  border: 1px solid var(--ritm-border);
  border-radius: 12px;
  background: var(--ritm-surface);
}
.settings-group legend, .holidays-header h3 { color: var(--ritm-accent); font-size: 17px; font-weight: 750; }
.settings-group legend { padding-inline: 6px; }
.settings-field + .settings-field { margin-top: 12px; }
.settings-field label { display: block; margin-bottom: 5px; color: var(--ritm-text); font-weight: 650; }
.settings-actions { display: flex; justify-content: flex-end; margin-top: 14px; }
.settings-actions .settings-save { border-color: var(--ritm-outline); }
.holidays-form { margin-top: 22px; }
.holidays-header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px; margin-bottom: 14px; }
.holiday-list { max-height: min(46vh, 25rem); overflow: auto; padding: 4px 12px; border: 1px solid var(--ritm-border); border-radius: 10px; }
.holiday-list:focus-visible { outline: 2px solid var(--ritm-focus); outline-offset: 3px; }
.holiday-row { display: grid; grid-template-columns: minmax(9.5rem, 1fr) minmax(0, 2fr) auto; align-items: center; gap: 12px; padding-block: 12px; }
.holiday-row + .holiday-row { border-top: 1px solid var(--ritm-divider); }
.holiday-empty { color: var(--ritm-muted); }

@media (max-width: 720px) {
  .settings-groups, .holiday-row { grid-template-columns: minmax(0, 1fr); }
  .holiday-row > .v-btn { justify-self: start; }
}
</style>
