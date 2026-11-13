<script setup lang="ts">
import { ref, watch } from 'vue'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { useAuthStore } from '../stores/auth'
import LeaveBalanceSummary from './LeaveBalanceSummary.vue'

const props = defineProps<{ revision: number }>()
const emit = defineEmits<{ unauthorized: [] }>()
const auth = useAuthStore()

interface LeaveBalance {
  year: number
  totalDays: number
  usedDays: number
  pendingDays: number
  availableDays: number
}

const balance = ref<LeaveBalance | null>(null)
const loading = ref(false)
const error = ref('')
let latestLoad = 0

async function loadBalance() {
  const load = ++latestLoad
  loading.value = true
  error.value = ''
  try {
    const response = await fetch('/api/leave-balances/me', {
      headers: { Authorization: `Bearer ${auth.getToken() ?? ''}` },
    })
    if (load !== latestLoad) return
    if (response.status === 401) {
      emit('unauthorized')
      return
    }
    if (!response.ok) {
      const result = await response.json().catch(() => null)
      throw new Error(result?.message || 'Soldul de concediu nu a putut fi încărcat.')
    }
    const result: LeaveBalance = await response.json()
    if (load === latestLoad) balance.value = result
  } catch (failure) {
    if (load === latestLoad) {
      balance.value = null
      error.value = failure instanceof Error ? failure.message : 'Soldul de concediu nu a putut fi încărcat.'
    }
  } finally {
    if (load === latestLoad) loading.value = false
  }
}

watch(() => props.revision, loadBalance, { immediate: true })
</script>

<template>
  <section class="balance-panel" aria-labelledby="own-balance-heading">
    <div class="balance-header">
      <div>
        <h2 id="own-balance-heading" class="text-h6">Soldul meu de concediu</h2>
        <p v-if="balance && !loading">Concediu de odihnă pentru {{ balance.year }}</p>
      </div>
      <VBtn variant="text" :disabled="loading" @click="loadBalance">Reîncarcă soldul</VBtn>
    </div>
    <p v-if="loading" role="status">Se încarcă soldul…</p>
    <VAlert v-else-if="error" class="my-2" color="error" variant="tonal" role="alert">{{ error }}</VAlert>
    <LeaveBalanceSummary v-else-if="balance" :balance="balance" />
  </section>
</template>

<style scoped>
.balance-panel {
  margin-top: 18px;
  padding: clamp(14px, 3vw, 24px);
  border: 1px solid var(--ritm-border);
  border-radius: 16px;
  background: var(--ritm-panel-soft);
}

.balance-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px 16px;
  margin-bottom: 16px;
}

.balance-header h2 { color: var(--ritm-accent); font-weight: 750; }
.balance-header p { color: var(--ritm-muted); }
</style>
