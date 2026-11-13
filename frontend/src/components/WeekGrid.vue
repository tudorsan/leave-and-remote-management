<script setup lang="ts">
import { computed } from 'vue'
import CalendarLegend from './CalendarLegend.vue'
import { types, statuses, weekdays, type CalendarEvent } from '../utils/calendarDisplay'

// Cerința 6: vederea săptămânală — membrii echipei pe rânduri, zilele (luni–duminică) pe coloane.
const props = defineProps<{
  days: string[]
  members: { id: number; firstName: string; lastName: string }[]
  events: CalendarEvent[]
  holidays: { date: string; name: string }[]
}>()

const dayFormat = new Intl.DateTimeFormat('ro-RO', { day: 'numeric', month: 'short', timeZone: 'UTC' })

function parseDay(date: string) {
  const [year, month, day] = date.split('-').map(Number)
  const value = new Date(0)
  value.setUTCFullYear(year!, month! - 1, day!)
  return value
}

const columns = computed(() => {
  const holidayNames = new Map(props.holidays.map(holiday => [holiday.date, holiday.name]))
  return props.days.map((date, index) => ({
    date,
    weekday: weekdays[index] ?? '',
    label: dayFormat.format(parseDay(date)),
    weekend: index >= 5,
    holiday: holidayNames.get(date),
  }))
})

const eventsByCell = computed(() => {
  const cells = new Map<string, CalendarEvent[]>()
  for (const event of props.events) {
    const key = `${event.userId}|${event.date}`
    cells.set(key, [...(cells.get(key) ?? []), event])
  }
  return cells
})

function cellEvents(memberId: number, date: string) {
  return eventsByCell.value.get(`${memberId}|${date}`) ?? []
}

function eventClass(event: CalendarEvent) {
  return `bg-${types[event.type].colors[event.status === 'Approved' ? 1 : 0]}`
}
</script>

<template>
  <div class="week-scroll" tabindex="0" role="region" aria-label="Calendarul săptămânal al echipei">
    <table class="week">
      <thead>
        <tr>
          <th scope="col" class="member-column">Membru</th>
          <th
            v-for="column in columns"
            :key="column.date"
            scope="col"
            :class="{ 'week-weekend': column.weekend, 'week-holiday': !!column.holiday }"
          >
            <span class="week-weekday">{{ column.weekday }}</span>
            <time :datetime="column.date">{{ column.label }}</time>
            <span v-if="column.holiday" class="week-holiday-name">{{ column.holiday }}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="member in members" :key="member.id">
          <th scope="row" class="member-column">{{ member.firstName }} {{ member.lastName }}</th>
          <td
            v-for="column in columns"
            :key="column.date"
            :class="{ 'week-weekend': column.weekend, 'week-holiday': !!column.holiday }"
          >
            <span
              v-for="event in cellEvents(member.id, column.date)"
              :key="`${event.type}-${event.requestId}`"
              class="week-event"
              :class="eventClass(event)"
            >
              {{ types[event.type].label }}
              <small>{{ statuses[event.status] }}</small>
            </span>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
  <p v-if="!members.length" class="text-body-2 mt-3">Nu sunt membri de afișat.</p>
  <p v-else-if="!events.length" class="text-body-2 mt-3">Nu sunt cereri care să corespundă selecției pentru această săptămână.</p>
  <CalendarLegend />
</template>

<style scoped>
.week-scroll {
  overflow-x: auto;
  border: 1px solid var(--ritm-calendar-border);
  border-radius: 18px;
  background: #fff;
}
.week-scroll:focus-visible { outline: 3px solid var(--ritm-focus); outline-offset: 3px; }
.week { width: 100%; min-width: 52rem; border-collapse: separate; border-spacing: 0; table-layout: fixed; }
.week th, .week td { padding: 0.55rem; text-align: left; vertical-align: top; border-bottom: 1px solid var(--ritm-border); }
.week th + th, .week td + td, .week th + td { border-left: 1px solid var(--ritm-border); }
.week tbody tr:last-child > * { border-bottom: 0; }
.week thead th { background: var(--ritm-panel-soft); color: var(--ritm-text); font-size: 0.88rem; font-weight: 750; }
.member-column { width: 11rem; color: var(--ritm-accent); font-weight: 700; overflow-wrap: anywhere; }
.week-weekday, .week thead time { display: block; }
.week thead time { color: var(--ritm-muted); font-weight: 600; }
.week-weekend { background: var(--ritm-panel); }
.week-holiday { background: #fbf2dd; }
.week-holiday-name { display: block; margin-top: 2px; color: #644716; font-size: 0.75rem; font-weight: 600; }
.week td { height: 3.5rem; }
.week-event { display: block; margin-bottom: 4px; padding: 0.25rem 0.45rem; border-radius: 0.45rem; font-size: 0.8rem; font-weight: 600; overflow-wrap: anywhere; }
.week-event small { display: block; font-weight: 500; }
</style>
