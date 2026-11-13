<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VCard } from 'vuetify/components/VCard'
import { VSelect } from 'vuetify/components/VSelect'
import { VTextField } from 'vuetify/components/VTextField'
import RemoteRequestForm from '../components/RemoteRequestForm.vue'
import LeaveRequestForm from '../components/LeaveRequestForm.vue'
import CalendarGrid from '../components/CalendarGrid.vue'
import WeekGrid from '../components/WeekGrid.vue'
import ApprovedDaysExport from '../components/ApprovedDaysExport.vue'
import { types, statuses, type CalendarEvent } from '../utils/calendarDisplay'
import { useAuthStore } from '../stores/auth'
import PendingRequests from '../components/PendingRequests.vue'
import AnnualLeaveAllocation from '../components/AnnualLeaveAllocation.vue'
import OwnLeaveBalance from '../components/OwnLeaveBalance.vue'
import GlobalSettings from '../components/GlobalSettings.vue'
import UserAdministration from '../components/UserAdministration.vue'
import TeamAdministration from '../components/TeamAdministration.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import '../assets/role-palette.css'
import { matchesName } from '../utils/nameSearch'

const router = useRouter()
const auth = useAuthStore()
const roles = { Employee: 'Angajat', Manager: 'Manager', Admin: 'Administrator' }
const role = computed(() => (auth.user ? roles[auth.user.role] : ''))
const rolePalette = computed(() => `ritm-role--${auth.user?.role ?? 'Employee'}`)
const isAdmin = computed(() => auth.user?.role === 'Admin')
const canReview = computed(() => isAdmin.value || auth.user?.role === 'Manager')
const pendingRequestsOpen = ref(false)
const allocationOpen = ref(false)
const settingsOpen = ref(false)
const usersOpen = ref(false)
const teamAdministrationOpen = ref(false)
const ownBalanceOpen = ref(false)
const balanceRevision = ref(0)

const requestDateFormat = new Intl.DateTimeFormat('ro-RO', {
  day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC',
})

function formatRequestDate(date: string) {
  return requestDateFormat.format(new Date(date))
}

interface OwnRequest {
  canCancel: boolean
  id: number
  status: keyof typeof statuses
  rejectionReason: string | null
  createdAt: string
  comment: string | null
}

interface OwnLeaveRequest extends OwnRequest {
  startDate: string
  endDate: string
  workingDays?: number
  type: Exclude<keyof typeof types, 'Remote'>
}

interface OwnRemoteRequest extends OwnRequest {
  type: 'Remote'
  days: string[]
}

interface CalendarMonth {
  year: number
  month: number
  teamId: number | null
  members: { id: number; firstName: string; lastName: string }[]
  events: CalendarEvent[]
  holidays?: { date: string; name: string }[]
}

interface CalendarWeek {
  from: string
  to: string
  teamId: number | null
  members: { id: number; firstName: string; lastName: string }[]
  events: CalendarEvent[]
  holidays: { date: string; name: string }[]
}

const calendar = ref<CalendarMonth | null>(null)
// Cerința 6: calendarul poate fi văzut pe lună sau pe săptămână (luni–duminică).
const calendarView = ref<'month' | 'week'>('month')
const weekStart = ref<string | null>(null)
const week = ref<CalendarWeek | null>(null)
const membersOpen = ref(false)
const memberSearch = ref('')
const selectedMonth = ref<{ year: number; month: number } | null>(null)
const selectedType = ref('')
const absenceFilterOpen = ref(false)
const selectedTeam = ref(0)
const teamFilterOpen = ref(false)
const teamSearch = ref('')
const teams = ref<{ id: number; name: string; isActive: boolean }[]>([])
const teamsLoading = ref(false)
const teamsError = ref('')
const typeOptions = [
  { title: 'Toate tipurile', value: '' },
  ...Object.entries(types).map(([value, type]) => ({ title: type.label, value })),
]
const leaveTypeOptions = typeOptions.filter(option => option.value && option.value !== 'Remote')
const leaveFormOpen = ref(false)
const leaveStart = ref('')
const leaveEnd = ref('')
const leaveType = ref<string | null>(null)
const leaveComment = ref('')
const leaveSubmitting = ref(false)
const leaveError = ref('')
const leaveConfirmation = ref('')
const ownRequestsOpen = ref(false)
const ownRequests = ref<(OwnLeaveRequest | OwnRemoteRequest)[]>([])
const ownRequestsLoading = ref(false)
const ownRequestsError = ref('')
const cancelingRequest = ref<OwnLeaveRequest | OwnRemoteRequest | null>(null)
const cancellationNotice = ref('')
const requestToCancel = ref<OwnLeaveRequest | OwnRemoteRequest | null>(null)
const remoteFormOpen = ref(false)
const remoteDays = ref<string[]>([])
const remoteComment = ref('')
const editingRequestId = ref<number | null>(null)
const remoteSubmitting = ref(false)
const remoteError = ref('')
const remoteNotice = ref('')
const requestFormOpen = computed(() => leaveFormOpen.value || remoteFormOpen.value)
// Selecția zilelor remote se face în grila lunară, deci formularele afișează mereu luna.
const showingWeek = computed(() => calendarView.value === 'week' && !requestFormOpen.value)
const panelOpen = computed(() => ownRequestsOpen.value || ownBalanceOpen.value ||
  pendingRequestsOpen.value || allocationOpen.value || settingsOpen.value ||
  usersOpen.value || teamAdministrationOpen.value)
const workspaceFocused = computed(() => requestFormOpen.value || panelOpen.value)
const newRequestMenuOpen = ref(false)
const navigationBusy = computed(() => leaveSubmitting.value || remoteSubmitting.value || cancelingRequest.value !== null)
const navigationPanels = {
  balance: ownBalanceOpen,
  pending: pendingRequestsOpen,
  allocation: allocationOpen,
  settings: settingsOpen,
  users: usersOpen,
  teams: teamAdministrationOpen,
}

function closeNewRequestMenu(event: PointerEvent) {
  if (event.pointerType === 'mouse') newRequestMenuOpen.value = false
}

function closeMembersOnPointerLeave(event: PointerEvent) {
  if (event.pointerType === 'mouse') membersOpen.value = false
}

function closeAbsenceFilterOnPointerLeave(event: PointerEvent) {
  if (event.pointerType === 'mouse') absenceFilterOpen.value = false
}

watch(workspaceFocused, focused => {
  if (focused) newRequestMenuOpen.value = false
})

function showWorkspace(destination: 'calendar' | 'requests' | keyof typeof navigationPanels) {
  if (navigationBusy.value) return
  if (leaveFormOpen.value) closeLeaveForm()
  if (remoteFormOpen.value) closeRemoteForm()
  closePanel()

  if (destination === 'requests') void toggleOwnRequests()
  else if (destination !== 'calendar') navigationPanels[destination].value = true
}

function closePanel() {
  if (cancelingRequest.value !== null) return
  ownRequestsOpen.value = false
  ownBalanceOpen.value = false
  pendingRequestsOpen.value = false
  allocationOpen.value = false
  settingsOpen.value = false
  usersOpen.value = false
  teamAdministrationOpen.value = false
}

const teamOptions = computed(() => [
  { title: 'Toate echipele', value: 0 },
  ...teams.value.map(team => ({ title: team.name, value: team.id })),
])
const selectedTeamName = computed(() => teamOptions.value.find(team => team.value === selectedTeam.value)?.title ?? 'Toate echipele')
const filteredTeamOptions = computed(() => teamOptions.value.filter(team => matchesName(team.title, teamSearch.value)))

function closeTeamFilter() {
  teamFilterOpen.value = false
  teamSearch.value = ''
  document.getElementById('calendar-team-trigger')?.focus()
}

function selectCalendarTeam(teamId: number) {
  closeTeamFilter()
  if (selectedTeam.value === teamId) return
  selectedTeam.value = teamId
  void loadCalendar()
}
const loading = ref(true)
const monthTurn = ref<-1 | 0 | 1>(0)
let calendarLoadId = 0
const errorMessage = ref('')
const memberNames = computed(() => new Map(
  calendar.value?.members.map(member => [member.id, `${member.firstName} ${member.lastName}`]),
))
const filteredMembers = computed(() => calendar.value?.members.filter(member =>
  matchesName(`${member.firstName} ${member.lastName}`, memberSearch.value),
) ?? [])
const monthLabel = computed(() => {
  if (!selectedMonth.value) return ''
  const { year, month } = selectedMonth.value
  return new Intl.DateTimeFormat('ro-RO', {
    month: 'long', year: 'numeric', timeZone: 'UTC',
  }).format(calendarDate(year, month - 1, 1))
})

function calendarDate(year: number, monthIndex: number, day: number) {
  const date = new Date(0)
  date.setUTCFullYear(year, monthIndex, day)
  return date
}

// Datele sunt calendaristice (AAAA-LL-ZZ), calculate în UTC ca să nu apară decalaje de fus orar.
function isoDay(date: Date) {
  return `${String(date.getUTCFullYear()).padStart(4, '0')}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`
}

function parseDay(day: string) {
  const [year, month, date] = day.split('-').map(Number)
  return calendarDate(year!, month! - 1, date!)
}

function addDays(day: string, count: number) {
  const date = parseDay(day)
  date.setUTCDate(date.getUTCDate() + count)
  return isoDay(date)
}

function mondayOf(day: string) {
  const date = parseDay(day)
  return addDays(day, -((date.getUTCDay() + 6) % 7))
}

function localToday() {
  const now = new Date()
  return isoDay(calendarDate(now.getFullYear(), now.getMonth(), now.getDate()))
}

const weekDays = computed(() => (week.value ? Array.from({ length: 7 }, (_, index) => addDays(week.value!.from, index)) : []))
const weekLabel = computed(() => {
  if (!weekStart.value) return ''
  const format = new Intl.DateTimeFormat('ro-RO', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })
  return `${format.format(parseDay(weekStart.value))} – ${format.format(parseDay(addDays(weekStart.value, 6)))}`
})

function setCalendarView(view: 'month' | 'week') {
  if (calendarView.value === view || loading.value) return
  if (view === 'week') {
    // Săptămâna curentă dacă se vede luna curentă, altfel prima săptămână a lunii afișate.
    const today = localToday()
    const selected = selectedMonth.value
    const inSelectedMonth = !selected || today.startsWith(`${String(selected.year).padStart(4, '0')}-${String(selected.month).padStart(2, '0')}`)
    weekStart.value = mondayOf(inSelectedMonth ? today : isoDay(calendarDate(selected!.year, selected!.month - 1, 1)))
  } else if (weekStart.value) {
    const thursday = parseDay(addDays(weekStart.value, 3))
    selectedMonth.value = { year: thursday.getUTCFullYear(), month: thursday.getUTCMonth() + 1 }
  }
  calendarView.value = view
}

function changeWeek(direction: -1 | 1) {
  if (!weekStart.value || loading.value) return
  weekStart.value = addDays(weekStart.value, 7 * direction)
  void loadCalendar(direction)
}

watch(showingWeek, () => void loadCalendar())

const weeks = computed(() => {
  if (!calendar.value) return []
  const { year, month, events, holidays = [] } = calendar.value
  const holidayNames = new Map(holidays.map(holiday => [holiday.date, holiday.name]))
  const byDate = new Map<string, CalendarEvent[]>()
  for (const event of events) {
    const dayEvents = byDate.get(event.date) ?? []
    dayEvents.push(event)
    byDate.set(event.date, dayEvents)
  }

  const firstWeekday = (calendarDate(year, month - 1, 1).getUTCDay() + 6) % 7
  const dayCount = calendarDate(year, month, 0).getUTCDate()
  const weekCount = Math.ceil((firstWeekday + dayCount) / 7)
  const monthPrefix = `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-`

  return Array.from({ length: weekCount }, (_, weekIndex) =>
    Array.from({ length: 7 }, (_, weekday) => {
      const day = weekIndex * 7 + weekday - firstWeekday + 1
      if (day < 1 || day > dayCount) return null

      const date = monthPrefix + String(day).padStart(2, '0')
      return { day, date, holiday: holidayNames.get(date), events: byDate.get(date) ?? [] }
    }),
  )
})

async function submitLeaveRequest() {
  if (leaveSubmitting.value || !leaveStart.value || !leaveEnd.value || !leaveType.value) return

  leaveSubmitting.value = true
  leaveError.value = ''
  try {
    const id = editingRequestId.value
    const response = await fetch(id === null ? '/api/leave-requests' : `/api/leave-requests/${id}`, {
      method: id === null ? 'POST' : 'PUT',
      headers: {
        Authorization: `Bearer ${auth.getToken() ?? ''}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        startDate: leaveStart.value,
        endDate: leaveEnd.value,
        type: leaveType.value,
        comment: leaveComment.value.trim() || null,
      }),
    })
    if (response.status === 401) {
      await sessionExpired()
      return
    }
    if (!response.ok) {
      const error = await response.json().catch(() => null)
      leaveError.value = error?.message || 'Cererea nu a putut fi trimisă.'
      return
    }

    const result = await response.json().catch(() => null)
    const days = result?.workingDays
    const usesDe = days >= 20 && (days % 100 === 0 || days % 100 >= 20)
    const dayLabel = days === 1 ? 'zi lucrătoare' : usesDe ? 'de zile lucrătoare' : 'zile lucrătoare'
    leaveConfirmation.value = Number.isInteger(days) && days > 0
      ? `Cererea de concediu a fost trimisă pentru ${days} ${dayLabel}.`
      : 'Cererea de concediu a fost trimisă.'
    if (id !== null) leaveConfirmation.value = 'Modificările cererii de concediu au fost salvate.'
    closeLeaveForm()
    balanceRevision.value++
    await Promise.all([loadCalendar(), ownRequestsOpen.value ? loadOwnRequests() : undefined])
  } catch {
    leaveError.value = 'Cererea nu a putut fi salvată. Încearcă din nou.'
  } finally {
    leaveSubmitting.value = false
  }
}

function closeLeaveForm() {
  leaveStart.value = ''
  leaveEnd.value = ''
  leaveType.value = null
  leaveComment.value = ''
  leaveFormOpen.value = false
  editingRequestId.value = null
}

async function editOwnRequest(request: OwnLeaveRequest | OwnRemoteRequest) {
  editingRequestId.value = request.id
  if (request.type === 'Remote') {
    remoteDays.value = [...request.days]
    remoteComment.value = request.comment ?? ''
    remoteNotice.value = ''
    remoteError.value = ''
    remoteFormOpen.value = true
    const firstDay = request.days[0]
    if (firstDay) {
      selectedMonth.value = { year: Number(firstDay.slice(0, 4)), month: Number(firstDay.slice(5, 7)) }
      void loadCalendar()
    }
  } else {
    leaveStart.value = request.startDate
    leaveEnd.value = request.endDate
    leaveType.value = request.type
    leaveComment.value = request.comment ?? ''
    leaveConfirmation.value = ''
    leaveError.value = ''
    leaveFormOpen.value = true
  }
  await nextTick()
  document.getElementById(request.type === 'Remote' ? 'remote-heading' : 'leave-heading')?.focus()
}

async function loadOwnRequests() {
  if (ownRequestsLoading.value) return
  ownRequestsLoading.value = true
  ownRequestsError.value = ''
  try {
    const headers = { Authorization: `Bearer ${auth.getToken() ?? ''}` }
    const [leave, remote] = await Promise.all([
      fetch('/api/leave-requests', { headers }),
      fetch('/api/remote-requests', { headers }),
    ])
    if (leave.status === 401 || remote.status === 401) {
      await sessionExpired()
      return
    }
    if (!leave.ok || !remote.ok) throw new Error('Own requests unavailable')
    const leaves: OwnLeaveRequest[] = await leave.json()
    const remotes: Omit<OwnRemoteRequest, 'type'>[] = await remote.json()
    ownRequests.value = [
      ...leaves,
      ...remotes.map(request => ({ ...request, type: 'Remote' as const })),
    ].sort((first, second) => second.createdAt.localeCompare(first.createdAt))
  } catch {
    ownRequestsError.value = 'Cererile tale nu au putut fi încărcate.'
  } finally {
    ownRequestsLoading.value = false
  }
}

function toggleRemoteDay(date: string) {
  // O cerere remote existentă acoperă o singură zi: la editare, clicul înlocuiește ziua.
  if (editingRequestId.value !== null) {
    remoteDays.value = [date]
    return
  }
  remoteDays.value = remoteDays.value.includes(date)
    ? remoteDays.value.filter(day => day !== date)
    : [...remoteDays.value, date].sort()
}

function closeRemoteForm() {
  remoteFormOpen.value = false
  remoteDays.value = []
  remoteComment.value = ''
  editingRequestId.value = null
  remoteError.value = ''
}

async function submitRemoteRequest() {
  if (remoteSubmitting.value || loading.value || !remoteDays.value.length) return

  remoteSubmitting.value = true
  remoteError.value = ''
  try {
    const id = editingRequestId.value
    const response = await fetch(id === null ? '/api/remote-requests' : `/api/remote-requests/${id}`, {
      method: id === null ? 'POST' : 'PUT',
      headers: {
        Authorization: `Bearer ${auth.getToken() ?? ''}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        days: remoteDays.value,
        comment: remoteComment.value.trim() || null,
      }),
    })
    if (response.status === 401) {
      await sessionExpired()
      return
    }
    if (!response.ok) {
      const error = await response.json().catch(() => null)
      remoteError.value = error?.message || 'Cererea de lucru remote nu a putut fi trimisă.'
      return
    }

    const result = await response.json().catch(() => null)
    const notices = {
      Approved: 'Cererea de lucru remote a fost aprobată automat.',
      Pending: 'Cererea de lucru remote a fost trimisă și așteaptă aprobarea.',
    }
    const status: unknown = result?.status
    remoteNotice.value = status === 'Approved' || status === 'Pending'
      ? notices[status]
      : 'Cererea de lucru remote a fost trimisă.'
    if (id !== null) {
      remoteNotice.value = status === 'Pending'
        ? 'Modificările au fost salvate. Cererea așteaptă aprobarea.'
        : 'Modificările cererii de lucru remote au fost salvate.'
    }
    closeRemoteForm()
    await Promise.all([loadCalendar(), ownRequestsOpen.value ? loadOwnRequests() : undefined])
  } catch {
    remoteError.value = 'Cererea de lucru remote nu a putut fi trimisă. Încearcă din nou.'
  } finally {
    remoteSubmitting.value = false
  }
}

async function toggleOwnRequests() {
  ownRequestsOpen.value = !ownRequestsOpen.value
  if (ownRequestsOpen.value) {
    cancellationNotice.value = ''
    await loadOwnRequests()
  }
}

async function cancelOwnRequest(request: OwnLeaveRequest | OwnRemoteRequest) {
  requestToCancel.value = null
  if (cancelingRequest.value !== null || !request.canCancel) return
  const remote = request.type === 'Remote'
  const label = remote ? 'de lucru remote' : 'de concediu'

  cancelingRequest.value = request
  ownRequestsError.value = ''
  cancellationNotice.value = ''
  try {
    const endpoint = remote ? 'remote-requests' : 'leave-requests'
    const response = await fetch(`/api/${endpoint}/${request.id}/cancel`, {
      method: 'PATCH',
      headers: { Authorization: `Bearer ${auth.getToken() ?? ''}` },
    })
    if (response.status === 401) {
      await sessionExpired()
      return
    }
    if (!response.ok) {
      const error = await response.json().catch(() => null)
      if (response.status === 404 || response.status === 409) await loadOwnRequests()
      ownRequestsError.value = error?.message || 'Cererea nu a putut fi anulată.'
      return
    }
    request.status = 'Canceled'
    request.canCancel = false
    if (request.type !== 'Remote') balanceRevision.value++
    cancellationNotice.value = `Cererea ${label} a fost anulată.`
    await loadCalendar()
  } catch {
    ownRequestsError.value = 'Cererea nu a putut fi anulată. Încearcă din nou.'
  } finally {
    cancelingRequest.value = null
  }
}

async function loadWeek(loadId: number) {
  try {
    const params = new URLSearchParams()
    if (weekStart.value) {
      params.set('from', weekStart.value)
      params.set('to', addDays(weekStart.value, 6))
    }
    if (isAdmin.value && selectedTeam.value) params.set('teamId', String(selectedTeam.value))
    if (selectedType.value) params.set('type', selectedType.value)
    const response = await fetch(`/api/calendar/range?${params}`, {
      headers: { Authorization: `Bearer ${auth.getToken() ?? ''}` },
    })
    if (loadId !== calendarLoadId) return
    if (response.status === 401) {
      await sessionExpired()
      return
    }
    const result = await response.json().catch(() => null)
    if (loadId !== calendarLoadId) return
    if (!response.ok) {
      errorMessage.value = result?.message || 'Calendarul săptămânal nu a putut fi încărcat.'
      return
    }
    week.value = result as CalendarWeek
    weekStart.value = week.value.from
  } catch {
    if (loadId === calendarLoadId) errorMessage.value = 'Calendarul săptămânal nu a putut fi încărcat. Încearcă din nou.'
  } finally {
    if (loadId === calendarLoadId) loading.value = false
  }
}

async function loadCalendar(direction: -1 | 0 | 1 = 0) {
  const loadId = ++calendarLoadId
  monthTurn.value = 0
  loading.value = true
  errorMessage.value = ''
  if (showingWeek.value) {
    await loadWeek(loadId)
    return
  }

  try {
    const selected = selectedMonth.value
    const params = new URLSearchParams()
    if (selected) {
      params.set('year', String(selected.year))
      params.set('month', String(selected.month))
    }
    if (isAdmin.value && selectedTeam.value) params.set('teamId', String(selectedTeam.value))
    if (selectedType.value) params.set('type', selectedType.value)
    const query = params.toString()
    const url = query ? `/api/calendar?${query}` : '/api/calendar'
    const response = await fetch(url, {
      headers: { Authorization: `Bearer ${auth.getToken() ?? ''}` },
    })
    if (loadId !== calendarLoadId) return
    if (response.status === 401) {
      await sessionExpired()
      return
    }
    if (!response.ok) {
      const error = await response.json().catch(() => null)
      if (loadId !== calendarLoadId) return
      errorMessage.value = error?.message || 'Calendarul nu a putut fi încărcat.'
      return
    }
    const month = await response.json() as CalendarMonth
    if (loadId !== calendarLoadId) return
    calendar.value = month
    monthTurn.value = direction
    selectedMonth.value = { year: calendar.value.year, month: calendar.value.month }
  } catch {
    if (loadId !== calendarLoadId) return
    errorMessage.value = 'Calendarul nu a putut fi încărcat. Încearcă din nou.'
  } finally {
    if (loadId === calendarLoadId) loading.value = false
  }
}

function updateTeamList(team: (typeof teams.value)[number]) {
  teams.value = [...teams.value.filter(item => item.id !== team.id), team]
    .sort((first, second) => first.name.localeCompare(second.name, 'ro'))
}

async function loadTeams() {
  teamsLoading.value = true
  teamsError.value = ''

  try {
    const response = await fetch('/api/teams', {
      headers: { Authorization: `Bearer ${auth.getToken() ?? ''}` },
    })
    if (response.status === 401) {
      await sessionExpired()
      return
    }
    if (!response.ok) throw new Error('Teams unavailable')
    teams.value = await response.json()
  } catch {
    teamsError.value = 'Lista echipelor nu a putut fi încărcată.'
  } finally {
    teamsLoading.value = false
  }
}

function changeMonth(direction: -1 | 1) {
  const selected = selectedMonth.value
  if (!selected || loading.value) return

  const next = selected.year * 12 + selected.month - 1 + direction
  const year = Math.floor(next / 12)
  if (year < 1 || year > 9999) return

  selectedMonth.value = { year, month: next % 12 + 1 }
  void loadCalendar(direction)
}

async function signOut() {
  auth.signOut()
  await router.replace({ name: 'login' })
}

// Serverul a respins sesiunea (expirată, parolă schimbată sau cont dezactivat): explicăm de ce apare din nou login-ul.
async function sessionExpired() {
  auth.signOut()
  await router.replace({ name: 'login', query: { session: 'expired' } })
}

onMounted(() => {
  void loadCalendar()
  if (isAdmin.value) void loadTeams()
})
</script>

<template>
  <section class="home ritm-role" :class="rolePalette">
    <VCard class="home-card" theme="light" elevation="0">
      <aside class="workspace-sidebar">
        <div class="sidebar-brand"><span>R</span> Ritm</div>
        <nav class="workspace-nav" aria-label="Navigare principală">
          <VBtn :aria-current="!workspaceFocused ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('calendar')">Calendar</VBtn>
          <VBtn :aria-current="ownRequestsOpen && !requestFormOpen ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('requests')">Cererile mele</VBtn>
          <VBtn :aria-current="ownBalanceOpen ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('balance')">Soldul meu de concediu</VBtn>
          <template v-if="canReview">
            <p class="nav-group-label">{{ isAdmin ? 'Aprobări și solduri' : 'Echipa mea' }}</p>
            <VBtn :aria-current="pendingRequestsOpen ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('pending')">Cereri în așteptare</VBtn>
            <VBtn :aria-current="allocationOpen ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('allocation')">Solduri de concediu</VBtn>
          </template>
          <template v-if="isAdmin">
            <p class="nav-group-label">Administrare</p>
            <VBtn :aria-current="usersOpen ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('users')">Utilizatori</VBtn>
            <VBtn :aria-current="teamAdministrationOpen ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('teams')">Echipe</VBtn>
            <VBtn :aria-current="settingsOpen ? 'page' : undefined" :disabled="navigationBusy" @click="showWorkspace('settings')">Setări globale</VBtn>
          </template>
        </nav>
      </aside>

      <div class="workspace-main">
        <header class="home-header">
          <h1>{{ workspaceFocused ? 'Spațiu de lucru' : 'Calendar lunar' }}</h1>
          <div class="session-actions">
            <div v-if="!workspaceFocused" class="new-request" @pointerleave="closeNewRequestMenu" @keydown.esc="newRequestMenuOpen = false">
              <VBtn class="new-request-trigger" type="button" aria-controls="new-request-menu" :aria-expanded="newRequestMenuOpen" @click="newRequestMenuOpen = !newRequestMenuOpen">
                <span aria-hidden="true">＋&nbsp;</span>Cerere nouă
              </VBtn>
              <div v-show="newRequestMenuOpen" id="new-request-menu" class="action-menu">
                <VBtn :disabled="loading" @click="leaveFormOpen = true; leaveConfirmation = ''; leaveError = ''">Solicită concediu</VBtn>
                <VBtn :disabled="loading" @click="remoteFormOpen = true; remoteNotice = ''">Solicită lucru remote</VBtn>
              </div>
            </div>
            <span class="session-role">{{ role }}</span>
            <VBtn variant="text" @click="signOut">Deconectare</VBtn>
          </div>
        </header>
        <div class="workspace-content" :class="{ 'workspace-content--request': requestFormOpen }">
          <div v-if="panelOpen && !requestFormOpen" class="panel-navigation">
            <VBtn type="button" variant="outlined" :disabled="cancelingRequest !== null" @click="closePanel">
              <span aria-hidden="true">←&nbsp;</span>Înapoi
            </VBtn>
          </div>

          <OwnLeaveBalance v-if="ownBalanceOpen" :revision="balanceRevision" @unauthorized="sessionExpired" />
          <PendingRequests
            v-if="canReview && pendingRequestsOpen"
            :leave-labels="types"
            @unauthorized="sessionExpired"
            @reviewed="balanceRevision++; loadCalendar()"
          />
          <AnnualLeaveAllocation v-if="canReview && allocationOpen" @unauthorized="sessionExpired" />

          <GlobalSettings v-if="isAdmin && settingsOpen" class="request-panel" @unauthorized="sessionExpired" />
          <UserAdministration v-if="isAdmin && usersOpen" class="request-panel" :teams="teams" :teams-unavailable="teamsLoading || !!teamsError" @unauthorized="sessionExpired" />
          <TeamAdministration
            v-if="isAdmin && teamAdministrationOpen"
            class="request-panel"
            :teams="teams"
            :loading="teamsLoading"
            :load-error="teamsError"
            @unauthorized="sessionExpired"
            @saved="updateTeamList"
            @reload="loadTeams"
          />

          <VAlert v-if="remoteNotice && (!workspaceFocused || (ownRequestsOpen && !requestFormOpen))" class="mt-4" color="success" variant="tonal" role="status">
            {{ remoteNotice }}
          </VAlert>
          <RemoteRequestForm
            v-if="remoteFormOpen"
            v-model:remote-comment="remoteComment"
            :editing-request-id="editingRequestId"
            :remote-days="remoteDays"
            :remote-error="remoteError"
            :remote-submitting="remoteSubmitting"
            :submit-disabled="remoteSubmitting || ownRequestsLoading || loading || !remoteDays.length"
            :format-request-date="formatRequestDate"
            @close="closeRemoteForm"
            @submit="submitRemoteRequest"
          />

          <VAlert v-if="leaveConfirmation && (!workspaceFocused || (ownRequestsOpen && !requestFormOpen))" class="mt-4" color="success" variant="tonal" role="status">
            {{ leaveConfirmation }}
          </VAlert>
          <LeaveRequestForm
            v-if="leaveFormOpen"
            v-model:leave-start="leaveStart"
            v-model:leave-end="leaveEnd"
            v-model:leave-type="leaveType"
            v-model:leave-comment="leaveComment"
            :editing-request-id="editingRequestId"
            :leave-error="leaveError"
            :leave-submitting="leaveSubmitting"
            :leave-type-options="leaveTypeOptions"
            :submit-disabled="leaveSubmitting || ownRequestsLoading || cancelingRequest !== null || !leaveStart || !leaveEnd || !leaveType"
            @close="closeLeaveForm"
            @submit="submitLeaveRequest"
          />

          <section v-if="ownRequestsOpen && !requestFormOpen" class="request-panel" aria-labelledby="own-leave-heading">
            <div class="own-requests-header">
              <h2 id="own-leave-heading" class="text-h6">Cererile mele</h2>
              <VBtn
                variant="text"
                :disabled="ownRequestsLoading || cancelingRequest !== null || leaveSubmitting || remoteSubmitting"
                @click="loadOwnRequests"
              >Reîncarcă cererile</VBtn>
            </div>
            <ApprovedDaysExport @unauthorized="sessionExpired" />
            <VAlert v-if="ownRequestsError" class="mb-3" color="error" variant="tonal" role="alert">
              {{ ownRequestsError }}
            </VAlert>
            <VAlert v-if="cancellationNotice" class="mb-3" color="success" variant="tonal" role="status">
              {{ cancellationNotice }}
            </VAlert>
            <p v-if="ownRequestsLoading" role="status">Se încarcă cererile…</p>
            <p v-else-if="!ownRequests.length && !ownRequestsError">Nu ai cereri.</p>
            <ul v-else-if="ownRequests.length" class="own-request-list">
              <li v-for="request in ownRequests" :key="`${request.type}-${request.id}`">
                <div class="own-request-details">
                  <div class="own-request-title">
                    <h3>{{ types[request.type].label }}</h3>
                    <span class="request-status" :data-status="request.status">{{ statuses[request.status] }}</span>
                  </div>
                  <p class="own-request-dates" v-if="request.type === 'Remote'">
                    {{ request.days.map(formatRequestDate).join(', ') }}
                  </p>
                  <p class="own-request-dates" v-else>
                    <time :datetime="request.startDate">{{ formatRequestDate(request.startDate) }}</time>
                    <template v-if="request.endDate !== request.startDate">
                      –
                      <time :datetime="request.endDate">{{ formatRequestDate(request.endDate) }}</time>
                    </template>
                    <span v-if="request.workingDays"> · {{ request.workingDays === 1 ? '1 zi lucrătoare' : `${request.workingDays} zile lucrătoare` }}</span>
                  </p>
                  <p v-if="request.status === 'Rejected' && request.rejectionReason" class="rejection-reason">
                    <strong>Motivul respingerii:</strong> {{ request.rejectionReason }}
                  </p>
                </div>
                <div class="own-request-actions">
                  <VBtn
                    v-if="request.status === 'Pending'"
                    variant="outlined"
                    :disabled="leaveFormOpen || remoteFormOpen || loading || leaveSubmitting || remoteSubmitting || cancelingRequest !== null"
                    @click="editOwnRequest(request)"
                  >Editează cererea</VBtn>
                  <VBtn
                    v-if="request.canCancel"
                    variant="outlined"
                    color="error"
                    :disabled="cancelingRequest !== null || leaveFormOpen || leaveSubmitting || loading || remoteFormOpen || remoteSubmitting"
                    :loading="cancelingRequest === request"
                  @click="requestToCancel = request"
                  >Anulează cererea</VBtn>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="!workspaceFocused || requestFormOpen" class="calendar-area" aria-label="Calendarul echipei">
            <div v-if="selectedMonth" class="month-navigation mt-5">
              <h2>{{ showingWeek ? weekLabel : monthLabel }}</h2>
              <div class="month-actions">
                <div v-if="!requestFormOpen" class="view-switch" role="group" aria-label="Mod de afișare a calendarului">
                  <VBtn
                    :variant="calendarView === 'month' ? 'flat' : 'outlined'"
                    :aria-pressed="calendarView === 'month'"
                    :disabled="loading"
                    @click="setCalendarView('month')"
                  >Lună</VBtn>
                  <VBtn
                    :variant="calendarView === 'week' ? 'flat' : 'outlined'"
                    :aria-pressed="calendarView === 'week'"
                    :disabled="loading"
                    @click="setCalendarView('week')"
                  >Săptămână</VBtn>
                </div>
                <template v-if="showingWeek">
                  <VBtn variant="outlined" :disabled="loading || cancelingRequest !== null" @click="changeWeek(-1)">Săptămâna anterioară</VBtn>
                  <VBtn variant="outlined" :disabled="loading || cancelingRequest !== null" @click="changeWeek(1)">Săptămâna următoare</VBtn>
                </template>
                <template v-else>
                  <VBtn
                    variant="outlined"
                    :disabled="loading || cancelingRequest !== null || remoteSubmitting || (selectedMonth.year === 1 && selectedMonth.month === 1)"
                    @click="changeMonth(-1)"
                  >Luna anterioară</VBtn>
                  <VBtn
                    variant="outlined"
                    :disabled="loading || cancelingRequest !== null || remoteSubmitting || (selectedMonth.year === 9999 && selectedMonth.month === 12)"
                    @click="changeMonth(1)"
                  >Luna următoare</VBtn>
                </template>
              </div>
            </div>

            <div v-if="selectedMonth && !requestFormOpen" class="calendar-filters mt-4">
              <div id="calendar-absence-filter" class="absence-filter" @pointerleave="closeAbsenceFilterOnPointerLeave">
                <label for="calendar-absence-type">Tip de absență</label>
                <VSelect
                  id="calendar-absence-type"
                  v-model="selectedType"
                  v-model:menu="absenceFilterOpen"
                  :items="typeOptions"
                  :disabled="loading || cancelingRequest !== null || remoteSubmitting"
                  variant="outlined"
                  density="compact"
                  hide-details
                  :menu-props="{ attach: '#calendar-absence-filter', contentClass: ['ritm-absence-menu', 'ritm-role', rolePalette] }"
                  @update:model-value="loadCalendar()"
                />
              </div>
              <div v-if="isAdmin" class="team-filter" @keydown.esc="closeTeamFilter">
                <span id="calendar-team-label" class="calendar-filter-label">Echipă</span>
                <VBtn
                  id="calendar-team-trigger"
                  class="team-filter-trigger"
                  type="button"
                  aria-labelledby="calendar-team-label calendar-team-value"
                  aria-controls="calendar-team-options"
                  :aria-expanded="teamFilterOpen"
                  :disabled="loading || cancelingRequest !== null || remoteSubmitting || teamsLoading || !!teamsError"
                  :loading="teamsLoading"
                  @click="teamFilterOpen = !teamFilterOpen; membersOpen = false"
                >
                  <span id="calendar-team-value">{{ selectedTeamName }}</span>
                  <span class="member-chevron" aria-hidden="true">{{ teamFilterOpen ? '▴' : '▾' }}</span>
                </VBtn>
                <div v-show="teamFilterOpen" id="calendar-team-options" class="member-dropdown">
                  <VTextField id="calendar-team-search" v-model="teamSearch" label="Caută echipă" variant="outlined" density="compact" hide-details />
                  <ul v-if="filteredTeamOptions.length" class="member-list team-option-list">
                    <li v-for="team in filteredTeamOptions" :key="team.value">
                      <button type="button" :aria-current="selectedTeam === team.value ? 'true' : undefined" @click="selectCalendarTeam(team.value)">
                        {{ team.title }}
                      </button>
                    </li>
                  </ul>
                  <p v-else class="member-empty" role="status">Nu am găsit nicio echipă.</p>
                </div>
              </div>
            </div>
            <VAlert v-if="isAdmin && teamsError && !requestFormOpen" class="mt-3" color="error" variant="tonal" role="alert">
              {{ teamsError }}
              <VBtn variant="text" @click="loadTeams">Reîncarcă echipele</VBtn>
            </VAlert>

            <p v-if="loading" class="mt-5" role="status">Se încarcă calendarul…</p>
            <VAlert v-else-if="errorMessage" class="mt-5" color="error" variant="tonal" role="alert">
              {{ errorMessage }}
              <VBtn variant="text" @click="loadCalendar()">Încearcă din nou</VBtn>
            </VAlert>

            <div v-else-if="showingWeek && week" class="mt-5">
              <WeekGrid :days="weekDays" :members="week.members" :events="week.events" :holidays="week.holidays" />
            </div>

            <div v-else-if="!showingWeek && calendar" class="mt-5">
              <div
                v-if="!requestFormOpen"
                class="calendar-members"
                @keydown.esc="membersOpen = false"
                @pointerleave="closeMembersOnPointerLeave"
              >
                <VBtn
                  type="button"
                  :aria-expanded="membersOpen"
                  aria-controls="calendar-member-list"
                  @click="membersOpen = !membersOpen; teamFilterOpen = false"
                >
                  <span class="profile-icon" aria-hidden="true"></span>
                  {{ calendar.teamId === null ? 'Membri din toate echipele' : 'Membrii echipei' }}
                  <span class="member-chevron" aria-hidden="true">{{ membersOpen ? '▴' : '▾' }}</span>
                </VBtn>
                <div v-show="membersOpen" id="calendar-member-list" class="member-dropdown">
                  <VTextField id="calendar-member-search" v-model="memberSearch" label="Caută membru" variant="outlined" density="compact" hide-details />
                  <ul v-if="filteredMembers.length" class="member-list">
                    <li v-for="member in filteredMembers" :key="member.id">
                      {{ memberNames.get(member.id) }}
                    </li>
                  </ul>
                  <p v-else class="member-empty" role="status">{{ memberSearch.trim() ? 'Nu am găsit niciun membru.' : 'Nu sunt membri de afișat.' }}</p>
                </div>
              </div>

              <CalendarGrid
                v-model:month-turn="monthTurn"
                :month-label="monthLabel"
                :weeks="weeks"
                :member-names="memberNames"
                :remote-form-open="remoteFormOpen"
                :remote-days="remoteDays"
                :remote-submitting="remoteSubmitting"
                :has-events="calendar.events.length > 0"
                :show-legend="!requestFormOpen"
                @toggle-remote-day="toggleRemoteDay"
              />
            </div>
          </section>
        </div>
      </div>
      <ConfirmDialog
        v-if="requestToCancel"
        title="Anulezi cererea?"
        :message="`Cererea ${requestToCancel.type === 'Remote' ? 'de lucru remote' : 'de concediu'} va fi marcată ca anulată.`"
        confirm-label="Confirmă anularea"
        @dismiss="requestToCancel = null"
        @confirm="cancelOwnRequest(requestToCancel)"
      />
    </VCard>
  </section>
</template>

<style scoped>
.home {
  --ritm-page: #f3f4f6;
  --ritm-surface: #fff;
  --ritm-text: #222832;
  --ritm-muted: #4e5865;
  --ritm-border: #e0e4e9;
  --ritm-divider: #e0e4e9;
  --ritm-calendar-border: #dce1e7;
  --ritm-panel: #f6f7f9;
  --ritm-panel-soft: #f8f9fa;
  --ritm-outside: #eff1f4;
  min-height: 100dvh;
  padding: clamp(8px, 2vw, 28px);
  background: var(--ritm-page);
}

.home-card {
  display: grid;
  grid-template-columns: 13rem minmax(0, 1fr);
  width: min(100%, 96rem);
  margin-inline: auto;
  border: 1px solid var(--ritm-divider);
  border-radius: 16px;
  background: var(--ritm-surface);
  color: var(--ritm-text);
}

.home-header, .session-actions, .month-navigation, .month-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.view-switch { display: flex; gap: 0.35rem; margin-right: 0.5rem; }
.view-switch [aria-pressed="true"] { background: var(--ritm-accent); color: #fff; }

.home-header, .month-navigation, .session-actions {
  align-items: center;
}

.home-header, .month-navigation {
  justify-content: space-between;
}

.workspace-sidebar {
  padding: 18px 12px;
  border-right: 1px solid var(--ritm-divider);
  background: var(--ritm-panel-soft);
}
.sidebar-brand { display: flex; align-items: center; gap: 9px; padding: 0 6px 16px; font-size: 21px; font-weight: 750; }
.sidebar-brand span { display: grid; place-items: center; width: 29px; height: 29px; border-radius: 8px; background: var(--ritm-accent); color: #fff; font-size: 16px; font-weight: 750; }
.workspace-nav { display: grid; gap: 4px; }
.workspace-nav .nav-group-label { margin: 14px 8px 2px; color: var(--ritm-muted); font-size: 12px; font-weight: 750; }
.workspace-main { min-width: 0; padding: clamp(12px, 2vw, 28px); }
.workspace-content, .calendar-area { min-width: 0; }
.home-header { padding-bottom: 14px; border-bottom: 1px solid var(--ritm-divider); }
.home-header h1 { font-size: clamp(23px, 3vw, 34px); font-weight: 760; line-height: 1.1; letter-spacing: -.04em; }
.session-role { padding: 4px 12px; border-radius: 999px; background: var(--ritm-badge); color: var(--ritm-accent); font-weight: 700; }
.new-request {
  position: relative;
  max-width: 100%;
}
.action-menu {
  position: absolute;
  z-index: 3;
  inset: calc(100% + 6px) 0 auto auto;
  display: grid;
  width: max-content;
  min-width: 100%;
  gap: 5px;
  padding: 6px;
  border: 1px solid var(--ritm-border-strong);
  border-radius: 14px;
  background: var(--ritm-surface);
  box-shadow: 0 8px 22px color-mix(in srgb, var(--ritm-accent) 15%, transparent);
}
.action-menu::before {
  position: absolute;
  inset: -8px 0 auto;
  height: 8px;
  content: '';
}
.action-menu :deep(.v-btn) { justify-content: start; }
.panel-navigation { margin-top: 16px; }
.month-navigation { padding-top: 0; }
.month-navigation h2 { color: var(--ritm-accent); font-size: clamp(24px, 4vw, 36px); font-weight: 750; letter-spacing: -.04em; text-transform: capitalize; }
.home-card :deep(.v-btn) {
  border: 1px solid var(--ritm-border);
  border-radius: 8px;
  background: var(--ritm-surface) !important;
  color: var(--ritm-text) !important;
  font-weight: 650;
  letter-spacing: 0;
  text-transform: none;
  transition: background-color 150ms ease, color 150ms ease, border-color 150ms ease;
}
.home-card .workspace-nav :deep(.v-btn) { justify-content: start; border-color: transparent; box-shadow: none; background: transparent !important; }
.home-card .workspace-nav :deep(.v-btn[aria-current='page']) { background: var(--ritm-badge) !important; color: var(--ritm-accent) !important; }
.home-card :deep(.v-btn.new-request-trigger) { border-color: var(--ritm-accent); background: var(--ritm-accent) !important; color: #fff !important; }
.home-card :deep(.v-btn[aria-expanded='true']),
.home-card :deep(.v-btn[aria-pressed='true']) {
  border-color: var(--ritm-outline);
  background: var(--ritm-selected) !important;
  box-shadow: inset 0 0 0 1px var(--ritm-outline);
}
.home-card :deep(.v-btn:focus-visible) {
  outline: 3px solid var(--ritm-focus);
  outline-offset: 3px;
}
@media (hover: hover) {
  .home-card :deep(.v-btn:not(.v-btn--disabled):not(:disabled):hover) {
    border-color: var(--ritm-accent);
    background: var(--ritm-accent) !important;
    color: #fff !important;
  }
}

.member-list {
  list-style: none;
  padding: 0;
}

.request-panel {
  margin-top: 18px;
  padding: clamp(14px, 3vw, 24px);
  border: 1px solid var(--ritm-border);
  border-radius: 16px;
  background: var(--ritm-panel-soft);
}
.request-panel h2 { color: var(--ritm-accent); font-weight: 750; }
.home-card :deep(.v-btn.leave-submit) { border-color: var(--ritm-outline); font-weight: 750; }
.own-requests-header, .own-request-title, .own-request-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
}
.own-requests-header { justify-content: space-between; margin-bottom: 14px; }
.own-request-list { display: grid; gap: 10px; list-style: none; padding: 0; }
.own-request-list > li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 20px;
  padding: 14px 16px;
  border: 1px solid var(--ritm-border);
  border-radius: 12px;
  background: var(--ritm-surface);
}
.own-request-details { flex: 1 1 16rem; min-width: 0; }
.own-request-title h3 { color: var(--ritm-accent); font-size: 16px; font-weight: 750; }
.own-request-dates { margin-top: 4px; color: var(--ritm-muted); overflow-wrap: anywhere; }
.request-status { padding: 2px 9px; border-radius: 999px; background: #ebede9; color: #39453d; font-size: 13px; font-weight: 700; }
.request-status[data-status='Pending'] { background: #fff1cb; color: #63470e; }
.request-status[data-status='Approved'] { background: #dcebd9; color: #204b39; }
.request-status[data-status='Rejected'] { background: #f9e4e0; color: #862e28; }
.rejection-reason { margin-top: 8px; color: #683d38; overflow-wrap: anywhere; }
.rejection-reason strong { font-weight: 700; }
.own-request-actions:empty { display: none; }
.calendar-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 1rem;
  padding: 16px;
  border: 1px solid var(--ritm-border);
  border-radius: 14px;
  background: var(--ritm-panel);
}
.calendar-filters > * { flex: 0 1 16rem; }
.absence-filter { min-width: 0; }
.absence-filter label, .calendar-filter-label {
  display: block;
  margin-bottom: 6px;
  color: var(--ritm-accent);
  font-size: 15px;
  font-weight: 750;
}
.absence-filter :deep(.v-field) {
  border-radius: 9px;
  background: var(--ritm-control);
  color: var(--ritm-accent);
  transition: background-color 150ms ease, color 150ms ease;
}
.absence-filter :deep(.v-field--focused),
.absence-filter :deep(.v-field:has([aria-expanded='true'])) {
  background: var(--ritm-accent);
  color: #fff;
}
:global(.ritm-absence-menu .v-list) { padding: 6px; background: var(--ritm-surface); color: var(--ritm-accent); }
:global(.ritm-absence-menu .v-list-item) { border-radius: 8px; }
:global(.ritm-absence-menu .v-list-item--active),
:global(.ritm-absence-menu .v-list-item[aria-selected='true']) {
  background: var(--ritm-accent);
  color: #fff;
}
@media (hover: hover) {
  .absence-filter :deep(.v-field:not(.v-field--disabled):hover),
  :global(.ritm-absence-menu .v-list-item:not(.v-list-item--disabled):hover) {
    background: var(--ritm-accent);
    color: #fff;
  }
}
.calendar-members {
  position: relative;
  width: fit-content;
  max-width: 100%;
  margin: 24px 0 18px;
}
.member-dropdown {
  position: absolute;
  z-index: 2;
  inset: calc(100% + 6px) 0 auto;
  padding: 6px;
  border: 1px solid var(--ritm-border-strong);
  border-radius: 10px;
  background: var(--ritm-surface);
  color: var(--ritm-accent);
  box-shadow: 0 8px 22px color-mix(in srgb, var(--ritm-accent) 15%, transparent);
}
.member-dropdown .member-list { max-height: 16rem; overflow: auto; margin-top: 5px; }
.calendar-members .member-dropdown::before {
  position: absolute;
  inset: -8px 0 auto;
  height: 8px;
  content: '';
}
.team-filter { position: relative; min-width: 0; }
.home-card :deep(.team-filter-trigger) { width: 100%; justify-content: space-between; }
.team-option-list li { padding: 0; }
.team-option-list button { display: block; width: 100%; padding: 8px 10px; color: inherit; text-align: left; }
.team-option-list button:hover, .team-option-list button[aria-current='true'] { background: var(--ritm-badge); }
.member-empty { padding: 8px 10px; }
.member-list li { padding: 8px 10px; overflow-wrap: anywhere; }
.member-list li + li { border-top: 1px solid var(--ritm-list-divider); }
.profile-icon { position: relative; width: 18px; height: 18px; margin-right: 8px; }
.profile-icon::before, .profile-icon::after { position: absolute; content: ''; border: 1.5px solid currentColor; }
.profile-icon::before { top: 1px; left: 6px; width: 6px; height: 6px; border-radius: 50%; }
.profile-icon::after { right: 2px; bottom: 1px; left: 2px; height: 8px; border-radius: 8px 8px 3px 3px; }
.member-chevron { margin-left: 8px; }

@media (min-width: 1300px) {
  .workspace-content--request {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(18rem, 22rem);
    gap: 16px;
    align-items: start;
  }
  .workspace-content--request > .calendar-area { grid-area: 1 / 1; }
  .workspace-content--request > .request-panel { grid-area: 1 / 2; }
  .workspace-content--request :deep(.leave-fields) { grid-template-columns: 1fr; }
}

@media (max-width: 820px) {
  .home-card { grid-template-columns: minmax(0, 1fr); }
  .workspace-sidebar { border-right: 0; border-bottom: 1px solid var(--ritm-divider); }
  .workspace-nav { display: flex; flex-wrap: wrap; }
  .workspace-nav .nav-group-label { flex-basis: 100%; }
}

@media (max-width: 640px) {
  .session-actions { gap: 6px; }
  .new-request .action-menu { right: auto; left: 0; }
  .own-request-details { flex-basis: 100%; }
}
</style>
