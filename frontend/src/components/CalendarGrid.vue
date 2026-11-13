<script setup lang="ts">
import { VBtn } from 'vuetify/components/VBtn'
import { types, statuses, weekdays, type CalendarEvent } from '../utils/calendarDisplay'

defineProps<{
  monthLabel: string
  weeks: ({ date: string; day: number; holiday?: string; events: CalendarEvent[] } | null)[][]
  memberNames: Map<number, string>
  remoteFormOpen: boolean
  remoteDays: string[]
  remoteSubmitting: boolean
  hasEvents: boolean
  showLegend: boolean
}>()
const monthTurn = defineModel<-1 | 0 | 1>('monthTurn', { required: true })
const emit = defineEmits<{ toggleRemoteDay: [date: string] }>()
</script>

<template>
  <div
    class="calendar-scroll"
    :class="{ 'month-turn-next': monthTurn === 1, 'month-turn-previous': monthTurn === -1 }"
    tabindex="0"
    role="region"
    aria-label="Grila calendarului lunar"
    @animationend.self="monthTurn = 0"
  >
    <table class="calendar" :aria-label="`Calendar pentru ${monthLabel}`">
      <thead>
        <tr><th v-for="weekday in weekdays" :key="weekday" scope="col">{{ weekday }}</th></tr>
      </thead>
      <tbody>
        <tr v-for="(week, index) in weeks" :key="index">
          <td
            v-for="(cell, weekday) in week"
            :key="weekday"
            :class="{
              'outside-month': !cell,
              'calendar-holiday': !!cell?.holiday,
              'remote-day-selected': remoteFormOpen && cell && remoteDays.includes(cell.date),
            }"
          >
            <template v-if="cell">
              <VBtn
                v-if="remoteFormOpen"
                type="button"
                size="small"
                color="primary"
                :variant="remoteDays.includes(cell.date) ? 'flat' : 'outlined'"
                :aria-label="`Zi remote ${cell.date}`"
                :aria-pressed="remoteDays.includes(cell.date)"
                :disabled="remoteSubmitting"
                @click="emit('toggleRemoteDay', cell.date)"
              ><time :datetime="cell.date">{{ cell.day }}</time></VBtn>
              <time v-else :datetime="cell.date">{{ cell.day }}</time>
              <div v-if="cell.holiday" class="holiday-marker">
                <strong>Sărbătoare legală</strong>
                <span>{{ cell.holiday }}</span>
              </div>
              <ul v-if="cell.events.length" class="day-events">
                <li
                  v-for="event in cell.events"
                  :key="`${event.type}-${event.requestId}`"
                  class="event"
                  :class="`bg-${types[event.type].colors[event.status === 'Approved' ? 1 : 0]}`"
                >
                  <strong>{{ memberNames.get(event.userId) }}</strong>
                  <span>{{ types[event.type].label }} · {{ statuses[event.status] }}</span>
                </li>
              </ul>
            </template>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
  <p v-if="!hasEvents" class="text-body-2 mt-3">
    Nu sunt cereri care să corespundă selecției pentru această lună.
  </p>

  <section v-if="showLegend" class="calendar-legend" aria-labelledby="calendar-legend-title">
    <h3 id="calendar-legend-title">Legenda calendarului</h3>
    <ul class="legend">
      <li v-for="type in types" :key="type.label">
        <strong>{{ type.label }}</strong>
        <div class="legend-statuses">
          <span class="event" :class="`bg-${type.colors[0]}`">{{ statuses.Pending }}</span>
          <span class="event" :class="`bg-${type.colors[1]}`">{{ statuses.Approved }}</span>
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.day-events, .legend {
  list-style: none;
  padding: 0;
}

.calendar-scroll {
  overflow-x: auto;
  border: 1px solid var(--ritm-calendar-border);
  border-radius: 18px;
  background: #fff;
  box-shadow: 0 8px 22px color-mix(in srgb, var(--ritm-accent) 8%, transparent);
}
.calendar-scroll:focus-visible { outline: 3px solid var(--ritm-focus); outline-offset: 3px; }

@media (prefers-reduced-motion: no-preference) {
  .month-turn-next, .month-turn-previous {
    animation: calendar-month-turn 380ms cubic-bezier(.2, .7, .3, 1) both;
  }
  .month-turn-next { --month-turn-angle: -55deg; transform-origin: left center; }
  .month-turn-previous { --month-turn-angle: 55deg; transform-origin: right center; }
}

@keyframes calendar-month-turn {
  from { opacity: .25; transform: perspective(1400px) rotateY(var(--month-turn-angle)); }
  to { opacity: 1; transform: perspective(1400px) rotateY(0); }
}
.calendar {
  width: 100%;
  min-width: 44rem;
  border-collapse: separate;
  border-spacing: 0;
  table-layout: fixed;
}

.calendar th, .calendar td {
  padding: 0.55rem;
  text-align: left;
}

.calendar th { padding: 0.7rem 0.65rem; border-bottom: 1px solid var(--ritm-calendar-border); background: var(--ritm-panel-soft); color: var(--ritm-text); font-size: 0.88rem; font-weight: 750; }
.calendar th:nth-last-child(-n + 2) { color: var(--ritm-muted); }
.calendar td { height: 6rem; border-bottom: 1px solid var(--ritm-border); vertical-align: top; }
.calendar td + td { border-left: 1px solid var(--ritm-border); }
.calendar tr:last-child td { border-bottom: 0; }
.calendar td:nth-last-child(-n + 2) { background: var(--ritm-panel); }
.calendar td.outside-month { background: var(--ritm-outside); }
.calendar td:not(.outside-month) { position: relative; background-color: var(--ritm-surface); }
.calendar td:nth-last-child(-n + 2):where(:not(.outside-month)) { background-color: var(--ritm-panel); }
@media (hover: hover) and (prefers-reduced-motion: no-preference) {
  .calendar td:not(.outside-month) { transition: transform 150ms ease-out, box-shadow 150ms ease-out; }
  .calendar td:not(.outside-month):hover {
    z-index: 1;
    transform: scale(1.04);
    box-shadow: 0 3px 12px color-mix(in srgb, var(--ritm-accent) 24%, transparent);
  }
  .calendar td:first-child { transform-origin: left center; }
  .calendar td:last-child { transform-origin: right center; }
}
.calendar td.remote-day-selected { background: var(--ritm-highlight); box-shadow: inset 0 0 0 2px var(--ritm-outline); }
.calendar td.remote-day-selected:hover { box-shadow: inset 0 0 0 2px var(--ritm-outline); }
.calendar td.calendar-holiday { background: #fbf2dd; }
.holiday-marker { margin-top: 5px; color: #644716; font-size: 0.78rem; line-height: 1.35; overflow-wrap: anywhere; }
.holiday-marker strong, .holiday-marker span { display: block; }
.holiday-marker strong { font-weight: 750; }
.calendar td > time { display: inline-grid; min-width: 2rem; height: 2rem; place-items: center; border-radius: 8px; background: var(--ritm-highlight); color: var(--ritm-accent); font-size: 0.95rem; font-weight: 750; }
.day-events { display: grid; gap: 0.25rem; margin-top: 0.2rem; }
.event { border-radius: 0.45rem; padding: 0.25rem 0.45rem; font-size: 0.8rem; overflow-wrap: anywhere; }
.event strong, .event span { display: block; }
.event strong { font-weight: 600; }
.calendar-legend {
  margin-top: 24px;
  padding: clamp(14px, 2.5vw, 24px);
  border: 1px solid var(--ritm-border);
  border-radius: 16px;
  background: var(--ritm-panel);
}
.calendar-legend h3 { margin-bottom: 12px; color: var(--ritm-accent); font-size: 18px; font-weight: 750; }
.legend { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 10.75rem), 1fr)); gap: 8px; }
.legend li { padding: 10px 12px; border: 1px solid var(--ritm-border); border-radius: 10px; background: var(--ritm-surface); }
.legend li > strong { display: block; margin-bottom: 6px; color: var(--ritm-accent); font-size: 14px; font-weight: 750; }
.legend-statuses { display: flex; flex-wrap: wrap; gap: 5px; }
.legend-statuses .event { padding: 0.15rem 0.45rem; border-radius: 999px; font-weight: 650; }

</style>
