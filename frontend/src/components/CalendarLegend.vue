<script setup lang="ts">
import { types, statuses } from '../utils/calendarDisplay'

// Legenda comună pentru vederea lunară și cea săptămânală: o culoare per tip, deschisă = în așteptare, plină = aprobat.
</script>

<template>
  <section class="calendar-legend" aria-labelledby="calendar-legend-title">
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
.legend { list-style: none; padding: 0; }
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
.event { border-radius: 999px; padding: 0.15rem 0.45rem; font-size: 0.8rem; font-weight: 650; }
</style>
