<script setup lang="ts">
defineProps<{ balance: { availableDays: number; usedDays: number; pendingDays: number; totalDays: number } }>()
</script>

<template>
  <dl class="balance-grid">
    <div class="balance-available"><dt>Zile rămase</dt><dd>{{ balance.availableDays }}</dd></div>
    <div><dt>Zile consumate</dt><dd>{{ balance.usedDays }}</dd></div>
    <div class="balance-pending"><dt>Zile blocate</dt><dd>{{ balance.pendingDays }}</dd><dd class="balance-note">În cereri în așteptare</dd></div>
    <div><dt>Zile alocate</dt><dd>{{ balance.totalDays }}</dd></div>
  </dl>
</template>

<style scoped>
.balance-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 10rem), 1fr)); gap: 10px; }
.balance-grid > div { padding: 14px 16px; border: 1px solid var(--ritm-border); border-radius: 12px; background: var(--ritm-surface); }
.balance-grid .balance-available { border-color: var(--ritm-balance-border); background: var(--ritm-highlight); }
.balance-grid .balance-pending { border-color: #e8d9ad; background: #faf3df; }
.balance-grid dt { color: var(--ritm-label); font-weight: 700; }
.balance-grid dd { color: var(--ritm-accent); font-size: clamp(26px, 4vw, 36px); font-weight: 750; line-height: 1.2; font-variant-numeric: tabular-nums; }
.balance-grid .balance-note { margin-top: 4px; color: #654e26; font-size: 13px; font-weight: 500; }
</style>
