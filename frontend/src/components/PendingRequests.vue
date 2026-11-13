<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { VAlert } from 'vuetify/components/VAlert'
import { VBtn } from 'vuetify/components/VBtn'
import { VTextarea } from 'vuetify/components/VTextarea'
import { useAuthStore } from '../stores/auth'

const props = defineProps<{ leaveLabels: Record<string, { label: string }> }>()
const emit = defineEmits<{ unauthorized: []; reviewed: [] }>()
const auth = useAuthStore()

interface PendingRequest {
  id: number
  firstName: string
  lastName: string
  comment: string | null
}

type Leave = PendingRequest & { type: string; startDate: string; endDate: string }
type Remote = PendingRequest & { days: string[] }
type ReviewRow = PendingRequest & { endpoint: string; label: string; period: string }

const requests = ref<ReviewRow[]>([])
const loading = ref(false)
const saving = ref(false)
const rejecting = ref<ReviewRow | null>(null)
const reason = ref('')
const error = ref('')
const notice = ref('')

async function loadRequests() {
  loading.value = true
  error.value = ''
  try {
    const headers = { Authorization: `Bearer ${auth.getToken() ?? ''}` }
    const [leave, remote] = await Promise.all([
      fetch('/api/leave-requests/pending', { headers }),
      fetch('/api/remote-requests/pending', { headers }),
    ])
    if (leave.status === 401 || remote.status === 401) {
      emit('unauthorized')
      return
    }
    if (!leave.ok || !remote.ok) throw new Error('Pending requests unavailable')

    const leaves: Leave[] = await leave.json()
    const remotes: Remote[] = await remote.json()
    requests.value = [
      ...leaves.map(request => ({
        ...request,
        endpoint: `/api/leave-requests/${request.id}`,
        label: props.leaveLabels[request.type]?.label ?? request.type,
        period: `${request.startDate} – ${request.endDate}`,
      })),
      ...remotes.map(request => ({
        ...request,
        endpoint: `/api/remote-requests/${request.id}`,
        label: 'Lucru remote',
        period: request.days.join(', '),
      })),
    ]
  } catch {
    error.value = 'Cererile în așteptare nu au putut fi încărcate.'
  } finally {
    loading.value = false
  }
}

async function review(request: ReviewRow, action: 'approve' | 'reject') {
  if (saving.value) return

  saving.value = true
  error.value = ''
  notice.value = ''
  try {
    const response = await fetch(`${request.endpoint}/${action}`, {
      method: 'PATCH',
      headers: {
        Authorization: `Bearer ${auth.getToken() ?? ''}`,
        ...(action === 'reject' ? { 'Content-Type': 'application/json' } : {}),
      },
      body: action === 'reject' ? JSON.stringify({ reason: reason.value.trim() }) : undefined,
    })
    if (response.status === 401) {
      emit('unauthorized')
      return
    }
    if (!response.ok) {
      const result = await response.json().catch(() => null)
      error.value = result?.message || 'Decizia nu a putut fi salvată.'
      return
    }

    requests.value = requests.value.filter(row => row.endpoint !== request.endpoint)
    rejecting.value = null
    reason.value = ''
    notice.value = action === 'approve' ? 'Cererea a fost aprobată.' : 'Cererea a fost respinsă.'
    emit('reviewed')
  } catch {
    error.value = 'Decizia nu a putut fi salvată. Încearcă din nou.'
  } finally {
    saving.value = false
  }
}

onMounted(loadRequests)
</script>

<template>
  <section class="review-panel" aria-labelledby="pending-heading">
    <div class="review-header">
      <h2 id="pending-heading" class="text-h6">Cereri în așteptare</h2>
      <VBtn variant="text" :disabled="loading || saving" @click="loadRequests">Reîncarcă lista</VBtn>
    </div>
    <VAlert v-if="error" class="my-2" color="error" variant="tonal" role="alert">{{ error }}</VAlert>
    <VAlert v-if="notice" class="my-2" color="success" variant="tonal" role="status">{{ notice }}</VAlert>
    <p v-if="loading" role="status">Se încarcă cererile…</p>
    <p v-else-if="!requests.length && !error">Nu sunt cereri în așteptare.</p>
    <ul v-else-if="requests.length" class="review-list">
      <li v-for="request in requests" :key="request.endpoint" class="review-row">
        <div class="review-details">
          <div class="review-title">
            <h3>{{ request.firstName }} {{ request.lastName }}</h3>
            <span class="review-type">{{ request.label }}</span>
          </div>
          <p class="review-period">{{ request.period }}</p>
          <p v-if="request.comment" class="review-comment">{{ request.comment }}</p>
        </div>
        <form v-if="rejecting?.endpoint === request.endpoint" class="rejection-form" @submit.prevent="review(request, 'reject')">
          <VTextarea v-model="reason" label="Motivul respingerii (minimum 10 caractere)" variant="outlined" rows="2" :disabled="saving" />
          <div class="review-actions">
            <VBtn type="submit" color="error" :disabled="saving">Confirmă respingerea</VBtn>
            <VBtn type="button" variant="text" :disabled="saving" @click="rejecting = null">Renunță</VBtn>
          </div>
        </form>
        <div v-else class="review-actions">
          <VBtn color="primary" variant="outlined" :disabled="saving" @click="review(request, 'approve')">Aprobă</VBtn>
          <VBtn color="error" variant="outlined" :disabled="saving" @click="rejecting = request; reason = ''">Respinge</VBtn>
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.review-panel {
  margin-top: 18px;
  padding: clamp(14px, 3vw, 24px);
  border: 1px solid var(--ritm-border);
  border-radius: 16px;
  background: var(--ritm-panel-soft);
}

.review-header, .review-title, .review-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
}

.review-header { justify-content: space-between; margin-bottom: 14px; }
.review-header h2, .review-title h3 { color: var(--ritm-accent); font-weight: 750; }
.review-title h3 { font-size: 16px; }
.review-list { display: grid; gap: 10px; padding: 0; list-style: none; }

.review-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 20px;
  padding: 14px 16px;
  border: 1px solid var(--ritm-border);
  border-radius: 12px;
  background: var(--ritm-surface);
}

.review-details { flex: 1 1 16rem; min-width: 0; overflow-wrap: anywhere; }
.review-type { padding: 2px 9px; border-radius: 999px; background: var(--ritm-badge); color: var(--ritm-accent); font-size: 13px; font-weight: 700; }
.review-period, .review-comment { margin-top: 4px; color: var(--ritm-muted); }
.review-comment { white-space: pre-wrap; }
.rejection-form { flex: 1 0 100%; padding-top: 14px; border-top: 1px solid var(--ritm-border); }

@media (max-width: 640px) {
  .review-details { flex-basis: 100%; }
}
</style>
