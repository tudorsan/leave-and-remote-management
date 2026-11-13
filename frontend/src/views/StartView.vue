<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()
const opening = ref(false)
const expanding = ref(false)
const book = ref<HTMLElement | null>(null)
const bookBounds = ref<Record<string, string>>({})
const liftDuration = 650
const coverDuration = 340
const pageDuration = 140
const finalPageDurations = [340, 260, 190]
const currentMonthPause = 220
const expansionDuration = 450
let expansionTimer: number | undefined
let navigationTimer: number | undefined

function enterApplication() {
  if (opening.value) return

  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    void router.push({ name: 'login' })
    return
  }

  opening.value = true
  const expansionStart = (pages.at(-1)?.delay ?? liftDuration + coverDuration) + currentMonthPause
  expansionTimer = window.setTimeout(() => {
    const bounds = book.value?.getBoundingClientRect()
    if (!bounds) return
    bookBounds.value = {
      '--book-top': `${bounds.top}px`,
      '--book-left': `${bounds.left}px`,
      '--book-width': `${bounds.width}px`,
      '--book-height': `${bounds.height}px`,
    }
    expanding.value = true
  }, expansionStart)
  navigationTimer = window.setTimeout(async () => {
    try {
      await router.push({ name: 'login' })
    } finally {
      opening.value = false
      expanding.value = false
    }
  }, expansionStart + expansionDuration)
}

onBeforeUnmount(() => {
  window.clearTimeout(expansionTimer)
  window.clearTimeout(navigationTimer)
})

const today = new Date()
const year = today.getFullYear()
const month = today.getMonth()
const monthFormat = new Intl.DateTimeFormat('ro-RO', { month: 'long', year: 'numeric' })
let nextPageDelay = liftDuration + coverDuration
const pages = Array.from({ length: month + 1 }, (_, index) => {
  const firstDay = new Date(year, index, 1)
  const delay = nextPageDelay
  const duration = finalPageDurations[month - index - 1] ?? pageDuration
  nextPageDelay += duration
  return {
    label: monthFormat.format(firstDay),
    firstDayOffset: (firstDay.getDay() + 6) % 7,
    days: new Date(year, index + 1, 0).getDate(),
    isCurrent: index === month,
    delay,
    duration,
  }
})
const weekdays = ['Lu', 'Ma', 'Mi', 'Jo', 'Vi', 'Sâ', 'Du']

const features = [
  { title: 'Calendar de echipă', detail: 'Vezi dintr-o privire concediile și zilele remote.' },
  { title: 'Cereri și aprobări', detail: 'Trimite o cerere și urmărește-i starea.' },
  { title: 'Sold de concediu', detail: 'Vezi zilele folosite, rezervate și disponibile.' },
]
</script>

<template>
  <div class="start-page">
    <header class="start-header shell">
      <div class="brand">
        <span class="brand-mark" aria-hidden="true">
          <svg width="23" height="23" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round">
            <rect x="3.5" y="5.5" width="17" height="15" rx="3" />
            <path d="M8 3.5v4m8-4v4M4 10h16m-12 5h3m2 0h3" />
          </svg>
        </span>
        Ritm<span class="brand-caption">Planificarea echipei</span>
      </div>
    </header>

    <section class="hero shell" aria-labelledby="start-title">
      <div class="hero-copy">
        <h1 id="start-title">Concedii și lucru remote</h1>
        <p class="intro">Planifică zilele de lucru remote, verifică disponibilitatea echipei și urmărește cererile de concediu până la aprobare.</p>
      </div>

      <figure class="preview">
        <div
          ref="book"
          class="calendar-book"
          :class="{ 'is-opening': opening, 'is-expanding': expanding }"
          :style="{
            ...bookBounds,
            '--lift-duration': liftDuration + 'ms',
            '--cover-duration': coverDuration + 'ms',
            '--expansion-duration': expansionDuration + 'ms',
          }"
        >
          <div
            v-for="(page, index) in pages"
            :key="page.label"
            class="month-page"
            :class="{ 'current-month': page.isCurrent }"
            :style="{
              '--page-order': pages.length - index,
              '--page-delay': page.delay + 'ms',
              '--page-duration': page.duration + 'ms',
            }"
            aria-hidden="true"
          >
            <div class="month-content">
              <p class="month-title">{{ page.label }}</p>
              <div class="month-grid">
                <span v-for="day in weekdays" :key="day" class="weekday">{{ day }}</span>
                <span
                  v-for="day in page.days"
                  :key="day"
                  :class="{ today: page.isCurrent && day === today.getDate() }"
                  :style="day === 1 ? { gridColumnStart: page.firstDayOffset + 1 } : undefined"
                >{{ day }}</span>
              </div>
            </div>
          </div>
          <div class="cover-rings" aria-hidden="true">
            <span v-for="ring in 6" :key="ring"></span>
          </div>
          <button class="calendar-cover" type="button" :aria-disabled="opening" aria-label="Accesează calendarul" @click="enterApplication">
            <span class="cover-label">
              <span>Ritm</span>
              <strong>Calendar</strong>
              <span class="primary-action">Accesează calendarul</span>
            </span>
          </button>
        </div>
      </figure>
    </section>

    <ul class="features shell" aria-label="Funcționalități">
      <li v-for="feature in features" :key="feature.title">
        <div><h2>{{ feature.title }}</h2><p>{{ feature.detail }}</p></div>
      </li>
    </ul>

  </div>
</template>

<style scoped>
.start-page {
  --start-paper: #f7f5ef;
  --start-ink: #202733;
  --start-muted: #565960;
  --start-line: #dcd7ca;
  min-height: 100dvh;
  padding-bottom: 32px;
  background: radial-gradient(ellipse at 75% 42%, #ebe7dd, transparent 45%), var(--start-paper);
  color: var(--start-ink);
}

.shell { width: min(1280px, calc(100% - 96px)); margin-inline: auto; }
.start-header { display: flex; align-items: center; min-height: 82px; border-bottom: 1px solid var(--start-line); }
.brand { display: flex; align-items: center; gap: 11px; font-size: 26px; font-weight: 750; letter-spacing: -.045em; }
.brand-mark { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 10px; background: var(--start-ink); color: #f5ead6; }
.brand-caption { border-left: 1px solid var(--start-line); margin-left: 5px; padding-left: 16px; color: var(--start-muted); font-size: 15px; font-weight: 600; letter-spacing: 0; }
.primary-action { margin-top: 18px; padding: 11px 20px; border: 1px solid #e6d2ad; border-radius: 9px; background: #f1e5cd; color: var(--start-ink); font-size: clamp(16px, 1.5vw, 20px); font-weight: 750; }
.calendar-cover:focus-visible { outline: 3px solid #80643b; outline-offset: 5px; }
@media (hover: hover) {
  .calendar-cover:hover .primary-action { background: #fff4df; }
}

@media (prefers-reduced-motion: no-preference) {
  .primary-action {
    box-shadow: 0 4px 0 #a98d5f;
    transition: transform 160ms ease-out, box-shadow 160ms ease-out;
  }

  .calendar-cover:active .primary-action {
    transform: translateY(4px);
    box-shadow: none;
    transition-duration: 60ms;
  }
}

.hero { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); align-items: center; gap: clamp(24px, 4vw, 60px); padding-block: clamp(42px, 6vw, 80px); }
.hero h1 { max-width: 13ch; font-family: Georgia, 'Times New Roman', serif; font-size: clamp(46px, 6vw, 88px); line-height: 1.04; font-weight: 400; letter-spacing: -.055em; }
.intro { max-width: 40ch; margin-top: 24px; color: var(--start-muted); font-size: clamp(16px, 1.5vw, 19px); line-height: 1.65; }
.preview { min-width: 0; }
.calendar-book {
  --resting-transform: perspective(1100px) rotateX(36deg) rotateZ(-90deg);
  position: relative;
  min-height: clamp(320px, 38vw, 520px);
  border-radius: 16px;
  background: #e9e3d5;
  box-shadow: 0 5px 0 #c8beab;
}

.calendar-book:not(.is-opening) { transform: var(--resting-transform); }

.month-page {
  position: absolute;
  z-index: var(--page-order);
  inset: 0;
  padding: clamp(16px, 2vw, 26px);
  border-radius: 16px;
  background: var(--start-paper);
  color: var(--start-ink);
  transform-origin: center top;
  backface-visibility: hidden;
}

.month-content { width: min(100%, 720px); margin-inline: auto; }
.month-title {
  margin-bottom: 8px;
  font-size: clamp(22px, 2.7vw, 32px);
  font-weight: 750;
  text-transform: capitalize;
}

.month-grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 2px;
  text-align: center;
  font-size: clamp(15px, 1.7vw, 20px);
  font-variant-numeric: tabular-nums;
}

.month-grid span { padding-block: 2px; }
.month-grid .weekday { color: var(--start-muted); font-weight: 750; }
.month-grid .today { border-radius: 6px; background: var(--start-ink); color: #fff; font-weight: 750; }

.calendar-cover {
  position: absolute;
  z-index: 14;
  inset: 0;
  width: 100%;
  display: grid;
  place-items: center;
  border: 1px solid #54565b;
  border-radius: 16px;
  background: linear-gradient(155deg, #414751, #202733 67%);
  color: #f9f3e8;
  box-shadow: inset 0 0 0 9px #ffffff0a, 0 5px 0 #bdb29e, 0 18px 36px #20273338;
  transform-origin: center top;
  backface-visibility: hidden;
  cursor: pointer;
}

.cover-rings {
  position: absolute;
  z-index: 15;
  inset: -7px 13% auto;
  display: flex;
  justify-content: space-between;
  pointer-events: none;
}

.cover-rings span {
  width: 13px;
  height: 25px;
  border: 3px solid #ccb184;
  border-radius: 8px;
  box-shadow: 1px 2px 2px #1a1c2459;
}

.cover-label {
  display: grid;
  gap: 4px;
  justify-items: center;
  text-align: center;
}

.calendar-book:not(.is-opening) .cover-label { transform: rotate(90deg); }

.cover-label strong {
  font-family: Georgia, serif;
  font-size: clamp(44px, 7vw, 86px);
  line-height: 1.05;
  letter-spacing: -.04em;
  font-weight: 700;
}

.cover-label > span:first-child { color: #ebd6b1; font-size: 19px; font-weight: 750; letter-spacing: .06em; }

@media (prefers-reduced-motion: no-preference) {
  .calendar-book.is-opening:not(.is-expanding) {
    animation: lift-calendar var(--lift-duration) cubic-bezier(.22, .75, .25, 1) both;
  }

  .is-opening .cover-label {
    animation: align-cover-label var(--lift-duration) cubic-bezier(.22, .75, .25, 1) both;
  }

  .is-opening .calendar-cover {
    animation: flip-sheet var(--cover-duration) ease-in-out var(--lift-duration) forwards;
  }

  .is-opening .month-page:not(.current-month) {
    animation: flip-sheet var(--page-duration) ease-in-out var(--page-delay) forwards;
  }

  .is-expanding .current-month {
    position: fixed;
    z-index: 20;
    display: grid;
    align-items: center;
    animation: expand-month var(--expansion-duration) cubic-bezier(.2, .75, .3, 1) both;
  }
}

@keyframes lift-calendar {
  from { transform: var(--resting-transform); }
  48% { transform: perspective(1100px) rotateX(20deg) rotateZ(-58deg) scale(.98); }
  to { transform: perspective(1100px) rotateX(0deg) rotateZ(0deg) scale(1); }
}

@keyframes align-cover-label {
  from { transform: rotate(90deg); }
  to { transform: none; }
}

@keyframes flip-sheet {
  from { transform: perspective(1000px) rotateX(0); }
  to { transform: perspective(1000px) rotateX(112deg); }
}

@keyframes expand-month {
  from { inset: var(--book-top) auto auto var(--book-left); width: var(--book-width); height: var(--book-height); }
  to { inset: 0; width: 100%; height: 100dvh; border-radius: 0; }
}

.features { display: grid; grid-template-columns: repeat(3, 1fr); list-style: none; padding: 0; border-block: 1px solid var(--start-line); }
.features li { padding: 24px clamp(14px, 3vw, 36px); }
.features li + li { border-left: 1px solid var(--start-line); }
.features h2 { font-size: 18px; font-weight: 750; }
.features p { margin-top: 5px; color: var(--start-muted); font-size: 15px; }

@media (max-width: 900px) {
  .shell { width: calc(100% - 48px); }
  .hero { grid-template-columns: 1fr; }
  .hero h1 { max-width: 16ch; }
  .preview { width: min(100%, 600px); }
  .calendar-book { min-height: clamp(270px, 65vw, 460px); }
}

@media (max-width: 600px) {
  .shell { width: calc(100% - 32px); }
  .start-header { min-height: 70px; }
  .brand-caption { display: none; }
  .hero h1 { font-size: clamp(38px, 10vw, 60px); }
  .features { grid-template-columns: 1fr; }
  .features li { padding: 15px 0; }
  .features li + li { border-left: 0; border-top: 1px solid var(--start-line); }
}
</style>
